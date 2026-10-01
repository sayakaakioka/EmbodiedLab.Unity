#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity.Internal;

namespace EmbodiedLab.Unity
{
    /// <summary>
    /// A stateful handle for one EmbodiedLab cloud training job.
    /// </summary>
    public sealed class EmbodiedLabJob : IDisposable
    {
        private readonly object gate = new();
        private readonly EmbodiedLabTransport transport;
        private readonly SynchronizationContext? synchronizationContext;
        private readonly CancellationTokenSource lifetimeCancellation = new();

        private ResultSnapshot? latestResult;
        private MonitorState? monitor;
        private bool disposed;

        internal EmbodiedLabJob(
            EmbodiedLabTransport transport,
            string submissionId,
            string scenarioId,
            string? cancelToken,
            SynchronizationContext? synchronizationContext)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            SubmissionId = RequireValue(submissionId, nameof(submissionId));
            ScenarioId = RequireValue(scenarioId, nameof(scenarioId));
            CancelToken = string.IsNullOrWhiteSpace(cancelToken) ? null : cancelToken;
            this.synchronizationContext = synchronizationContext;
        }

        /// <summary>Notifies consumers of an accepted immutable result snapshot.</summary>
        /// <remarks>
        /// Uses the SynchronizationContext captured by SubmitAsync or Restore. Create the job on
        /// Unity's main thread for main-thread notifications. Without a context, handlers run on
        /// the publishing thread, which can be a transport worker. Queued notifications are
        /// suppressed after Dispose or when superseded by a newer accepted snapshot. Handlers must
        /// not throw or block. Handler exceptions propagate on the notification thread and may
        /// disrupt monitoring. No notification is raised for a rejected stale result.
        /// </remarks>
        public event Action<ResultSnapshot>? ResultUpdated;

        /// <summary>Gets the server submission identity.</summary>
        public string SubmissionId { get; }

        /// <summary>Gets the expected scenario identity used to validate results and Replay.</summary>
        public string ScenarioId { get; }

        /// <summary>Gets the secret cloud-cancellation capability, or null for a read-only handle. Do not log it.</summary>
        public string? CancelToken { get; }

        /// <summary>Gets whether this handle holds a cloud-cancellation capability.</summary>
        public bool CanCancel => CancelToken != null;

        /// <summary>Gets the last accepted immutable snapshot, or null before the first result. Safe to retain after disposal.</summary>
        public ResultSnapshot? LatestResult
        {
            get
            {
                lock (gate)
                {
                    return latestResult;
                }
            }
        }

        /// <summary>Gets whether the latest accepted state is completed, failed, or cancelled.</summary>
        public bool IsTerminal
        {
            get
            {
                ResultSnapshot? result = LatestResult;
                return result != null && IsTerminalStatus(result.Status);
            }
        }

