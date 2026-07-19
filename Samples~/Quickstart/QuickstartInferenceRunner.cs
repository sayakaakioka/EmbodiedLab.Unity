#nullable enable

using System;
using System.IO;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal sealed class QuickstartInferenceRunner : IDisposable
    {
        internal const float DecisionSeconds = 0.1f;
        internal const float ForwardMetersPerDecision = 0.2f;
        internal const float TurnDegreesPerDecision = 15f;

        private readonly Transform robot;
        private readonly CapsuleCollider robotCollider;
        private readonly Transform goal;
        private readonly Camera forwardCamera;
        private readonly Vector3 startPosition;
        private readonly Quaternion startRotation;
        private readonly float goalRadius;
        private readonly float[] imageObservation =
            new float[QuickstartOnnxContract.ImageValueCount];
        private readonly float[] numericObservation =
            new float[QuickstartOnnxContract.NumericValueCount];

        private QuickstartOnnxPolicy? policy;
        private QuickstartSemanticCamera? semanticCamera;
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
        }

        internal bool IsRunning { get; private set; }

        internal string Status { get; private set; } = "Inference: off";

        internal string ObservationStatus { get; private set; } = "-";

        internal string ActionStatus { get; private set; } = "-";

        internal void Start(string modelPath)
        {
            StopInternal("Inference: off", resetRobot: true);
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                Status = "Inference: selected model file is missing.";
                return;
            }

            try
            {
                policy = new QuickstartOnnxPolicy(modelPath);
                semanticCamera = new QuickstartSemanticCamera(forwardCamera);
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
            if (elapsedSeconds < DecisionSeconds)
            {
                return;
            }

            elapsedSeconds %= DecisionSeconds;
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

        private void Step()
        {
            try
            {
                QuickstartSemanticCamera activeCamera = semanticCamera ??
                    throw new ObjectDisposedException(nameof(QuickstartSemanticCamera));
                QuickstartOnnxPolicy activePolicy = policy ??
                    throw new ObjectDisposedException(nameof(QuickstartOnnxPolicy));
                string imageSummary = activeCamera.Capture(imageObservation);
                QuickstartInferenceMath.WriteNumericObservation(
                    robot.position,
                    robot.rotation.eulerAngles.y,
                    goal.position,
                    numericObservation);
                ObservationStatus =
                    $"angle={numericObservation[0]:0.00} deg | " +
                    $"distance={numericObservation[1]:0.00} m | {imageSummary}";

                QuickstartAppliedAction action =
                    QuickstartInferenceMath.ApplyActionContract(
                        activePolicy.Run(imageObservation, numericObservation));
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
                action.Turn * TurnDegreesPerDecision,
                0f,
                Space.World);
            Physics.SyncTransforms();

            float distance = action.Forward * ForwardMetersPerDecision;
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
            if (resetRobot)
            {
                robot.SetPositionAndRotation(startPosition, startRotation);
                Physics.SyncTransforms();
            }

            Status = status;
        }
    }
}
