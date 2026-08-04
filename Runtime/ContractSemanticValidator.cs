#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmbodiedLab.Contracts;

namespace EmbodiedLab.Unity.Internal
{
    internal static class ContractSemanticValidator
    {
        private const string ForwardActionName = "forward";
        private const string ForwardActionMapping = "sigmoid(policy_forward)";
        private const string TurnActionName = "turn";
        private const string TurnActionMapping = "clip(policy_turn, -3, 3) / 3";

        internal static void ValidateResultDocument(ResultDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            RequireText(document.SubmissionId, "Result submission_id");
            RequireText(document.UpdatedAt, "Result updated_at");
            Progress progress = document.Progress ?? throw new InvalidDataException(
                "Result progress is required.");
            if (document.Status != progress.Phase)
            {
                throw new InvalidDataException(
                    "Result status must match progress.phase.");
            }

            if (progress.CurrentStep < 0 ||
                progress.TotalSteps < 0 ||
                progress.CurrentStep > progress.TotalSteps)
            {
                throw new InvalidDataException(
                    "Result progress steps are inconsistent.");
            }

            RequireText(progress.Message, "Result progress message");
            switch (document.Status)
            {
                case ResultStatus.Completed:
                    if (document.Error != null || document.ResultBundle == null)
                    {
                        throw new InvalidDataException(
                            "A completed result requires a bundle and no error.");
                    }

                    ValidateResultBundle(document.ResultBundle);
                    RequireMatchingBundle(document, ResultBundleStatus.Completed);
                    break;

                case ResultStatus.Failed:
                    RequireText(document.Error, "Failed result error");
                    if (document.ResultBundle != null)
                    {
                        ValidateResultBundle(document.ResultBundle);
                        RequireMatchingBundle(document, ResultBundleStatus.Failed);
                    }

                    break;

                default:
                    if (document.Error != null || document.ResultBundle != null)
                    {
                        throw new InvalidDataException(
                            "A nonterminal or cancelled result cannot contain terminal output.");
                    }

                    break;
            }
        }

        internal static void ValidateReplayManifest(ReplayBundleManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            RequireText(manifest.JobId, "Replay manifest job_id");
            RequireText(manifest.ScenarioId, "Replay manifest scenario_id");
            if (manifest.TotalTimesteps <= 0)
            {
                throw new InvalidDataException(
                    "Replay manifest total_timesteps must be positive.");
            }

            ICollection<ReplayBundleChunk> chunks = manifest.Chunks ??
                throw new InvalidDataException("Replay manifest chunks are required.");
            if (chunks.Count == 0)
            {
                throw new InvalidDataException(
                    "Replay manifest must contain at least one chunk.");
            }

            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (ReplayBundleChunk chunk in chunks)
            {
                if (chunk == null)
                {
                    throw new InvalidDataException(
                        "Replay manifest contains an empty chunk entry.");
                }

                ValidateDigestAndSize(chunk.SizeBytes, chunk.Sha256, "Replay chunk");
                if (chunk.CheckpointStep < 0 ||
                    chunk.CheckpointStep > manifest.TotalTimesteps)
                {
                    throw new InvalidDataException(
                        "Replay chunk checkpoint_step is outside the training range.");
                }

                string path;
                switch (chunk)
                {
                    case TrainReplayBundleChunk train:
                        path = train.Path;
                        ValidateReplayPath(path, "train");
                        if (train.StartStep < 0 ||
                            train.EndStep < train.StartStep ||
                            train.CheckpointStep != train.EndStep ||
                            train.StepCount <= 0 ||
                            train.EpisodeCount != null ||
                            train.SuccessRate != null ||
                            train.AvgReward != null ||
                            train.AvgSteps != null)
                        {
                            throw new InvalidDataException(
                                "Training replay chunk step metadata is inconsistent.");
                        }

                        break;

                    case EvalReplayBundleChunk evaluation:
                        path = evaluation.Path;
                        ValidateReplayPath(path, "eval");
                        if (evaluation.StartStep != null ||
                            evaluation.EndStep != null ||
                            evaluation.StepCount < 0 ||
                            evaluation.EpisodeCount < 0 ||
                            !IsFinite(evaluation.SuccessRate) ||
                            evaluation.SuccessRate < 0.0 ||
                            evaluation.SuccessRate > 1.0 ||
                            !IsFinite(evaluation.AvgReward) ||
                            !IsFinite(evaluation.AvgSteps) ||
                            evaluation.AvgSteps < 0.0)
                        {
                            throw new InvalidDataException(
                                "Evaluation replay chunk metrics are invalid.");
                        }

                        break;

                    default:
                        throw new InvalidDataException(
                            "Replay manifest contains an unsupported chunk type.");
                }

                if (!paths.Add(path))
                {
                    throw new InvalidDataException(
                        "Replay manifest contains duplicate chunk paths.");
                }
            }
        }

