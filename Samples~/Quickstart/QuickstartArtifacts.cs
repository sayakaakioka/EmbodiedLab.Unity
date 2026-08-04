#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal sealed class QuickstartArtifacts
    {
        private enum Operation
        {
            None,
            DownloadingModel,
            DownloadingReplay,
        }

        private Operation operation;

        internal event Action<string>? ActivityChanged;

        internal event Action<string, Exception>? Failed;

        internal event Action<ResultDocument>? ResultRefreshed;

        internal bool IsBusy => operation != Operation.None;

        internal string ModelPath { get; private set; } = "-";

        internal OnnxModelArtifactLocation? ModelContract { get; private set; }

        internal string ReplayPath { get; private set; } = "-";

        internal void Reset()
        {
            if (IsBusy)
            {
                throw new InvalidOperationException(
                    "Cannot reset artifacts while a download is active.");
            }

            ModelPath = "-";
            ModelContract = null;
            ReplayPath = "-";
        }

        internal bool HasRunnableModel(
            EmbodiedLabJob? job,
            string persistentDataPath)
        {
            if (job == null || ModelPath == "-" || ModelContract == null)
            {
                return false;
            }

            try
            {
                string expected = QuickstartLocalPaths.GetModelPath(
                    persistentDataPath,
                    job.SubmissionId);
                return PathsEqual(expected, ModelPath) && File.Exists(expected);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal async Task DownloadModelAsync(
            EmbodiedLabJob job,
            string persistentDataPath,
            CancellationToken cancellationToken)
        {
            if (IsBusy)
            {
                throw new InvalidOperationException(
                    "Another artifact download is already active.");
            }

            operation = Operation.DownloadingModel;
            try
            {
                string destinationPath = QuickstartLocalPaths.GetModelPath(
                    persistentDataPath,
                    job.SubmissionId);
                CreateParentDirectory(destinationPath, "Model");
                ReportActivity("Downloading the trained model...");
                await job.DownloadModelAsync(destinationPath, cancellationToken);
                ModelContract = job.LatestResult?.ResultBundle?.Artifacts?.OnnxModel ??
                    throw new InvalidDataException(
                        "The completed result has no ONNX model metadata.");
                ModelPath = destinationPath;
                ReportActivity("Model downloaded.");
            }
            catch (OperationCanceledException)
            {
                ReportActivity("The local model download stopped.");
            }
            catch (Exception exception)
            {
                Failed?.Invoke("Model download failed", exception);
            }
            finally
            {
                operation = Operation.None;
            }
        }

        internal async Task DownloadReplayAsync(
            EmbodiedLabJob job,
            string scenarioId,
            string persistentDataPath,
            Transform robot,
            QuickstartReplayPlayer replayPlayer,
            CancellationToken cancellationToken)
        {
            if (IsBusy)
            {
                throw new InvalidOperationException(
                    "Another artifact download is already active.");
            }

            ReplayPath = "-";
            operation = Operation.DownloadingReplay;
            try
            {
                ReportActivity("Refreshing the completed job...");
                ResultDocument refreshed = await job.RefreshAsync(cancellationToken);
                ResultRefreshed?.Invoke(refreshed);
                if (refreshed.Status != ResultStatus.Completed)
                {
                    throw new InvalidOperationException(
                        "Replay download requires a completed cloud job.");
                }

                string manifestPath = QuickstartLocalPaths.GetReplayManifestPath(
                    persistentDataPath,
                    job.SubmissionId);
                CreateParentDirectory(manifestPath, "Replay manifest");
                ReportActivity("Downloading the replay manifest...");
                await job.DownloadReplayBundleAsync(manifestPath, cancellationToken);

                ReplayBundleManifest manifest = EmbodiedLabReplay.ReadManifest(
                    manifestPath,
                    job.SubmissionId,
                    scenarioId);
                EvalReplayBundleChunk selectedChunk =
                    QuickstartReplayTimeline.SelectLatestDeterministicEvaluationChunk(
                        manifest);
                string chunkPath = QuickstartLocalPaths.GetReplayChunkPath(
                    persistentDataPath,
                    job.SubmissionId,
                    selectedChunk.Path);
                CreateParentDirectory(chunkPath, "Replay chunk");

                ReportActivity($"Downloading replay chunk {selectedChunk.Path}...");
                await job.DownloadReplayChunkAsync(
                    selectedChunk,
                    chunkPath,
                    cancellationToken);

                IReadOnlyList<ReplayLogStep> steps =
                    EmbodiedLabReplay.ReadChunk(
                        chunkPath,
                        selectedChunk,
                        job.SubmissionId,
                        manifest.ScenarioId);
                QuickstartReplayTimeline.ValidateSelectedChunkSteps(
                    job.SubmissionId,
                    manifest.ScenarioId,
                    selectedChunk,
                    steps);
                replayPlayer.Load(robot, steps, selectedChunk.Path);
                ReplayPath = chunkPath;
                ReportActivity("Replay downloaded and ready.");
            }
            catch (OperationCanceledException)
            {
                ReportActivity("The local replay download stopped.");
            }
            catch (Exception exception)
            {
                replayPlayer.Clear();
                Failed?.Invoke("Replay download failed", exception);
            }
            finally
            {
                operation = Operation.None;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static void CreateParentDirectory(string path, string artifactName)
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException(
                    $"{artifactName} output directory is unavailable.");
            }

            Directory.CreateDirectory(directory);
        }

        private void ReportActivity(string message)
        {
            ActivityChanged?.Invoke(message);
        }
    }
}
