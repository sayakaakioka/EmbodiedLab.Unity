#nullable enable

using System;
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
        private EmbodiedLabJob? job;
        private bool destroyed;
        private bool monitorRunning;
        private bool cancelRequestRunning;
        private bool modelDownloadRunning;
        private string submissionIdText = "Not submitted";
        private string jobStatusText = "Not submitted";
        private string progressText = "-";
        private string activityText = "Ready.";
        private string modelPathText = "-";

        private void Awake()
        {
            lifetimeCancellation = new CancellationTokenSource();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20, 20, 700, 500), GUI.skin.box);
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
            DrawButton("Cancel Cloud Job", CanCancel(), StartCloudCancellation);
            DrawButton("Download Model", CanDownloadModel(), StartModelDownload);

            GUILayout.Space(12);
            DrawValue("Submission ID", submissionIdText);
            DrawValue("Job status", jobStatusText);
            DrawValue("Progress", progressText);
            DrawValue("Activity", activityText);
            DrawValue("Downloaded model", modelPathText);

            GUILayout.Space(8);
            GUILayout.Label(
                "Local cancellation stops this sample only. Use Cancel Cloud Job " +
                "to stop the remote training job.");
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            destroyed = true;
            lifetimeCancellation?.Cancel();
            DisposeCurrentJob();
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

        private bool CanSubmit()
        {
            return !destroyed &&
                !monitorRunning &&
                !modelDownloadRunning &&
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
                !cancelRequestRunning;
        }

        private bool CanDownloadModel()
        {
            return !destroyed &&
                job?.LatestResult?.Status == ResultStatus.Completed &&
                !modelDownloadRunning;
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
            _ = SubmitAndMonitorAsync();
        }

        private void StartCloudCancellation()
        {
            _ = CancelCloudJobAsync();
        }

        private void StartModelDownload()
        {
            _ = DownloadModelAsync();
        }

        private async Awaitable SubmitAndMonitorAsync()
        {
            if (scenarioJson == null || lifetimeCancellation == null)
            {
                return;
            }

            monitorRunning = true;
            EmbodiedLabJob? submittedJob = null;
            try
            {
                var endpoints = new EmbodiedLabEndpoints(
                    apiBaseUrl.Trim(),
                    resultWebSocketBaseUrl.Trim());
                ScenarioBundle scenario = ScenarioBundleJson.Deserialize(
                    scenarioJson.text);

                activityText = "Submitting scenario and starting training...";
                submittedJob = await EmbodiedLabJob.SubmitAsync(
                    endpoints,
                    scenario,
                    lifetimeCancellation.Token);
                if (destroyed)
                {
                    return;
                }

                DisposeCurrentJob();
                job = submittedJob;
                submittedJob = null;
                job.ResultUpdated += HandleResultUpdated;
                submissionIdText = job.SubmissionId;
                jobStatusText = "Training accepted";
                activityText = "Waiting for WebSocket result updates...";

                ResultDocument result = await job.WaitForCompletionAsync(
                    lifetimeCancellation.Token);
                ApplyResult(result);
            }
            catch (OperationCanceledException)
            {
                if (!destroyed)
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
                submittedJob?.Dispose();
                monitorRunning = false;
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
                ApplyResult(result);
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
            if (activeJob == null || lifetimeCancellation == null)
            {
                return;
            }

            modelDownloadRunning = true;
            try
            {
                string outputDirectory = Path.Combine(
                    Application.persistentDataPath,
                    "EmbodiedLabQuickstart",
                    activeJob.SubmissionId);
                Directory.CreateDirectory(outputDirectory);
                string destinationPath = Path.Combine(
                    outputDirectory,
                    "policy.onnx");

                activityText = "Downloading trained model...";
                await activeJob.DownloadModelAsync(
                    destinationPath,
                    lifetimeCancellation.Token);
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

        private void HandleResultUpdated(ResultDocument result)
        {
            ApplyResult(result);
        }

        private void ApplyResult(ResultDocument result)
        {
            submissionIdText = result.SubmissionId;
            jobStatusText = result.Status.ToString();

            Progress? progress = result.Progress;
            progressText = progress == null
                ? "-"
                : $"{progress.Phase}: {progress.CurrentStep}/{progress.TotalSteps} " +
                    progress.Message;

            activityText = result.Status switch
            {
                ResultStatus.Completed => "Training completed.",
                ResultStatus.Failed => "Training failed.",
                ResultStatus.Cancelled => "Cloud job cancelled.",
                _ => progress?.Message ?? "Waiting for the next result update.",
            };

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                activityText = $"{activityText} {result.Error}";
            }
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

        private void DisposeCurrentJob()
        {
            if (job == null)
            {
                return;
            }

            job.ResultUpdated -= HandleResultUpdated;
            job.Dispose();
            job = null;
        }
    }
}