        internal static void ValidateReplayStep(ReplayLogStep step)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            bool validMode =
                (step.Phase == ReplayLogStepPhase.Train &&
                    step.PolicyMode == ReplayLogStepPolicyMode.Stochastic) ||
                (step.Phase == ReplayLogStepPhase.Eval &&
                    step.PolicyMode == ReplayLogStepPolicyMode.Deterministic);
            if (!validMode)
            {
                throw new InvalidDataException(
                    "Replay phase and policy_mode are inconsistent.");
            }

            if (step.CheckpointStep < 0 ||
                step.EnvIndex < 0 ||
                step.StepIndex < 0 ||
                !IsFinite(step.TimeSeconds) ||
                step.TimeSeconds < 0.0)
            {
                throw new InvalidDataException("Replay step indexes or time are invalid.");
            }

            RequireText(step.JobId, "Replay job_id");
            RequireText(step.ScenarioId, "Replay scenario_id");
            RequireText(step.EpisodeId, "Replay episode_id");
            ReplayActionValue[] actions = step.Action?.Values?.ToArray() ??
                throw new InvalidDataException("Replay action values are required.");
            if (actions.Length != 2 ||
                actions[0] is not ReplayForwardActionValue ||
                actions[1] is not ReplayTurnActionValue ||
                actions.Any(action => !IsFinite(action.Value)))
            {
                throw new InvalidDataException(
                    "Replay actions must be finite forward and turn values in that order.");
            }

            ReplayRobotState robot = step.Robot ?? throw new InvalidDataException(
                "Replay robot state is required.");
            ReplayPosition position = robot.Position ?? throw new InvalidDataException(
                "Replay robot position is required.");
            RequireFinite(position.X, "Replay robot x");
            RequireFinite(position.Z, "Replay robot z");
            RequireFinite(robot.RotationYDegrees, "Replay robot rotation");

            ReplayReward reward = step.Reward ?? throw new InvalidDataException(
                "Replay reward is required.");
            RequireFinite(reward.Total, "Replay reward total");
            foreach (ReplayNamedValue component in reward.Components ??
                throw new InvalidDataException("Replay reward components are required."))
            {
                if (component == null)
                {
                    throw new InvalidDataException(
                        "Replay reward contains an empty component.");
                }

                RequireText(component.Name, "Replay reward component name");
                RequireFinite(component.Value, "Replay reward component value");
            }

            foreach (ReplaySensorSummary sensor in step.Sensors ??
                throw new InvalidDataException("Replay sensors are required."))
            {
                if (sensor == null)
                {
                    throw new InvalidDataException(
                        "Replay sensors contain an empty entry.");
                }

                RequireText(sensor.Id, "Replay sensor id");
                RequireText(sensor.Type, "Replay sensor type");
                RequireFinite(sensor.Value, "Replay sensor value");
            }
        }

