#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using EmbodiedLab.Contracts;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal sealed class QuickstartInferenceRunner : IDisposable
    {
        private readonly Transform robot;
        private readonly CapsuleCollider robotCollider;
        private readonly Transform goal;
        private readonly Camera forwardCamera;
        private readonly Vector3 startPosition;
        private readonly Quaternion startRotation;
        private readonly float goalRadius;
        private readonly float decisionSeconds;
        private readonly float forwardMetersPerDecision;
        private readonly float turnDegreesPerDecision;
        private readonly ScenarioBundle scenario;

        private QuickstartOnnxPolicy? policy;
        private QuickstartSemanticCamera? semanticCamera;
        private float[]? imageObservation;
        private float[]? numericObservation;
        private float elapsedSeconds;

        internal QuickstartInferenceRunner(QuickstartWorldBuilder world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            robot = world.RobotTransform ??
                throw new InvalidOperationException("Quickstart robot is unavailable.");
            robotCollider = robot.GetComponent<CapsuleCollider>() ??
                throw new InvalidOperationException("Quickstart robot collider is unavailable.");
            goal = world.GoalTransform ??
                throw new InvalidOperationException("Quickstart goal is unavailable.");
            forwardCamera = world.ForwardCamera ??
                throw new InvalidOperationException(
                    "Submitted forward semantic camera is unavailable.");
            startPosition = world.RobotStartPosition;
            startRotation = world.RobotStartRotation;
            goalRadius = world.GoalRadius;
            scenario = world.Scenario ?? throw new InvalidOperationException(
                "Scenario is unavailable.");
            ActionSpace actionSpace = scenario.Robot?.ActionSpace ??
                throw new InvalidOperationException(
                    "Scenario robot action space is unavailable.");
            decisionSeconds = RequirePositiveFinite(
                actionSpace.StepDurationSeconds,
                "step_duration_seconds");
            forwardMetersPerDecision = RequirePositiveFinite(
                actionSpace.ForwardStepMeters,
                "forward_step_meters");
            turnDegreesPerDecision = RequirePositiveFinite(
                actionSpace.TurnDegreesPerStep,
                "turn_degrees_per_step");
        }

        internal float DecisionSeconds => decisionSeconds;

        internal bool IsRunning { get; private set; }

        internal string Status { get; private set; } = "Inference: off";

        internal string ObservationStatus { get; private set; } = "-";

        internal string ActionStatus { get; private set; } = "-";

        internal void Start(
            string modelPath,
            OnnxModelArtifactLocation modelContract)
        {
            StopInternal("Inference: off", resetRobot: true);
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                Status = "Inference: selected model file is missing.";
                return;
            }

            try
            {
                policy = new QuickstartOnnxPolicy(
                    modelPath,
                    scenario,
                    modelContract);
                QuickstartOnnxContract contract = policy.Contract;
                imageObservation = new float[contract.ImageValueCount];
                numericObservation = new float[contract.NumericValueCount];
                semanticCamera = new QuickstartSemanticCamera(
                    forwardCamera,
                    contract);
                elapsedSeconds = 0f;
                IsRunning = true;
                Status = $"Inference: running {Path.GetFileName(modelPath)}";
                ObservationStatus = "Waiting for first observation.";
                ActionStatus = "Waiting for first action.";
            }
            catch (Exception exception)
            {
                StopInternal(
                    $"Inference load failed: {exception.Message}",
                    resetRobot: true);
                Debug.LogException(exception);
            }
        }

        internal void Tick(float deltaSeconds)
        {
            if (!IsRunning)
            {
                return;
            }

            elapsedSeconds += Math.Max(0f, deltaSeconds);
            if (elapsedSeconds < decisionSeconds)
            {
                return;
            }

            elapsedSeconds %= decisionSeconds;
            Step();
        }

        internal void Stop()
        {
            StopInternal("Inference: stopped and reset.", resetRobot: true);
        }

        public void Dispose()
        {
            StopInternal("Inference: off", resetRobot: true);
        }

        internal void DisposeWithoutReset()
        {
            StopInternal("Inference: off", resetRobot: false);
        }

        private void Step()
        {
            try
            {
                QuickstartSemanticCamera activeCamera = semanticCamera ??
                    throw new ObjectDisposedException(nameof(QuickstartSemanticCamera));
                QuickstartOnnxPolicy activePolicy = policy ??
                    throw new ObjectDisposedException(nameof(QuickstartOnnxPolicy));
                QuickstartOnnxContract contract = activePolicy.Contract;
                float[] activeImageObservation = imageObservation ??
                    throw new InvalidOperationException(
                        "Image observation buffer is unavailable.");
                float[] activeNumericObservation = numericObservation ??
                    throw new InvalidOperationException(
                        "Numeric observation buffer is unavailable.");
                activeCamera.Capture(activeImageObservation);
                QuickstartInferenceMath.WriteNumericObservation(
                    robot.position,
                    robot.rotation.eulerAngles.y,
                    goal.position,
                    contract.NumericValues,
                    activeNumericObservation);
                ObservationStatus = FormatNumericObservation(
                    contract,
                    activeNumericObservation);

                QuickstartAppliedAction action =
                    QuickstartInferenceMath.ApplyActionContract(
                        activePolicy.Run(
                            activeImageObservation,
                            activeNumericObservation));
                ActionStatus = action.FormatSummary();
                ApplyMotion(action);
                if (!IsRunning)
                {
                    return;
                }

                Status = action.ContractViolation
                    ? "Inference: running | CONTRACT VIOLATION: action clamped."
                    : "Inference: running.";
            }
            catch (Exception exception)
            {
                StopInternal(
                    $"Inference failed: {exception.Message}",
                    resetRobot: true);
                Debug.LogException(exception);
            }
        }

        private void ApplyMotion(QuickstartAppliedAction action)
        {
            robot.Rotate(
                0f,
                action.Turn * turnDegreesPerDecision,
                0f,
                Space.World);
            Physics.SyncTransforms();

            float distance = action.Forward * forwardMetersPerDecision;
            if (distance > 0f && WouldHitBlockedGeometry(robot.forward, distance, out string hit))
            {
                StopInternal(
                    $"Inference stopped: wall collision ({hit}).",
                    resetRobot: true);
                return;
            }

            robot.position += robot.forward * distance;
            Physics.SyncTransforms();
            float deltaX = goal.position.x - robot.position.x;
            float deltaZ = goal.position.z - robot.position.z;
            if (Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ) <= goalRadius)
            {
                StopInternal("Inference stopped: goal reached.", resetRobot: true);
            }
        }

        private bool WouldHitBlockedGeometry(
            Vector3 direction,
            float distance,
            out string hitName)
        {
            Bounds bounds = robotCollider.bounds;
            float radius = Math.Max(0.01f, Math.Min(bounds.extents.x, bounds.extents.z));
            float halfLine = Math.Max(0f, bounds.extents.y - radius);
            Vector3 top = bounds.center + Vector3.up * halfLine;
            Vector3 bottom = bounds.center - Vector3.up * halfLine;
            if (Physics.CapsuleCast(
                top,
                bottom,
                radius,
                direction,
                out RaycastHit hit,
                distance,
                1 << QuickstartWorldBuilder.BlockedLayer,
                QueryTriggerInteraction.Ignore))
            {
                hitName = hit.collider == null ? "blocked geometry" : hit.collider.name;
                return true;
            }

            hitName = string.Empty;
            return false;
        }

        private void StopInternal(string status, bool resetRobot)
        {
            IsRunning = false;
            elapsedSeconds = 0f;
            semanticCamera?.Dispose();
            semanticCamera = null;
            policy?.Dispose();
            policy = null;
            imageObservation = null;
            numericObservation = null;
            if (resetRobot && robot != null)
            {
                robot.SetPositionAndRotation(startPosition, startRotation);
                Physics.SyncTransforms();
            }

            Status = status;
        }

        private static float RequirePositiveFinite(double value, string fieldName)
        {
            float converted = (float)value;
            if (converted <= 0f || float.IsNaN(converted) || float.IsInfinity(converted))
            {
                throw new InvalidOperationException(
                    $"Scenario {fieldName} must be a positive finite value.");
            }

            return converted;
        }

        private static string FormatNumericObservation(
            QuickstartOnnxContract contract,
            IReadOnlyList<float> values)
        {
            var parts = new string[contract.NumericValues.Count];
            for (int index = 0; index < contract.NumericValues.Count; index++)
            {
                parts[index] = contract.NumericValues[index] switch
                {
                    Values.GoalAngleDegrees => $"angle={values[index]:0.00} deg",
                    Values.GoalDistanceMeters => $"distance={values[index]:0.00} m",
                    _ => throw new InvalidDataException(
                        $"Unsupported goal-vector value '{contract.NumericValues[index]}'."),
                };
            }

            return string.Join(" | ", parts);
        }
    }
}
