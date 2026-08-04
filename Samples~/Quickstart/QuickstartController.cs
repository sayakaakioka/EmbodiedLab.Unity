#nullable enable

using System;
using System.Threading;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    [DisallowMultipleComponent]
    public sealed partial class QuickstartController : MonoBehaviour
    {
        [SerializeField]
        private string apiBaseUrl = "https://api.example.com/";

        [SerializeField]
        private string resultWebSocketBaseUrl = "wss://results.example.com/";

        [SerializeField]
        private TextAsset? scenarioJson = null;

        private CancellationTokenSource? lifetimeCancellation;
        private QuickstartCloudJob? cloudJob;
        private QuickstartArtifacts? artifacts;
        private QuickstartWorldBuilder? worldBuilder;
        private QuickstartReplayPlayer? replayPlayer;
        private QuickstartInferenceRunner? inferenceRunner;
        private ScenarioBundle? scenario;
        private bool destroyed;
        private bool cloudCancellationArmed;
        private string submissionIdText = "Not submitted";
        private string jobStatusText = "Not submitted";
        private string progressText = "-";
        private string activityText = "Ready.";

        private void Awake()
        {
            lifetimeCancellation = new CancellationTokenSource();
            cloudJob = new QuickstartCloudJob();
            artifacts = new QuickstartArtifacts();
            worldBuilder = new QuickstartWorldBuilder();
            replayPlayer = new QuickstartReplayPlayer();
            Subscribe();
            TryBuildScenario();
        }

        private void Update()
        {
            UpdateOverviewCameraViewport();
            replayPlayer?.Tick(Time.deltaTime);
            inferenceRunner?.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            destroyed = true;
            lifetimeCancellation?.Cancel();
            Unsubscribe();
            cloudJob?.Dispose();
            cloudJob = null;
            artifacts = null;
            inferenceRunner?.DisposeWithoutReset();
            inferenceRunner = null;
            replayPlayer?.Clear();
            replayPlayer = null;
            worldBuilder?.Dispose();
            worldBuilder = null;
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = null;
        }

        private bool CanSubmit()
        {
            return !destroyed &&
                cloudJob != null &&
                !cloudJob.IsBusy &&
                !cloudJob.IsMonitoring &&
                artifacts?.IsBusy == false &&
                scenario != null &&
                !UsesExampleEndpoint(apiBaseUrl) &&
                !UsesExampleEndpoint(resultWebSocketBaseUrl);
        }

        private bool CanCancel()
        {
            return !destroyed &&
                cloudJob?.CanCancel == true &&
                artifacts?.IsBusy == false;
        }

        private bool CanDownloadArtifacts()
        {
            return !destroyed &&
                cloudJob?.IsBusy == false &&
                cloudJob.IsMonitoring == false &&
                cloudJob.Job?.LatestResult?.Status == ResultStatus.Completed &&
                artifacts?.IsBusy == false;
        }

        private bool CanPlayReplay()
        {
            return !destroyed &&
                cloudJob?.IsBusy == false &&
                artifacts?.IsBusy == false &&
                replayPlayer?.IsLoaded == true &&
                !replayPlayer.IsPlaying &&
                inferenceRunner?.IsRunning != true;
        }

        private bool CanRunInference()
        {
            return !destroyed &&
                cloudJob?.IsBusy == false &&
                artifacts?.IsBusy == false &&
                replayPlayer?.IsPlaying != true &&
                inferenceRunner?.IsRunning != true &&
                artifacts.HasRunnableModel(
                    cloudJob.Job,
                    Application.persistentDataPath);
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
            if (!CanSubmit() ||
                cloudJob == null ||
                artifacts == null ||
                scenario == null ||
                lifetimeCancellation == null)
            {
                return;
            }

            cloudCancellationArmed = false;
            ResetTutorialResult();
            var endpoints = new EmbodiedLabEndpoints(
                apiBaseUrl.Trim(),
                resultWebSocketBaseUrl.Trim());
            _ = cloudJob.SubmitAndMonitorAsync(
                endpoints,
                scenario,
                lifetimeCancellation.Token);
        }

        private void StartCloudCancellation()
        {
            if (!CanCancel() || cloudJob == null || lifetimeCancellation == null)
            {
                return;
            }

            cloudCancellationArmed = false;
            _ = cloudJob.CancelAsync(lifetimeCancellation.Token);
        }

        private void StartModelDownload()
        {
            EmbodiedLabJob? job = cloudJob?.Job;
            if (!CanDownloadArtifacts() ||
                job == null ||
                artifacts == null ||
                lifetimeCancellation == null)
            {
                return;
            }

            StopResultExecution();
            _ = artifacts.DownloadModelAsync(
                job,
                Application.persistentDataPath,
                lifetimeCancellation.Token);
        }

        private void StartReplayDownload()
        {
            EmbodiedLabJob? job = cloudJob?.Job;
            Transform? robot = worldBuilder?.RobotTransform;
            if (!CanDownloadArtifacts() ||
                job == null ||
                robot == null ||
                artifacts == null ||
                replayPlayer == null ||
                scenario == null ||
                lifetimeCancellation == null)
            {
                return;
            }

            StopResultExecution();
            replayPlayer.Clear();
            _ = artifacts.DownloadReplayAsync(
                job,
                scenario.ScenarioId,
                Application.persistentDataPath,
                robot,
                replayPlayer,
                lifetimeCancellation.Token);
        }

        private void StartReplayPlayback()
        {
            if (!CanPlayReplay())
            {
                return;
            }

            inferenceRunner?.Stop();
            replayPlayer!.Play();
            activityText = "Replay playback started.";
        }

        private void StopReplayPlayback()
        {
            if (replayPlayer?.IsPlaying != true)
            {
                return;
            }

            replayPlayer.Stop();
            activityText = "Replay stopped at the first loaded step.";
        }

        private void StartInference()
        {
            if (!CanRunInference() || artifacts == null)
            {
                return;
            }

            replayPlayer?.Stop();
            inferenceRunner!.Start(artifacts.ModelPath);
            activityText = inferenceRunner.Status;
        }

        private void StopInference()
        {
            if (inferenceRunner?.IsRunning != true)
            {
                return;
            }

            inferenceRunner.Stop();
            activityText = inferenceRunner.Status;
        }

        private void ArmCloudCancellation()
        {
            cloudCancellationArmed = true;
            activityText = "Confirm cloud cancellation below.";
        }

        private void DisarmCloudCancellation()
        {
            cloudCancellationArmed = false;
            activityText = "Cloud job kept running.";
        }

        private void HandleSubmissionStarted(string submissionId)
        {
            submissionIdText = submissionId;
            jobStatusText = ResultStatus.Queued.ToString();
            progressText = "Waiting for the trainer to start.";
        }

        private void ApplyResult(ResultDocument result)
        {
            EmbodiedLabJob? currentJob = cloudJob?.Job;
            if (currentJob == null ||
                !string.Equals(
                    currentJob.SubmissionId,
                    result.SubmissionId,
                    StringComparison.Ordinal))
            {
                return;
            }

            submissionIdText = result.SubmissionId;
            jobStatusText = result.Status.ToString();
            progressText = QuickstartProgressText.Format(result.Status, result.Progress);
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

            if (currentJob.IsTerminal)
            {
                cloudCancellationArmed = false;
            }
        }

        private void TryBuildScenario()
        {
            if (scenarioJson == null || worldBuilder == null)
            {
                activityText = "The tutorial scenario asset is not assigned.";
                return;
            }

            try
            {
                scenario = ScenarioBundleJson.Deserialize(scenarioJson.text);
                worldBuilder.Build(scenario);
                UpdateOverviewCameraViewport();
                CreateInferenceRunner();
                activityText = "Fixed scenario loaded.";
            }
            catch (Exception exception)
            {
                scenario = null;
                worldBuilder.Dispose();
                ReportError("Scenario display failed", exception);
            }
        }

        private void CreateInferenceRunner()
        {
            QuickstartWorldBuilder activeWorld = worldBuilder ??
                throw new InvalidOperationException("Tutorial world is unavailable.");
            inferenceRunner?.Dispose();
            inferenceRunner = new QuickstartInferenceRunner(activeWorld);
        }

        private void ResetTutorialResult()
        {
            StopResultExecution();
            replayPlayer?.Clear();
            CreateInferenceRunner();
            artifacts?.Reset();
        }

        private void StopResultExecution()
        {
            replayPlayer?.Stop();
            inferenceRunner?.Stop();
        }

        private void Subscribe()
        {
            if (cloudJob == null || artifacts == null)
            {
                return;
            }

            cloudJob.ActivityChanged += SetActivity;
            cloudJob.Failed += ReportError;
            cloudJob.ResultUpdated += ApplyResult;
            cloudJob.SubmissionStarted += HandleSubmissionStarted;
            artifacts.ActivityChanged += SetActivity;
            artifacts.Failed += ReportError;
            artifacts.ResultRefreshed += ApplyResult;
        }

        private void Unsubscribe()
        {
            if (cloudJob != null)
            {
                cloudJob.ActivityChanged -= SetActivity;
                cloudJob.Failed -= ReportError;
                cloudJob.ResultUpdated -= ApplyResult;
                cloudJob.SubmissionStarted -= HandleSubmissionStarted;
            }

            if (artifacts != null)
            {
                artifacts.ActivityChanged -= SetActivity;
                artifacts.Failed -= ReportError;
                artifacts.ResultRefreshed -= ApplyResult;
            }
        }

        private void SetActivity(string message)
        {
            if (!destroyed)
            {
                activityText = message;
            }
        }

        private void ReportError(string operationName, Exception exception)
        {
            if (destroyed)
            {
                return;
            }

            activityText = $"{operationName}: {exception.Message}";
            Debug.LogException(exception, this);
        }
    }
}