        private static void ValidateResultBundle(ResultBundle bundle)
        {
            RequireText(bundle.JobId, "Result bundle job_id");
            RequireText(bundle.ScenarioId, "Result bundle scenario_id");
            ResultArtifacts artifacts = bundle.Artifacts ?? throw new InvalidDataException(
                "Result artifacts are required.");
            switch (bundle.Status)
            {
                case ResultBundleStatus.Completed:
                    if (bundle.Summary == null ||
                        bundle.Error != null ||
                        artifacts.OnnxModel == null ||
                        artifacts.SentisModel == null ||
                        artifacts.ReplayBundle == null)
                    {
                        throw new InvalidDataException(
                            "A completed bundle requires its summary and all artifacts.");
                    }

                    ValidateArtifact(
                        artifacts.OnnxModel.Bucket,
                        artifacts.OnnxModel.Path,
                        artifacts.OnnxModel.SizeBytes,
                        artifacts.OnnxModel.Sha256,
                        "ONNX model");
                    ValidateArtifact(
                        artifacts.SentisModel.Bucket,
                        artifacts.SentisModel.Path,
                        artifacts.SentisModel.SizeBytes,
                        artifacts.SentisModel.Sha256,
                        "Sentis model");
                    ValidateArtifact(
                        artifacts.ReplayBundle.Bucket,
                        artifacts.ReplayBundle.Path,
                        artifacts.ReplayBundle.SizeBytes,
                        artifacts.ReplayBundle.Sha256,
                        "Replay bundle");
                    ValidateCompatibility(bundle.Compatibility);
                    ValidateTrainingSummary(bundle.Summary);
                    ValidateOnnxModel(artifacts.OnnxModel);
                    ValidateSentisModel(artifacts.SentisModel);
                    if (artifacts.ReplayBundle.Storage != ArtifactStorage.Gcs ||
                        artifacts.ReplayBundle.Format != ArtifactLocationFormat.Json)
                    {
                        throw new InvalidDataException(
                            "Replay bundle storage or format is invalid.");
                    }
                    break;

                case ResultBundleStatus.Failed:
                    if (bundle.Summary != null ||
                        bundle.Error == null ||
                        artifacts.OnnxModel != null ||
                        artifacts.SentisModel != null ||
                        artifacts.ReplayBundle != null)
                    {
                        throw new InvalidDataException(
                            "A failed bundle requires only an error report.");
                    }

                    break;

                default:
                    throw new InvalidDataException("Unsupported result bundle status.");
            }
        }

