#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity.Internal;
using UnityEngine;

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

        private ResultDocument? latestResult;
        private bool completionMonitorRunning;
        private bool disposed;

        internal EmbodiedLabJob(
            EmbodiedLabTransport transport,
            string submissionId,
            string? cancelToken,
            SynchronizationContext? synchronizationContext)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            SubmissionId = RequireValue(submissionId, nameof(submissionId));
            CancelToken = string.IsNullOrWhiteSpace(cancelToken) ? null : cancelToken;
            this.synchronizationContext = synchronizationContext;
        }

        public event Action<ResultDocument>? ResultUpdated;

        public string SubmissionId { get; }

        public string? CancelToken { get; }

        public bool CanCancel => CancelToken != null;

        public ResultDocument? LatestResult
        {
            get
            {
                lock (gate)
                {
                    return latestResult;
                }
            }
        }

        public bool IsTerminal
        {
            get
            {
                ResultDocument? result = LatestResult;
                return result != null && IsTerminalStatus(result.Status);
            }
        }

        public static async Awaitable<EmbodiedLabJob> SubmitAsync(
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
            catch (EmbodiedLabTrainingStartException)
            {
                throw;
            }
            catch
            {
                transport.Dispose();
                throw;
            }
        }

        internal static async Awaitable<EmbodiedLabJob> SubmitAsync(
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
                cancelToken,
                synchronizationContext);
            try
            {
                TrainingResponse training = await transport.StartTrainingAsync(
                    submissionId,
                    cancellationToken);
                if (!string.Equals(
                    training.SubmissionId,
                    submissionId,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "EmbodiedLab accepted training for a different submission.");
                }
            }
            catch (Exception exception)
            {
                throw new EmbodiedLabTrainingStartException(job, exception);
            }

            return job;
        }

        public static EmbodiedLabJob Restore(
            EmbodiedLabEndpoints endpoints,
            string submissionId,
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
                    cancelToken,
                    SynchronizationContext.Current);
            }
            catch
            {
                transport.Dispose();
                throw;
            }
        }

        public async Awaitable<ResultDocument> WaitForCompletionAsync(
            CancellationToken cancellationToken = default)
        {
            CancellationTokenSource operationCancellation;
            lock (gate)
            {
                ThrowIfDisposed();
                if (latestResult != null && IsTerminalStatus(latestResult.Status))
                {
                    return latestResult;
                }

                if (completionMonitorRunning)
                {
                    throw new InvalidOperationException(
                        "This job already has an active completion monitor.");
                }

                completionMonitorRunning = true;
                operationCancellation = CreateOperationCancellation(cancellationToken);
            }

            try
            {
                await transport.MonitorResultAsync(
                    SubmissionId,
                    result => PublishResult(result),
                    operationCancellation.Token);
                ResultDocument? result = LatestResult;
                if (result == null || !IsTerminalStatus(result.Status))
                {
                    throw new InvalidOperationException(
                        "EmbodiedLab monitoring ended without a terminal result.");
                }

                return result;
            }
            finally
            {
                operationCancellation.Dispose();
                lock (gate)
                {
                    completionMonitorRunning = false;
                }
            }
        }

        public async Awaitable<ResultDocument> RefreshAsync(
            CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource operationCancellation =
                CreateOperationCancellationThreadSafe(cancellationToken);
            ResultDocument result = await transport.GetResultAsync(
                SubmissionId,
                operationCancellation.Token);
            return PublishResult(result);
        }

        public async Awaitable<ResultDocument> CancelAsync(
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

        public async Awaitable DownloadReplayBundleAsync(
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            ArtifactLocation replayBundle = GetArtifacts().ReplayBundle ??
                throw new InvalidOperationException(
                    "The latest result does not contain a replay bundle artifact.");
            using CancellationTokenSource operationCancellation =
                CreateOperationCancellationThreadSafe(cancellationToken);
            await transport.DownloadArtifactAsync(
                replayBundle,
                destinationPath,
                operationCancellation.Token);
        }

        public async Awaitable DownloadReplayChunkAsync(
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
            ArtifactLocation chunkArtifact = CreateReplayChunkArtifact(
                replayManifest,
                chunk);
            using CancellationTokenSource operationCancellation =
                CreateOperationCancellationThreadSafe(cancellationToken);
            await transport.DownloadArtifactAsync(
                chunkArtifact,
                destinationPath,
                operationCancellation.Token);
        }

        public async Awaitable DownloadModelAsync(
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            ResultArtifacts artifacts = GetArtifacts();
            ArtifactLocation model = artifacts.OnnxModel ??
                throw new InvalidOperationException(
                    "The latest result does not contain an ONNX model artifact.");
            if (model.Format != ArtifactFormat.Onnx)
            {
                throw new InvalidDataException(
                    "The ONNX model artifact must declare the ONNX format.");
            }

            using CancellationTokenSource operationCancellation =
                CreateOperationCancellationThreadSafe(cancellationToken);
            await transport.DownloadArtifactAsync(
                model,
                destinationPath,
                operationCancellation.Token);
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
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

        private static ArtifactLocation CreateReplayChunkArtifact(
            ArtifactLocation replayManifest,
            ReplayBundleChunk chunk)
        {
            EmbodiedLabReplay.ValidateChunkMetadata(chunk);
            string manifestPath = RequireValue(
                replayManifest.Path,
                nameof(replayManifest.Path));
            string chunkPath = RequireValue(chunk.Path, nameof(chunk.Path));
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
            return new ArtifactLocation
            {
                Bucket = replayManifest.Bucket,
                Format = ArtifactFormat.JsonlGz,
                Path = replayDirectory + chunkPath,
                Storage = replayManifest.Storage,
            };
        }

        private ResultArtifacts GetArtifacts()
        {
            ResultDocument result = LatestResult ?? throw new InvalidOperationException(
                "No result has been received for this job.");
            return result.ResultBundle?.Artifacts ?? throw new InvalidOperationException(
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

        private ResultDocument PublishResult(ResultDocument result)
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

            lock (gate)
            {
                if (disposed)
                {
                    return latestResult ?? result;
                }

                if (latestResult != null && !ShouldAcceptResult(latestResult, result))
                {
                    return latestResult;
                }

                latestResult = result;
            }

            if (synchronizationContext != null &&
                !ReferenceEquals(SynchronizationContext.Current, synchronizationContext))
            {
                synchronizationContext.Post(_ => RaiseResultUpdated(result), null);
                return result;
            }

            RaiseResultUpdated(result);
            return result;
        }

        private static bool ShouldAcceptResult(
            ResultDocument current,
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

        private void RaiseResultUpdated(ResultDocument result)
        {
            Action<ResultDocument>? handler;
            lock (gate)
            {
                if (disposed)
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
