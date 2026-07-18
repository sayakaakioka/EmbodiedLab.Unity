#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    [DisallowMultipleComponent]
    public sealed class QuickstartController : MonoBehaviour
    {
        [SerializeField]
        private string apiBaseUrl = "https://api.example.com/";

        [SerializeField]
        private string resultWebSocketBaseUrl = "wss://results.example.com/";

        [SerializeField]
        private TextAsset? scenarioJson = null;

        private CancellationTokenSource? lifetimeCancellation;
        private CancellationTokenSource? monitorCancellation;
        private QuickstartHistoryStore? historyStore;
        private QuickstartWorldBuilder? worldBuilder;
        private QuickstartReplayPlayer? replayPlayer;
        private QuickstartHistoryRecord? selectedHistoryRecord;
        private EmbodiedLabJob? job;
        private Vector2 historyScrollPosition;
        private bool destroyed;
        private bool submissionRequestRunning;
        private bool monitorRunning;
        private bool restoreRunning;
        private bool cancelRequestRunning;
        private bool modelDownloadRunning;
        private bool replayDownloadRunning;
        private bool cancellationConfirmationArmed;
        private bool removalConfirmationArmed;
        private bool selectedHistoryRecordDirty;
        private int monitorGeneration;
        private DateTimeOffset nextHistorySaveRetryAtUtc = DateTimeOffset.MinValue;
        private string submissionIdText = "Not submitted";
        private string jobStatusText = "Not submitted";
        private string progressText = "-";
        private string activityText = "Ready.";
        private string modelPathText = "-";
        private string activeTargetText = "-";
        private string historyStorageText = "Ready.";

        private void Awake()
        {
            lifetimeCancellation = new CancellationTokenSource();
            historyStore = new QuickstartHistoryStore(
                Path.Combine(
                    Application.persistentDataPath,
                    "EmbodiedLabQuickstart",
                    "job-history.json"));
            worldBuilder = new QuickstartWorldBuilder();
            replayPlayer = new QuickstartReplayPlayer();

            TryLoadHistory();
            TryBuildBundledScenario();
        }

        private void OnGUI()
        {
            RetryDirtyHistory();
            GUILayout.BeginArea(new Rect(20, 20, 760, 900), GUI.skin.box);
            GUILayout.Label("EmbodiedLab Quickstart");
            GUILayout.Space(8);

            DrawTextField("API base URL", ref apiBaseUrl);
            DrawTextField("Result WebSocket URL", ref resultWebSocketBaseUrl);

            if (UsesExampleEndpoint(apiBaseUrl) ||
                UsesExampleEndpoint(resultWebSocketBaseUrl))
            {
                GUILayout.Label(
                    "Replace both example endpoints with your EmbodiedLab deployment.");
            }

            if (scenarioJson == null)
            {
                GUILayout.Label("The fixed scenario asset is not assigned.");
            }

            GUILayout.Space(8);
            DrawButton("Submit and Train", CanSubmit(), StartSubmission);
            DrawCloudCancellation();
            DrawButton("Download Model", CanDownloadModel(), StartModelDownload);
            DrawButton("Download Replay", CanDownloadReplay(), StartReplayDownload);
            DrawButton("Play Replay", CanPlayReplay(), StartReplayPlayback);
            DrawButton("Stop Replay", CanStopReplay(), StopReplayPlayback);

            GUILayout.Space(12);
            DrawValue("Submission ID", submissionIdText);
            DrawValue("Active cloud target", activeTargetText);
            DrawValue("Job status", jobStatusText);
            DrawValue("Progress", progressText);
            DrawValue("Activity", activityText);
            DrawValue("Downloaded model", modelPathText);
            DrawValue("Replay chunk", replayPlayer?.SelectedChunk ?? "-");
            DrawValue("Replay episode", replayPlayer?.CurrentEpisode ?? "-");
            DrawValue("Replay step", replayPlayer?.CurrentStep ?? "-");
            DrawValue("Replay status", replayPlayer?.Status ?? "Unavailable");
            DrawValue("History storage", historyStorageText);

            GUILayout.Space(12);
            DrawHistory();

            GUILayout.Space(8);
            GUILayout.Label(
                "Local cancellation stops this sample only. Use Cancel Cloud Job " +
                "to stop the remote training job.");
            GUILayout.EndArea();
        }

        private void Update()
        {
            replayPlayer?.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            destroyed = true;
            if (selectedHistoryRecordDirty && selectedHistoryRecord != null)
            {
                TryPersistDetachedHistoryRecord(selectedHistoryRecord);
            }

            lifetimeCancellation?.Cancel();
            StopCurrentJob();
            replayPlayer?.Clear();
            replayPlayer = null;
            worldBuilder?.Dispose();
            worldBuilder = null;
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = null;
        }

        private static void DrawTextField(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180));
            value = GUILayout.TextField(value);
            GUILayout.EndHorizontal();
        }

        private static void DrawButton(string label, bool enabled, Action action)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && enabled;
            if (GUILayout.Button(label))
            {
                action();
            }

            GUI.enabled = previousEnabled;
        }

        private static void DrawValue(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180));
            GUILayout.Label(value);
            GUILayout.EndHorizontal();
        }

        private void DrawHistory()
        {
            GUILayout.Label("Local history (newest first)");
            if (historyStore == null || historyStore.Records.Count == 0)
            {
                GUILayout.Label("No saved jobs.");
                return;
            }

            historyScrollPosition = GUILayout.BeginScrollView(
                historyScrollPosition,
                GUILayout.Height(170));
            foreach (QuickstartHistoryRecord record in historyStore.Records)
            {
                string marker = ReferenceEquals(record, selectedHistoryRecord) ? ">" : " ";
                string label =
                    $"{marker} {record.SubmittedAtUtc} | {record.Status} | {record.SubmissionId}";
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && CanSelectHistory();
                if (GUILayout.Button(label))
                {
                    StartHistorySelection(record.SubmissionId);
                }

                GUI.enabled = previousEnabled;
            }

            GUILayout.EndScrollView();

            if (selectedHistoryRecord == null)
            {
                return;
            }

            if (!removalConfirmationArmed)
            {
                DrawButton(
                    "Remove Local History Record",
                    CanChangeHistory(),
                    ArmRecordRemoval);
                return;
            }

            GUILayout.Label(GetRemovalWarning(selectedHistoryRecord));
            DrawButton(
                "Confirm: Remove Local Record Only",
                CanChangeHistory(),
                ConfirmRecordRemoval);
            DrawButton("Keep Record", true, DisarmRecordRemoval);
        }

        private bool CanSubmit()
        {
            return !destroyed &&
                !submissionRequestRunning &&
                !monitorRunning &&
                !restoreRunning &&
                !cancelRequestRunning &&
                !modelDownloadRunning &&
                !replayDownloadRunning &&
                !selectedHistoryRecordDirty &&
                !cancellationConfirmationArmed &&
                scenarioJson != null &&
                !UsesExampleEndpoint(apiBaseUrl) &&
                !UsesExampleEndpoint(resultWebSocketBaseUrl);
        }

        private bool CanCancel()
        {
            return !destroyed &&
                job != null &&
                job.CanCancel &&
                !job.IsTerminal &&
                !submissionRequestRunning &&
                !restoreRunning &&
                !cancelRequestRunning &&
                !modelDownloadRunning &&
                !replayDownloadRunning;
        }

        private bool CanDownloadModel()
        {
            return !destroyed &&
                job?.LatestResult?.Status == ResultStatus.Completed &&
                selectedHistoryRecord != null &&
                !submissionRequestRunning &&
                !restoreRunning &&
                !cancelRequestRunning &&
                !modelDownloadRunning &&
                !replayDownloadRunning;
        }

        private bool CanDownloadReplay()
        {
            return !destroyed &&
                job?.LatestResult?.Status == ResultStatus.Completed &&
                selectedHistoryRecord != null &&
                !submissionRequestRunning &&
                !restoreRunning &&
                !cancelRequestRunning &&
                !modelDownloadRunning &&
                !replayDownloadRunning;
        }

        private bool CanPlayReplay()
        {
            return !destroyed &&
                replayPlayer?.IsLoaded == true &&
                !replayPlayer.IsPlaying &&
                !submissionRequestRunning &&
                !restoreRunning &&
                !cancelRequestRunning &&
                !modelDownloadRunning &&
                !replayDownloadRunning;
        }

        private bool CanStopReplay()
        {
            return !destroyed && replayPlayer?.IsLoaded == true;
        }

        private static bool UsesExampleEndpoint(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
                (string.Equals(
                    uri.Host,
                    "example.com",
                    StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(
                    ".example.com",
                    StringComparison.OrdinalIgnoreCase));
        }

        private void StartSubmission()
        {
            if (!CanSubmit())
            {
                return;
            }

            submissionRequestRunning = true;
            ClearReplayForModeChange();
            cancellationConfirmationArmed = false;
            removalConfirmationArmed = false;
            _ = SubmitAndMonitorAsync();
        }

        private void ArmCloudCancellation()
        {
            removalConfirmationArmed = false;
            cancellationConfirmationArmed = true;
            activityText = "Confirm the active cloud cancellation target below.";
        }

        private void DisarmCloudCancellation()
        {
            cancellationConfirmationArmed = false;
            activityText = "Cloud job kept running.";
        }

        private void StartCloudCancellation()
        {
            if (!CanCancel())
            {
                return;
            }

            cancellationConfirmationArmed = false;
            _ = CancelCloudJobAsync();
        }

        private void StartModelDownload()
        {
            if (!CanDownloadModel())
            {
                return;
            }

            cancellationConfirmationArmed = false;
            removalConfirmationArmed = false;
            replayPlayer?.Stop();
            _ = DownloadModelAsync();
        }

        private void StartReplayDownload()
        {
            if (!CanDownloadReplay())
            {
                return;
            }

            cancellationConfirmationArmed = false;
            removalConfirmationArmed = false;
            replayPlayer?.Stop();
            _ = DownloadReplayAsync();
        }

        private void StartReplayPlayback()
        {
            if (!CanPlayReplay())
            {
                return;
            }

            replayPlayer!.Play();
            activityText = "Replay playback started.";
        }

        private void StopReplayPlayback()
        {
            if (!CanStopReplay())
            {
                return;
            }

            replayPlayer!.Stop();
            activityText = "Replay stopped at the first loaded step.";
        }

        private void StartHistorySelection(string submissionId)
        {
            if (!CanSelectHistory())
            {
                return;
            }

            restoreRunning = true;
            ClearReplayForModeChange();
            cancellationConfirmationArmed = false;
            removalConfirmationArmed = false;
            _ = RestoreHistoryRecordAsync(submissionId);
        }

        private async Awaitable SubmitAndMonitorAsync()
        {
            if (scenarioJson == null ||
                lifetimeCancellation == null ||
                historyStore == null ||
                worldBuilder == null)
            {
                submissionRequestRunning = false;
                return;
            }

            EmbodiedLabJob? submittedJob = null;
            int generation = -1;
            try
            {
                string exactScenarioJson = scenarioJson.text;
                ScenarioBundle scenario = ScenarioBundleJson.Deserialize(exactScenarioJson);
                var endpoints = new EmbodiedLabEndpoints(
                    apiBaseUrl.Trim(),
                    resultWebSocketBaseUrl.Trim());

                activityText = "Submitting scenario and starting training...";
                submittedJob = await EmbodiedLabJob.SubmitAsync(
                    endpoints,
                    scenario,
                    lifetimeCancellation.Token);
                var record = new QuickstartHistoryRecord
                {
                    SubmissionId = submittedJob.SubmissionId,
                    SubmittedAtUtc = DateTimeOffset.UtcNow.ToString(
                        "O",
                        CultureInfo.InvariantCulture),
                    ApiBaseUrl = endpoints.ApiBaseUri.AbsoluteUri,
                    ResultWebSocketBaseUrl = endpoints.ResultWebSocketBaseUri.AbsoluteUri,
                    ScenarioJson = exactScenarioJson,
                    Status = ResultStatus.Queued,
                    CancelToken = submittedJob.CancelToken,
                };
                if (destroyed)
                {
                    TryPersistDetachedHistoryRecord(record);
                    return;
                }

                selectedHistoryRecord = record;
                selectedHistoryRecordDirty = true;
                AttachJob(submittedJob);
                submittedJob = null;
                ApplyHistoryRecord(record);
                (CancellationToken token, int operationGeneration) = StartMonitor();
                generation = operationGeneration;
                submissionRequestRunning = false;

                activityText = "Waiting for WebSocket result updates...";
                TryBuildWorld(scenario);
                TryPersistHistoryRecord(record);

                ResultDocument result = await job!.WaitForCompletionAsync(token);
                ApplyResult(result);
            }
            catch (OperationCanceledException)
            {
                if (!destroyed && generation == monitorGeneration)
                {
                    activityText =
                        "Local monitoring stopped. The cloud job may still be running.";
                }
            }
            catch (Exception exception)
            {
                ReportError("Training request failed", exception);
            }
            finally
            {
                submissionRequestRunning = false;
                submittedJob?.Dispose();
                FinishMonitor(generation);
            }
        }

        private async Awaitable RestoreHistoryRecordAsync(string submissionId)
        {
            if (historyStore == null ||
                worldBuilder == null ||
                lifetimeCancellation == null)
            {
                restoreRunning = false;
                return;
            }

            QuickstartHistoryRecord? record = historyStore.Find(submissionId);
            if (record == null)
            {
                activityText = "The selected history record no longer exists.";
                restoreRunning = false;
                return;
            }

            EmbodiedLabJob? restoredJob = null;
            int generation = -1;
            bool restorePhaseCompleted = false;
            try
            {
                ScenarioBundle scenario = ScenarioBundleJson.Deserialize(record.ScenarioJson);
                var endpoints = new EmbodiedLabEndpoints(
                    record.ApiBaseUrl,
                    record.ResultWebSocketBaseUrl);
                restoredJob = EmbodiedLabJob.Restore(
                    endpoints,
                    record.SubmissionId,
                    record.CancelToken);

                selectedHistoryRecord = record;
                selectedHistoryRecordDirty = false;
                nextHistorySaveRetryAtUtc = DateTimeOffset.MinValue;
                AttachJob(restoredJob);
                restoredJob = null;
                ApplyHistoryRecord(record);
                activityText = "Refreshing the selected cloud job...";
                TryBuildWorld(scenario);

                ResultDocument refreshed = await job!.RefreshAsync(
                    lifetimeCancellation.Token);
                ApplyResult(refreshed);
                if (refreshed.Status == ResultStatus.Completed)
                {
                    TryLoadPersistedReplay(record);
                }

                if (job.IsTerminal)
                {
                    return;
                }

                activityText = "Resuming WebSocket result monitoring...";
                (CancellationToken token, int operationGeneration) = StartMonitor();
                generation = operationGeneration;
                restoreRunning = false;
                restorePhaseCompleted = true;
                ResultDocument completed = await job.WaitForCompletionAsync(token);
                ApplyResult(completed);
            }
            catch (OperationCanceledException)
            {
                if (!destroyed && generation == monitorGeneration)
                {
                    activityText =
                        "Local monitoring stopped. The cloud job may still be running.";
                }
            }
            catch (Exception exception)
            {
                ReportError("History restore failed", exception);
            }
            finally
            {
                restoredJob?.Dispose();
                if (!restorePhaseCompleted)
                {
                    restoreRunning = false;
                }

                FinishMonitor(generation);
            }
        }

        private async Awaitable CancelCloudJobAsync()
        {
            EmbodiedLabJob? activeJob = job;
            if (activeJob == null || lifetimeCancellation == null)
            {
                return;
            }

            cancelRequestRunning = true;
            try
            {
                activityText = "Requesting cloud cancellation...";
                ResultDocument result = await activeJob.CancelAsync(
                    lifetimeCancellation.Token);
                if (ReferenceEquals(job, activeJob))
                {
                    ApplyResult(result);
                }
            }
            catch (OperationCanceledException)
            {
                if (!destroyed)
                {
                    activityText = "The local cancellation request stopped.";
                }
            }
            catch (Exception exception)
            {
                ReportError("Cloud cancellation failed", exception);
            }
            finally
            {
                cancelRequestRunning = false;
            }
        }

        private async Awaitable DownloadModelAsync()
        {
            EmbodiedLabJob? activeJob = job;
            QuickstartHistoryRecord? record = selectedHistoryRecord;
            if (activeJob == null ||
                record == null ||
                lifetimeCancellation == null ||
                historyStore == null)
            {
                return;
            }

            modelDownloadRunning = true;
            try
            {
                string outputDirectory = QuickstartLocalPaths.GetSubmissionDirectory(
                    Application.persistentDataPath,
                    activeJob.SubmissionId);
                Directory.CreateDirectory(outputDirectory);
                string destinationPath = Path.Combine(outputDirectory, "policy.onnx");

                activityText = "Downloading trained model...";
                await activeJob.DownloadModelAsync(
                    destinationPath,
                    lifetimeCancellation.Token);
                record.LocalOnnxPath = destinationPath;
                selectedHistoryRecordDirty = true;
                TryPersistHistoryRecord(record);
                modelPathText = destinationPath;
                activityText = "Model downloaded.";
            }
            catch (OperationCanceledException)
            {
                if (!destroyed)
                {
                    activityText = "The local model download stopped.";
                }
            }
            catch (Exception exception)
            {
                ReportError("Model download failed", exception);
            }
            finally
            {
                modelDownloadRunning = false;
            }
        }

        private async Awaitable DownloadReplayAsync()
        {
            EmbodiedLabJob? activeJob = job;
            QuickstartHistoryRecord? record = selectedHistoryRecord;
            if (activeJob == null ||
                record == null ||
                lifetimeCancellation == null ||
                historyStore == null ||
                worldBuilder == null ||
                replayPlayer == null)
            {
                return;
            }

            QuickstartReplayPlayer activeReplayPlayer = replayPlayer;
            replayDownloadRunning = true;
            try
            {
                activityText = "Refreshing the completed job before replay download...";
                ResultDocument refreshed = await activeJob.RefreshAsync(
                    lifetimeCancellation.Token);
                if (!IsSelectedJob(activeJob, record))
                {
                    return;
                }

                ApplyResult(refreshed);
                if (refreshed.Status != ResultStatus.Completed)
                {
                    throw new InvalidOperationException(
                        "Replay download requires a completed cloud job.");
                }

                string manifestPath = QuickstartLocalPaths.GetReplayManifestPath(
                    Application.persistentDataPath,
                    activeJob.SubmissionId);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(manifestPath) ?? throw new InvalidOperationException(
                        "Replay manifest directory is unavailable."));

                activityText = "Downloading replay manifest...";
                await activeJob.DownloadReplayBundleAsync(
                    manifestPath,
                    lifetimeCancellation.Token);
                if (!IsSelectedJob(activeJob, record))
                {
                    return;
                }

                ReplayBundleManifest manifest = EmbodiedLabReplay.ReadManifest(manifestPath);
                ValidateReplayManifest(record, manifest);
                ReplayBundleChunk selectedChunk =
                    QuickstartReplayTimeline.SelectLatestDeterministicEvaluationChunk(
                        manifest);
                string chunkPath = QuickstartLocalPaths.GetReplayChunkPath(
                    Application.persistentDataPath,
                    activeJob.SubmissionId,
                    selectedChunk.Path);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(chunkPath) ?? throw new InvalidOperationException(
                        "Replay chunk directory is unavailable."));

                activityText = $"Downloading replay chunk {selectedChunk.Path}...";
                await activeJob.DownloadReplayChunkAsync(
                    selectedChunk,
                    chunkPath,
                    lifetimeCancellation.Token);
                if (!IsSelectedJob(activeJob, record))
                {
                    return;
                }

                IReadOnlyList<ReplayLogStep> steps = EmbodiedLabReplay.ReadSteps(chunkPath);
                QuickstartReplayTimeline.ValidateSelectedChunkSteps(
                    activeJob.SubmissionId,
                    manifest.ScenarioId,
                    selectedChunk,
                    steps);
                LoadReplay(selectedChunk, steps);

                record.LocalReplayManifestPath = manifestPath;
                record.LocalReplayChunkPath = chunkPath;
                selectedHistoryRecordDirty = true;
                TryPersistHistoryRecord(record);
                activityText = "Replay downloaded and ready.";
            }
            catch (OperationCanceledException)
            {
                if (!destroyed)
                {
                    activityText = "The local replay download stopped.";
                }
            }
            catch (Exception exception)
            {
                activeReplayPlayer.Clear();
                ReportError("Replay download failed", exception);
            }
            finally
            {
                replayDownloadRunning = false;
            }
        }

        private void TryLoadPersistedReplay(QuickstartHistoryRecord record)
        {
            if (replayPlayer == null ||
                string.IsNullOrWhiteSpace(record.LocalReplayManifestPath) ||
                string.IsNullOrWhiteSpace(record.LocalReplayChunkPath))
            {
                return;
            }

            try
            {
                string expectedManifestPath = QuickstartLocalPaths.GetReplayManifestPath(
                    Application.persistentDataPath,
                    record.SubmissionId);
                if (!PathsEqual(expectedManifestPath, record.LocalReplayManifestPath))
                {
                    throw new InvalidDataException(
                        "Saved replay manifest path is outside Quickstart storage.");
                }

                ReplayBundleManifest manifest = EmbodiedLabReplay.ReadManifest(
                    expectedManifestPath);
                ValidateReplayManifest(record, manifest);
                ReplayBundleChunk selectedChunk =
                    QuickstartReplayTimeline.SelectLatestDeterministicEvaluationChunk(
                        manifest);
                string expectedChunkPath = QuickstartLocalPaths.GetReplayChunkPath(
                    Application.persistentDataPath,
                    record.SubmissionId,
                    selectedChunk.Path);
                if (!PathsEqual(expectedChunkPath, record.LocalReplayChunkPath))
                {
                    throw new InvalidDataException(
                        "Saved replay chunk path does not match the selected manifest chunk.");
                }

                IReadOnlyList<ReplayLogStep> steps = EmbodiedLabReplay.ReadSteps(
                    expectedChunkPath);
                QuickstartReplayTimeline.ValidateSelectedChunkSteps(
                    record.SubmissionId,
                    manifest.ScenarioId,
                    selectedChunk,
                    steps);
                LoadReplay(selectedChunk, steps);
                activityText = "Saved replay loaded and ready.";
            }
            catch (Exception exception)
            {
                replayPlayer.Clear();
                activityText = $"Saved replay unavailable: {exception.Message}";
                Debug.LogException(exception, this);
            }
        }

        private void LoadReplay(
            ReplayBundleChunk selectedChunk,
            IReadOnlyList<ReplayLogStep> steps)
        {
            Transform robot = worldBuilder?.RobotTransform ??
                throw new InvalidOperationException(
                    "The shared Quickstart robot is unavailable.");
            QuickstartReplayPlayer player = replayPlayer ??
                throw new InvalidOperationException("Replay player is unavailable.");
            player.Load(robot, steps, selectedChunk.Path);
        }

        private static void ValidateReplayManifest(
            QuickstartHistoryRecord record,
            ReplayBundleManifest manifest)
        {
            if (!string.Equals(
                manifest.JobId,
                record.SubmissionId,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Replay manifest belongs to a different submission.");
            }

            ScenarioBundle scenario = ScenarioBundleJson.Deserialize(record.ScenarioJson);
            if (!string.Equals(
                manifest.ScenarioId,
                scenario.ScenarioId,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Replay manifest belongs to a different scenario.");
            }
        }

        private bool IsSelectedJob(
            EmbodiedLabJob activeJob,
            QuickstartHistoryRecord record)
        {
            return !destroyed &&
                ReferenceEquals(job, activeJob) &&
                ReferenceEquals(selectedHistoryRecord, record);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private void HandleResultUpdated(ResultDocument result)
        {
            ApplyResult(result);
        }

        private void ApplyResult(ResultDocument result)
        {
            QuickstartHistoryRecord? record = selectedHistoryRecord;
            if (record == null ||
                !string.Equals(record.SubmissionId, result.SubmissionId, StringComparison.Ordinal))
            {
                return;
            }

            bool changed = record.ApplyResult(result);
            selectedHistoryRecordDirty |= changed;
            if (changed || selectedHistoryRecordDirty)
            {
                TryPersistHistoryRecord(record);
            }

            submissionIdText = result.SubmissionId;
            jobStatusText = result.Status.ToString();
            progressText = FormatProgress(result.Progress);
            activityText = result.Status switch
            {
                ResultStatus.Completed => "Training completed.",
                ResultStatus.Failed => "Training failed.",
                ResultStatus.Cancelled => "Cloud job cancelled.",
                _ => result.Progress?.Message ?? "Waiting for the next result update.",
            };

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                activityText = $"{activityText} {result.Error}";
            }

            if (record.IsTerminal)
            {
                cancellationConfirmationArmed = false;
            }
        }

        private static string FormatProgress(Progress? progress)
        {
            return progress == null
                ? "-"
                : $"{progress.Phase}: {progress.CurrentStep}/{progress.TotalSteps} " +
                    progress.Message;
        }

        private void ApplyHistoryRecord(QuickstartHistoryRecord record)
        {
            submissionIdText = record.SubmissionId;
            jobStatusText = record.Status.ToString();
            progressText = FormatProgress(record.Progress);
            modelPathText = record.LocalOnnxPath ?? "-";
            activeTargetText = FormatCloudTarget(record.ApiBaseUrl, record.SubmissionId);
        }

        private void TryLoadHistory()
        {
            try
            {
                historyStore?.Load();
            }
            catch (Exception exception)
            {
                historyStorageText = "Unavailable. Existing history was not loaded.";
                ReportError("Local history load failed", exception);
            }
        }

        private void TryBuildBundledScenario()
        {
            if (scenarioJson == null || worldBuilder == null)
            {
                return;
            }

            try
            {
                ClearReplayForModeChange();
                ScenarioBundle scenario = ScenarioBundleJson.Deserialize(scenarioJson.text);
                worldBuilder.Build(scenario);
            }
            catch (Exception exception)
            {
                worldBuilder.Dispose();
                ReportError("Bundled scenario display failed", exception);
            }
        }

        private void TryBuildWorld(ScenarioBundle scenario)
        {
            try
            {
                ClearReplayForModeChange();
                worldBuilder?.Build(scenario);
            }
            catch (Exception exception)
            {
                worldBuilder?.Dispose();
                ReportError("Scenario display failed; cloud monitoring remains active", exception);
            }
        }

        private void TryPersistHistoryRecord(
            QuickstartHistoryRecord record,
            bool reportFailure = true)
        {
            if (historyStore == null)
            {
                historyStorageText = "Unavailable. History storage is not initialized.";
                return;
            }

            try
            {
                historyStore.Upsert(record);
                if (ReferenceEquals(record, selectedHistoryRecord))
                {
                    selectedHistoryRecordDirty = false;
                    nextHistorySaveRetryAtUtc = DateTimeOffset.MinValue;
                }

                historyStorageText = "Saved locally.";
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(record, selectedHistoryRecord))
                {
                    selectedHistoryRecordDirty = true;
                    nextHistorySaveRetryAtUtc = DateTimeOffset.UtcNow.AddSeconds(2);
                }

                historyStorageText =
                    "Save failed. Keep this scene open to retain the active job handle.";
                if (reportFailure)
                {
                    ReportError(
                        "Local history save failed; cloud monitoring remains active",
                        exception);
                }
            }
        }

        private void RetryDirtyHistory()
        {
            if (!selectedHistoryRecordDirty ||
                selectedHistoryRecord == null ||
                DateTimeOffset.UtcNow < nextHistorySaveRetryAtUtc)
            {
                return;
            }

            TryPersistHistoryRecord(selectedHistoryRecord, reportFailure: false);
        }

        private void TryPersistDetachedHistoryRecord(QuickstartHistoryRecord record)
        {
            try
            {
                historyStore?.Upsert(record);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void AttachJob(EmbodiedLabJob nextJob)
        {
            StopCurrentJob();
            job = nextJob;
            job.ResultUpdated += HandleResultUpdated;
            cancellationConfirmationArmed = false;
        }

        private (CancellationToken Token, int Generation) StartMonitor()
        {
            if (lifetimeCancellation == null)
            {
                throw new InvalidOperationException("Quickstart lifetime has ended.");
            }

            monitorCancellation?.Cancel();
            monitorCancellation?.Dispose();
            monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeCancellation.Token);
            monitorRunning = true;
            int generation = ++monitorGeneration;
            return (monitorCancellation.Token, generation);
        }

        private void FinishMonitor(int generation)
        {
            if (generation >= 0 && generation == monitorGeneration)
            {
                monitorRunning = false;
            }
        }

        private void StopCurrentJob()
        {
            monitorGeneration++;
            monitorRunning = false;
            monitorCancellation?.Cancel();
            monitorCancellation?.Dispose();
            monitorCancellation = null;
            if (job == null)
            {
                return;
            }

            job.ResultUpdated -= HandleResultUpdated;
            job.Dispose();
            job = null;
        }

        private void ClearReplayForModeChange()
        {
            replayPlayer?.Clear();
        }

        private void DrawCloudCancellation()
        {
            if (!cancellationConfirmationArmed)
            {
                DrawButton("Cancel Cloud Job", CanCancel(), ArmCloudCancellation);
                return;
            }

            GUILayout.Label($"Cloud cancellation target: {activeTargetText}");
            DrawButton(
                "Confirm: Cancel Active Cloud Job",
                CanCancel(),
                StartCloudCancellation);
            DrawButton("Keep Cloud Job Running", true, DisarmCloudCancellation);
        }

        private bool CanSelectHistory()
        {
            return !submissionRequestRunning &&
                !restoreRunning &&
                !cancelRequestRunning &&
                !modelDownloadRunning &&
                !replayDownloadRunning &&
                !selectedHistoryRecordDirty;
        }

        private bool CanChangeHistory()
        {
            return CanSelectHistory() && !cancellationConfirmationArmed;
        }

        private static string FormatCloudTarget(string apiUrl, string submissionId)
        {
            return Uri.TryCreate(apiUrl, UriKind.Absolute, out Uri? uri)
                ? $"{uri.Scheme}://{uri.Authority} | {submissionId}"
                : $"{apiUrl} | {submissionId}";
        }

        private static string GetRemovalWarning(QuickstartHistoryRecord record)
        {
            string baseWarning =
                "This removes only the local history record. It does not cancel the cloud job " +
                "or delete downloaded files.";
            if (record.IsTerminal || string.IsNullOrWhiteSpace(record.CancelToken))
            {
                return baseWarning;
            }

            return baseWarning +
                " This active job will keep running, and its cancellation capability will be " +
                $"permanently lost for {FormatCloudTarget(record.ApiBaseUrl, record.SubmissionId)}.";
        }

        private void ArmRecordRemoval()
        {
            cancellationConfirmationArmed = false;
            removalConfirmationArmed = true;
            activityText = "Confirm local history removal below.";
        }

        private void DisarmRecordRemoval()
        {
            removalConfirmationArmed = false;
            activityText = "Local history record kept.";
        }

        private void ConfirmRecordRemoval()
        {
            QuickstartHistoryRecord? record = selectedHistoryRecord;
            if (record == null || historyStore == null)
            {
                return;
            }

            ClearReplayForModeChange();

            try
            {
                historyStore.Remove(record.SubmissionId);
            }
            catch (Exception exception)
            {
                historyStorageText = "Removal was not saved; the record was kept.";
                removalConfirmationArmed = false;
                ReportError("Local history removal failed", exception);
                return;
            }

            if (job != null &&
                string.Equals(job.SubmissionId, record.SubmissionId, StringComparison.Ordinal))
            {
                // Disposing the local handle never cancels or deletes the cloud job.
                StopCurrentJob();
            }

            selectedHistoryRecord = null;
            selectedHistoryRecordDirty = false;
            nextHistorySaveRetryAtUtc = DateTimeOffset.MinValue;
            cancellationConfirmationArmed = false;
            removalConfirmationArmed = false;
            submissionIdText = "Not selected";
            jobStatusText = "Not selected";
            progressText = "-";
            modelPathText = "-";
            activeTargetText = "-";
            historyStorageText = "Saved locally.";
            activityText =
                "Local history removed. Cloud jobs and downloaded files were not changed.";
        }

        private void ReportError(string operation, Exception exception)
        {
            if (destroyed)
            {
                return;
            }

            activityText = $"{operation}: {exception.Message}";
            Debug.LogException(exception, this);
        }
    }
}