        private static void RequireMatchingBundle(
            ResultDocument document,
            ResultBundleStatus expectedStatus)
        {
            ResultBundle bundle = document.ResultBundle ?? throw new InvalidDataException(
                "Result bundle is required.");
            if (bundle.Status != expectedStatus ||
                !string.Equals(
                    bundle.JobId,
                    document.SubmissionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Result bundle status and job_id must match the result document.");
            }
        }

        private static void ValidateArtifact(
            string bucket,
            string path,
            int sizeBytes,
            string sha256,
            string description)
        {
            RequireText(bucket, $"{description} bucket");
            RequireText(path, $"{description} path");
            ValidateDigestAndSize(sizeBytes, sha256, description);
        }

        private static void ValidateOnnxModel(OnnxModelArtifactLocation model)
        {
            if (model.Storage != ArtifactStorage.Gcs ||
                model.Format != OnnxModelArtifactLocationFormat.Onnx ||
                model.Target != OnnxModelArtifactLocationTarget.OnnxRuntime ||
                model.OpsetVersion != OnnxModelArtifactLocationOpsetVersion._17)
            {
                throw new InvalidDataException(
                    "ONNX model storage, format, target, or opset is invalid.");
            }

            ValidateModelMetadata(model.Inputs, model.Output, "ONNX model");
        }

        private static void ValidateSentisModel(SentisModelArtifactLocation model)
        {
            if (model.Storage != ArtifactStorage.Gcs ||
                model.Format != SentisModelArtifactLocationFormat.Onnx ||
                model.Target != SentisModelArtifactLocationTarget.UnitySentis ||
                model.OpsetVersion != SentisModelArtifactLocationOpsetVersion._15)
            {
                throw new InvalidDataException(
                    "Sentis model storage, format, target, or opset is invalid.");
            }

            ValidateModelMetadata(model.Inputs, model.Output, "Sentis model");
        }

        private static void ValidateModelMetadata(
            ICollection<ModelInput> inputs,
            ModelOutput output,
            string description)
        {
            if (inputs == null || inputs.Count < 1 || inputs.Count > 16)
            {
                throw new InvalidDataException(
                    $"{description} input metadata is invalid.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModelInput input in inputs)
            {
                if (input == null)
                {
                    throw new InvalidDataException(
                        $"{description} contains an empty input.");
                }

                RequireText(input.Name, $"{description} input name");
                RequireText(input.Dtype, $"{description} input dtype");
                if (!names.Add(input.Name) ||
                    input.Shape == null ||
                    input.Shape.Count < 1 ||
                    input.Shape.Count > 8 ||
                    input.Layout == null ||
                    input.Layout.Count > 256 ||
                    input.Layout.Any(string.IsNullOrWhiteSpace))
                {
                    throw new InvalidDataException(
                        $"{description} input metadata is invalid.");
                }
            }

            ValidateSupportedModelOutput(output, description);
        }

        internal static void ValidateSupportedModelOutput(
            ModelOutput output,
            string description)
        {
            if (output == null)
            {
                throw new InvalidDataException($"{description} output is required.");
            }

            RequireText(output.Name, $"{description} output name");
            if (output.Layout == null ||
                !output.Layout.SequenceEqual(
                    new[] { ForwardActionName, TurnActionName },
                    StringComparer.Ordinal) ||
                output.ActionMapping == null ||
                output.ActionMapping.Count != 2 ||
                !output.ActionMapping.TryGetValue(
                    ForwardActionName,
                    out string? forwardMapping) ||
                !string.Equals(
                    forwardMapping,
                    ForwardActionMapping,
                    StringComparison.Ordinal) ||
                !output.ActionMapping.TryGetValue(
                    TurnActionName,
                    out string? turnMapping) ||
                !string.Equals(
                    turnMapping,
                    TurnActionMapping,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{description} output metadata does not match the supported " +
                    "forward/turn action contract.");
            }
        }

        private static void ValidateCompatibility(ResultCompatibility compatibility)
        {
            if (compatibility == null)
            {
                throw new InvalidDataException("Result compatibility is required.");
            }

            RequireText(compatibility.ScenarioSchemaVersion, "Scenario schema version");
            RequireText(compatibility.RobotVersion, "Robot version");
            RequireText(compatibility.SensorVersion, "Sensor version");
            ValidateNonemptyUniqueText(
                compatibility.ActionLayout,
                "Result action layout");
            ValidateNonemptyUniqueText(
                compatibility.ObservationLayout,
                "Result observation layout");
        }

        private static void ValidateTrainingSummary(TrainingSummary summary)
        {
            if (summary == null || summary.Configuration == null)
            {
                throw new InvalidDataException(
                    "Completed result training summary is incomplete.");
            }

            if (summary.SuccessRate.HasValue &&
                (!IsFinite(summary.SuccessRate.Value) ||
                    summary.SuccessRate.Value < 0.0 ||
                    summary.SuccessRate.Value > 1.0) ||
                summary.AverageEpisodeReward.HasValue &&
                !IsFinite(summary.AverageEpisodeReward.Value) ||
                summary.AverageEpisodeSteps.HasValue &&
                (!IsFinite(summary.AverageEpisodeSteps.Value) ||
                    summary.AverageEpisodeSteps.Value < 0.0))
            {
                throw new InvalidDataException(
                    "Completed result training metrics are invalid.");
            }
        }

        private static void ValidateNonemptyUniqueText(
            ICollection<string> values,
            string description)
        {
            if (values == null ||
                values.Count == 0 ||
                values.Count > 256 ||
                values.Any(string.IsNullOrWhiteSpace) ||
                values.Distinct(StringComparer.Ordinal).Count() != values.Count)
            {
                throw new InvalidDataException($"{description} is invalid.");
            }
        }

        private static void ValidateDigestAndSize(
            int sizeBytes,
            string sha256,
            string description)
        {
            if (sizeBytes < 0 ||
                sha256 == null ||
                sha256.Length != 64 ||
                sha256.Any(character =>
                    !(character >= '0' && character <= '9') &&
                    !(character >= 'a' && character <= 'f')))
            {
                throw new InvalidDataException(
                    $"{description} size or SHA-256 is invalid.");
            }
        }

        private static void ValidateReplayPath(string path, string phase)
        {
            RequireText(path, "Replay chunk path");
            string prefix = phase + "/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal) ||
                !path.EndsWith(".jsonl.gz", StringComparison.Ordinal) ||
                path.IndexOf('/', prefix.Length) >= 0 ||
                path.Substring(prefix.Length).Any(character =>
                    !IsAsciiPathCharacter(character)))
            {
                throw new InvalidDataException(
                    "Replay chunk path does not match its phase.");
            }
        }

        private static void RequireFinite(double value, string description)
        {
            if (!IsFinite(value))
            {
                throw new InvalidDataException($"{description} must be finite.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsAsciiPathCharacter(char value)
        {
            return (value >= 'A' && value <= 'Z') ||
                (value >= 'a' && value <= 'z') ||
                (value >= '0' && value <= '9') ||
                value == '.' ||
                value == '_' ||
                value == '-';
        }

        private static void RequireText(string? value, string description)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"{description} is required.");
            }
        }
    }
}