        /// <summary>Submits a scenario and returns an owned job handle; the server dispatches training.</summary>
        /// <remarks>Captures the current SynchronizationContext. Dispose the returned handle when finished.
        /// Cancelling the HTTP request does not guarantee that the server has not accepted the job.
        /// The existing bounded transport recovery uses the same submission identity and capability.</remarks>
        /// <param name="endpoints">Validated API and result-stream endpoints.</param>
        /// <param name="scenario">Scenario to submit; do not mutate it while submission is pending.</param>
        /// <param name="cancellationToken">Cancels local submission/recovery, not accepted cloud training.</param>
        /// <returns>A disposable handle. Monitoring begins with WaitForCompletionAsync.</returns>
        /// <exception cref="ArgumentNullException">Endpoints or scenario are null.</exception>
        /// <exception cref="OperationCanceledException">Local submission was cancelled.</exception>
        public static async Task<EmbodiedLabJob> SubmitAsync(
            EmbodiedLabEndpoints endpoints,
            ScenarioBundle scenario,
            CancellationToken cancellationToken = default)
        {
            if (endpoints == null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            SynchronizationContext? context = SynchronizationContext.Current;
            var transport = new EmbodiedLabTransport(
                endpoints.ApiBaseUri,
                endpoints.ResultWebSocketBaseUri);
            try
            {
                return await SubmitAsync(transport, scenario, context, cancellationToken);
            }
            catch
            {
                transport.Dispose();
                throw;
            }
        }

        internal static async Task<EmbodiedLabJob> SubmitAsync(
            EmbodiedLabTransport transport,
            ScenarioBundle scenario,
            SynchronizationContext? synchronizationContext,
            CancellationToken cancellationToken)
        {
            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            SubmissionResponse submission = await transport.SubmitAsync(
                scenario,
                cancellationToken);
            string submissionId = RequireValue(
                submission.SubmissionId,
                nameof(submission.SubmissionId));
            string cancelToken = RequireValue(
                submission.CancelToken,
                nameof(submission.CancelToken));
            var job = new EmbodiedLabJob(
                transport,
                submissionId,
                RequireValue(scenario.ScenarioId, nameof(scenario.ScenarioId)),
                cancelToken,
                synchronizationContext);
            return job;
        }

        /// <summary>Creates a local handle without making a request or restarting cloud training.</summary>
        /// <remarks>Captures the current SynchronizationContext. The caller owns and must dispose the handle.</remarks>
        /// <param name="endpoints">Validated service endpoints.</param>
        /// <param name="submissionId">Existing nonempty submission identity.</param>
        /// <param name="scenarioId">Expected nonempty scenario identity.</param>
        /// <param name="cancelToken">Optional secret capability; absent means cloud cancellation is unavailable.</param>
        /// <exception cref="ArgumentException">An identity is empty.</exception>
        /// <exception cref="ArgumentNullException">Endpoints are null.</exception>
        public static EmbodiedLabJob Restore(
            EmbodiedLabEndpoints endpoints,
            string submissionId,
            string scenarioId,
            string? cancelToken = null)
        {
            if (endpoints == null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            var transport = new EmbodiedLabTransport(
                endpoints.ApiBaseUri,
                endpoints.ResultWebSocketBaseUri);
            try
            {
                return new EmbodiedLabJob(
                    transport,
                    submissionId,
                    scenarioId,
                    cancelToken,
                    SynchronizationContext.Current);
            }
            catch
            {
                transport.Dispose();
                throw;
            }
        }

        /// <summary>Waits for a terminal result using the job's single shared result monitor.</summary>
        /// <remarks>
        /// The first waiter starts monitoring. Cancelling any caller only cancels that caller's
        /// wait; monitoring continues even when no waiters remain. If cancellation is observed
        /// before this wait returns, cancellation wins over a concurrently completed result. A terminal refresh or cloud
        /// cancellation response also completes all waiters. After a monitoring failure or an
        /// awaited StopMonitoringAsync, a subsequent call may start a new monitor.
        /// </remarks>
        /// <param name="cancellationToken">Cancels this local wait only, never the cloud job.</param>
        /// <returns>The immutable terminal result shared by waiters.</returns>
        /// <exception cref="ObjectDisposedException">The job has been disposed.</exception>
        /// <exception cref="OperationCanceledException">This wait, monitoring, or the job was stopped.</exception>
        public async Task<ResultSnapshot> WaitForCompletionAsync(
            CancellationToken cancellationToken = default)
        {
            MonitorState state;
            bool start = false;
            lock (gate)
            {
                ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                if (latestResult != null && IsTerminalStatus(latestResult.Status))
                {
                    return latestResult;
                }

                if (monitor == null)
                {
                    monitor = new MonitorState(lifetimeCancellation.Token);
                    start = true;
                }

                state = monitor;
            }

            if (start)
            {
                _ = RunMonitorAsync(state);
            }

            return await WaitForCallerAsync(state.Completion.Task, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Stops local monitoring, cancels its pending waits, and awaits monitor cleanup.</summary>
        /// <remarks>
        /// Does not cancel the cloud job or unrelated refresh/download requests, clear the latest
        /// result, or dispose the job. Await completion before requesting a fresh monitor.
        /// With no active monitor this is a no-op. Call CancelAsync to cancel cloud training.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The job has been disposed.</exception>
        public Task StopMonitoringAsync()
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (monitor == null)
                {
                    return Task.CompletedTask;
                }

                MonitorState state = monitor;
                state.Completion.TrySetCanceled();
                state.Cancellation.Cancel();
                return state.Stopped.Task;
            }
        }

        private async Task RunMonitorAsync(MonitorState state)
        {
            Exception? failure = null;
            try
            {
                await transport.MonitorResultAsync(
                    SubmissionId,
                    result => PublishResult(result, state),
                    state.Cancellation.Token).ConfigureAwait(false);
                lock (gate)
                {
                    if (latestResult != null && IsTerminalStatus(latestResult.Status))
                    {
                        state.Completion.TrySetResult(latestResult);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "EmbodiedLab monitoring ended without a terminal result.");
                    }
                }
            }
            catch (OperationCanceledException) when (state.Cancellation.IsCancellationRequested)
            {
                // Publish cancellation only after removing this monitor from the job.
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                lock (gate)
                {
                    if (ReferenceEquals(monitor, state))
                    {
                        monitor = null;
                    }

                    state.Cancellation.Dispose();
                    // A caller that observes failure can immediately start a fresh monitor.
                    if (failure != null)
                    {
                        state.Completion.TrySetException(failure);
                        // Observe failures even when every caller has left its local wait.
                        _ = state.Completion.Task.Exception;
                    }
                    else
                    {
                        state.Completion.TrySetCanceled();
                    }

                    state.Stopped.TrySetResult(true);
                }
            }
        }

        private static async Task<ResultSnapshot> WaitForCallerAsync(
            Task<ResultSnapshot> completion,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return await completion.ConfigureAwait(false);
            }

            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                await Task.WhenAny(completion, cancelled.Task).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return await completion.ConfigureAwait(false);
            }
        }

        private sealed class MonitorState
        {
            internal readonly CancellationTokenSource Cancellation;
            internal readonly TaskCompletionSource<ResultSnapshot> Completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource<bool> Stopped =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal MonitorState(CancellationToken lifetimeToken)
            {
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            }
        }

        /// <summary>Fetches and publishes a result, rejecting stale state and terminal rollback.</summary>
        /// <remarks>A terminal response completes shared waiters and stops the local monitor.</remarks>
        /// <param name="cancellationToken">Cancels only this HTTP refresh.</param>
        /// <returns>The latest accepted immutable snapshot, which may be newer than the response.</returns>
        /// <exception cref="ObjectDisposedException">The job is disposed.</exception>
        /// <exception cref="InvalidOperationException">The response belongs to another submission or scenario.</exception>
        /// <exception cref="OperationCanceledException">The request or job was stopped.</exception>
        public async Task<ResultSnapshot> RefreshAsync(
            CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource operationCancellation =
                CreateOperationCancellationThreadSafe(cancellationToken);
            ResultDocument result = await transport.GetResultAsync(
                SubmissionId,
                operationCancellation.Token);
            return PublishResult(result);
        }

        /// <summary>Requests cloud cancellation using this handle's capability.</summary>
        /// <remarks>The only lifecycle operation that cancels cloud training. A cancelling response
        /// is not terminal; continue waiting for cancellation to finish. Local request cancellation
        /// does not undo a request already accepted by the server.</remarks>
        /// <param name="cancellationToken">Cancels this local HTTP operation only.</param>
        /// <returns>The latest accepted immutable snapshot.</returns>
        /// <exception cref="InvalidOperationException">No capability is available or response identity differs.</exception>
        /// <exception cref="ObjectDisposedException">The job is disposed.</exception>
        /// <exception cref="OperationCanceledException">The request or job was stopped.</exception>
        public async Task<ResultSnapshot> CancelAsync(
            CancellationToken cancellationToken = default)
        {
            string cancelToken = CancelToken ?? throw new InvalidOperationException(
                "This job was restored without its cancellation capability token.");
            using CancellationTokenSource operationCancellation =
                CreateOperationCancellationThreadSafe(cancellationToken);
            ResultDocument result = await transport.CancelAsync(
                SubmissionId,
                cancelToken,
                operationCancellation.Token);
            return PublishResult(result);
        }

        /// <summary>Downloads and validates the selected artifact before replacing the destination.</summary>
        /// <remarks>Uses the accepted result metadata and existing size/SHA-256 validation.
        /// Limits are 1 GiB for ONNX, 1 MiB for a manifest, and 64 MiB for compressed Replay;
        /// Replay parsing also retains its identity and resource limits. Failure or cancellation
        /// preserves an existing destination and removes the temporary download.</remarks>
        /// <param name="destinationPath">Caller-selected final file path.</param>
        /// <param name="cancellationToken">Cancels this download only.</param>
        /// <exception cref="ObjectDisposedException">The job is disposed.</exception>
        /// <exception cref="InvalidOperationException">No required artifact metadata is available.</exception>
        /// <exception cref="InvalidDataException">Artifact integrity, format or Replay validation fails.</exception>
        /// <exception cref="OperationCanceledException">The request or job was stopped.</exception>
        public async Task DownloadReplayBundleAsync(
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            ArtifactLocation replayBundle = GetArtifacts().ReplayBundle ??
                throw new InvalidOperationException(
                    "The latest result does not contain a replay bundle artifact.");
            if (replayBundle.Format != ArtifactLocationFormat.Json)
            {
                throw new InvalidDataException(
                    "The replay bundle artifact must declare the JSON format.");
            }

            using CancellationTokenSource operationCancellation =
                CreateOperationCancellationThreadSafe(cancellationToken);
            await transport.DownloadArtifactAsync(
                new ArtifactDownloadRequest(
                    replayBundle.Storage,
                    replayBundle.Bucket,
                    replayBundle.Path,
                    "json",
                    replayBundle.SizeBytes,
                    replayBundle.Sha256),
                destinationPath,
                operationCancellation.Token,
                (temporaryPath, _) => EmbodiedLabReplay.ReadManifest(
                    temporaryPath,
                    SubmissionId,
                    ScenarioId));
        }

        /// <summary>Downloads and validates the selected artifact before replacing the destination.</summary>
        /// <remarks>Uses the accepted result metadata and existing size/SHA-256 validation.
        /// Limits are 1 GiB for ONNX, 1 MiB for a manifest, and 64 MiB for compressed Replay;
        /// Replay parsing also retains its identity and resource limits. Failure or cancellation
        /// preserves an existing destination and removes the temporary download.</remarks>
        /// <param name="destinationPath">Caller-selected final file path.</param>
        /// <param name="cancellationToken">Cancels this download only.</param>
        /// <exception cref="ObjectDisposedException">The job is disposed.</exception>
        /// <exception cref="InvalidOperationException">No required artifact metadata is available.</exception>
        /// <exception cref="InvalidDataException">Artifact integrity, format or Replay validation fails.</exception>
        /// <exception cref="OperationCanceledException">The request or job was stopped.</exception>
        /// <param name="chunk">Selected manifest entry; do not mutate it while the request is pending.</param>
        public async Task DownloadReplayChunkAsync(
            ReplayBundleChunk chunk,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            ArtifactLocation replayManifest = GetArtifacts().ReplayBundle ??
                throw new InvalidOperationException(
                    "The latest result does not contain a replay bundle artifact.");
            ArtifactDownloadRequest chunkArtifact = CreateReplayChunkArtifact(
                replayManifest,
                chunk);
            using CancellationTokenSource operationCancellation =
                CreateOperationCancellationThreadSafe(cancellationToken);
            await transport.DownloadArtifactAsync(
                chunkArtifact,
                destinationPath,
                operationCancellation.Token,
                (temporaryPath, validationCancellation) => EmbodiedLabReplay.ReadChunk(
                    temporaryPath,
                    chunk,
                    SubmissionId,
                    ScenarioId,
                    validationCancellation));
        }

        /// <summary>Downloads and validates the selected artifact before replacing the destination.</summary>
        /// <remarks>Uses the accepted result metadata and existing size/SHA-256 validation.
        /// Limits are 1 GiB for ONNX, 1 MiB for a manifest, and 64 MiB for compressed Replay;
        /// Replay parsing also retains its identity and resource limits. Failure or cancellation
        /// preserves an existing destination and removes the temporary download.</remarks>
        /// <param name="destinationPath">Caller-selected final file path.</param>
        /// <param name="cancellationToken">Cancels this download only.</param>
        /// <exception cref="ObjectDisposedException">The job is disposed.</exception>
        /// <exception cref="InvalidOperationException">No required artifact metadata is available.</exception>
        /// <exception cref="InvalidDataException">Artifact integrity, format or Replay validation fails.</exception>
        /// <exception cref="OperationCanceledException">The request or job was stopped.</exception>
        public async Task DownloadModelAsync(
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            ResultArtifacts artifacts = GetArtifacts();
            OnnxModelArtifactLocation model = artifacts.OnnxModel ??
                throw new InvalidOperationException(
                    "The latest result does not contain an ONNX model artifact.");
            if (model.Format != OnnxModelArtifactLocationFormat.Onnx)
            {
                throw new InvalidDataException(
                    "The ONNX model artifact must declare the ONNX format.");
            }

            using CancellationTokenSource operationCancellation =
                CreateOperationCancellationThreadSafe(cancellationToken);
            await transport.DownloadArtifactAsync(
                new ArtifactDownloadRequest(
                    model.Storage,
                    model.Bucket,
                    model.Path,
                    "onnx",
                    model.SizeBytes,
                    model.Sha256),
                destinationPath,
                operationCancellation.Token);
        }

        /// <summary>Stops local monitoring and operations and releases transport resources.</summary>
        /// <remarks>Idempotent. Does not send cloud cancellation. Pending waits are cancelled,
        /// queued events are suppressed, and existing immutable snapshots remain readable.
        /// Does not synchronously wait for in-flight I/O cleanup.</remarks>
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                monitor?.Completion.TrySetCanceled();
            }

            lifetimeCancellation.Cancel();
            transport.Dispose();
            lifetimeCancellation.Dispose();
        }

        private static string RequireValue(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value cannot be empty.", parameterName);
            }

            return value;
        }

        private static bool IsTerminalStatus(ResultStatus status)
        {
            return status == ResultStatus.Completed ||
                status == ResultStatus.Failed ||
                status == ResultStatus.Cancelled;
        }

        private static ArtifactDownloadRequest CreateReplayChunkArtifact(
            ArtifactLocation replayManifest,
            ReplayBundleChunk chunk)
        {
            EmbodiedLabReplay.ValidateChunkMetadata(chunk);
            string manifestPath = RequireValue(
                replayManifest.Path,
                nameof(replayManifest.Path));
            string chunkPath = RequireValue(
                EmbodiedLabReplay.GetChunkPath(chunk),
                nameof(chunk));
            if (chunkPath.StartsWith("/", StringComparison.Ordinal) ||
                chunkPath.Contains("\\") ||
                chunkPath.Contains("?") ||
                chunkPath.Contains("#"))
            {
                throw new ArgumentException(
                    "Replay chunk path must be a relative GCS object path.",
                    nameof(chunk));
            }

            string[] segments = chunkPath.Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    throw new ArgumentException(
                        "Replay chunk path contains an invalid segment.",
                        nameof(chunk));
                }
            }

            int manifestFilenameStart = manifestPath.LastIndexOf('/');
            string replayDirectory = manifestFilenameStart < 0
                ? string.Empty
                : manifestPath.Substring(0, manifestFilenameStart + 1);
            return new ArtifactDownloadRequest(
                replayManifest.Storage,
                replayManifest.Bucket,
                replayDirectory + chunkPath,
                "jsonl.gz",
                chunk.SizeBytes,
                chunk.Sha256);
        }

        private ResultArtifacts GetArtifacts()
        {
            ResultSnapshot result = LatestResult ?? throw new InvalidOperationException(
                "No result has been received for this job.");
            return result.ToDocument().ResultBundle?.Artifacts ?? throw new InvalidOperationException(
                "The latest result does not contain artifact metadata.");
        }

        private CancellationTokenSource CreateOperationCancellationThreadSafe(
            CancellationToken cancellationToken)
        {
            lock (gate)
            {
                ThrowIfDisposed();
                return CreateOperationCancellation(cancellationToken);
            }
        }

        private CancellationTokenSource CreateOperationCancellation(
            CancellationToken cancellationToken)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeCancellation.Token);
        }

        private ResultSnapshot PublishResult(ResultDocument result, MonitorState? source = null)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (!string.Equals(result.SubmissionId, SubmissionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "EmbodiedLab returned a result for a different submission.");
            }

            if (result.ResultBundle != null &&
                !string.Equals(
                    result.ResultBundle.ScenarioId,
                    ScenarioId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "EmbodiedLab returned a result for a different Scenario.");
            }

            ResultSnapshot snapshot;
            lock (gate)
            {
                if (disposed || (source != null &&
                    (!ReferenceEquals(monitor, source) || source.Cancellation.IsCancellationRequested)))
                {
                    return latestResult ?? new ResultSnapshot(result);
                }

                if (latestResult != null && !ShouldAcceptResult(latestResult, result))
                {
                    return latestResult;
                }

                snapshot = new ResultSnapshot(result);
                latestResult = snapshot;
                if (IsTerminalStatus(snapshot.Status) && monitor != null)
                {
                    MonitorState active = monitor;
                    active.Completion.TrySetResult(snapshot);
                    active.Cancellation.Cancel();
                }
            }

            if (synchronizationContext != null &&
                !ReferenceEquals(SynchronizationContext.Current, synchronizationContext))
            {
                synchronizationContext.Post(_ => RaiseResultUpdated(snapshot), null);
                return snapshot;
            }

            RaiseResultUpdated(snapshot);
            return snapshot;
        }

        private static bool ShouldAcceptResult(
            ResultSnapshot current,
            ResultDocument candidate)
        {
            if (IsTerminalStatus(current.Status) && current.Status != candidate.Status)
            {
                return false;
            }

            return !TryParseUpdatedAt(current.UpdatedAt, out DateTimeOffset currentTime) ||
                !TryParseUpdatedAt(candidate.UpdatedAt, out DateTimeOffset candidateTime) ||
                candidateTime >= currentTime;
        }

        private static bool TryParseUpdatedAt(string value, out DateTimeOffset timestamp)
        {
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out timestamp);
        }

        private void RaiseResultUpdated(ResultSnapshot result)
        {
            Action<ResultSnapshot>? handler;
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                // A queued notification must not roll a consumer back after an inline update
                // on the captured context has already published a newer result.
                if (!ReferenceEquals(result, latestResult))
                {
                    return;
                }

                handler = ResultUpdated;
            }

            handler?.Invoke(result);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(EmbodiedLabJob));
            }
        }
    }
}
