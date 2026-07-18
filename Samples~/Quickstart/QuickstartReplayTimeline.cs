#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using EmbodiedLab.Contracts;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal readonly struct QuickstartReplayFrame
    {
        internal QuickstartReplayFrame(
            ReplayLogStep fromStep,
            ReplayLogStep toStep,
            double interpolation)
        {
            FromStep = fromStep;
            ToStep = toStep;
            Interpolation = interpolation;
        }

        internal ReplayLogStep FromStep { get; }

        internal ReplayLogStep ToStep { get; }

        internal double Interpolation { get; }

        internal double Interpolate(double from, double to)
        {
            return from + ((to - from) * Interpolation);
        }

        internal double InterpolateAngleDegrees(double from, double to)
        {
            double normalizedFrom = NormalizeDegrees(from);
            double normalizedTo = NormalizeDegrees(to);
            double delta = normalizedTo - normalizedFrom;
            if (delta > 180D)
            {
                delta -= 360D;
            }
            else if (delta < -180D)
            {
                delta += 360D;
            }

            return normalizedFrom + (delta * Interpolation);
        }

        private static double NormalizeDegrees(double value)
        {
            double normalized = value % 360D;
            return normalized < 0D ? normalized + 360D : normalized;
        }
    }

    internal sealed class QuickstartReplayTimeline
    {
        internal const double EpisodePauseSeconds = 0.5D;

        private readonly IReadOnlyList<ReplayLogStep> steps;
        private int currentIndex;
        private double segmentElapsedSeconds;
        private double episodePauseRemainingSeconds;

        internal QuickstartReplayTimeline(IReadOnlyList<ReplayLogStep> steps)
        {
            if (steps == null)
            {
                throw new ArgumentNullException(nameof(steps));
            }

            if (steps.Count == 0)
            {
                throw new ArgumentException("Replay must contain at least one step.", nameof(steps));
            }

            var copiedSteps = new List<ReplayLogStep>(steps.Count);
            for (int index = 0; index < steps.Count; index++)
            {
                ReplayLogStep step = steps[index] ?? throw new ArgumentException(
                    "Replay cannot contain a null step.",
                    nameof(steps));
                if (string.IsNullOrWhiteSpace(step.EpisodeId))
                {
                    throw new ArgumentException(
                        "Replay steps must identify their episode.",
                        nameof(steps));
                }

                if (double.IsNaN(step.TimeSeconds) ||
                    double.IsInfinity(step.TimeSeconds) ||
                    step.TimeSeconds < 0D)
                {
                    throw new ArgumentException(
                        "Replay step times must be finite and non-negative.",
                        nameof(steps));
                }

                ReplayRobotState robot = step.Robot ?? throw new ArgumentException(
                    "Replay steps must contain robot state.",
                    nameof(steps));
                ReplayPosition position = robot.Position ?? throw new ArgumentException(
                    "Replay robot state must contain a position.",
                    nameof(steps));
                if (step.StepIndex < 0 ||
                    !IsFloatRepresentable(position.X) ||
                    !IsFloatRepresentable(position.Z) ||
                    !IsFinite(robot.RotationYDegrees))
                {
                    throw new ArgumentException(
                        "Replay robot values must be finite and step indices non-negative.",
                        nameof(steps));
                }

                if (index > 0 &&
                    !IsEpisodeBoundary(steps[index - 1], step) &&
                    step.TimeSeconds < steps[index - 1].TimeSeconds)
                {
                    throw new ArgumentException(
                        "Replay step times must not decrease within one episode.",
                        nameof(steps));
                }

                copiedSteps.Add(step);
            }

            this.steps = copiedSteps;
        }

        internal bool IsPlaying { get; private set; }

        internal bool IsEpisodePause => episodePauseRemainingSeconds > 0D;

        internal QuickstartReplayFrame CurrentFrame => CreateCurrentFrame();

        internal static ReplayBundleChunk SelectLatestDeterministicEvaluationChunk(
            ReplayBundleManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            ReplayBundleChunk? selected = null;
            foreach (ReplayBundleChunk chunk in manifest.Chunks ??
                throw new InvalidOperationException("Replay manifest chunks are missing."))
            {
                if (chunk == null ||
                    chunk.Phase != ReplayBundleChunkPhase.Eval ||
                    chunk.PolicyMode != ReplayBundleChunkPolicyMode.Deterministic)
                {
                    continue;
                }

                if (selected == null || chunk.CheckpointStep >= selected.CheckpointStep)
                {
                    selected = chunk;
                }
            }

            return selected ?? throw new InvalidOperationException(
                "Replay manifest does not contain a deterministic evaluation chunk.");
        }

        internal static void ValidateSelectedChunkSteps(
            string submissionId,
            string scenarioId,
            ReplayBundleChunk selectedChunk,
            IReadOnlyList<ReplayLogStep> steps)
        {
            if (selectedChunk == null)
            {
                throw new ArgumentNullException(nameof(selectedChunk));
            }

            if (steps == null)
            {
                throw new ArgumentNullException(nameof(steps));
            }

            if (steps.Count == 0)
            {
                throw new InvalidDataException("Replay chunk does not contain any steps.");
            }

            if (steps.Count != selectedChunk.StepCount)
            {
                throw new InvalidDataException(
                    "Replay chunk step count does not match its manifest entry.");
            }

            foreach (ReplayLogStep step in steps)
            {
                if (step == null ||
                    !string.Equals(step.JobId, submissionId, StringComparison.Ordinal) ||
                    !string.Equals(step.ScenarioId, scenarioId, StringComparison.Ordinal) ||
                    !string.Equals(step.Phase, "eval", StringComparison.Ordinal) ||
                    !string.Equals(
                        step.PolicyMode,
                        "deterministic",
                        StringComparison.Ordinal) ||
                    step.CheckpointStep != selectedChunk.CheckpointStep ||
                    step.Robot == null ||
                    step.Robot.Position == null)
                {
                    throw new InvalidDataException(
                        "Replay chunk contains a step outside the selected evaluation replay.");
                }
            }
        }

        internal static bool IsEpisodeBoundary(ReplayLogStep left, ReplayLogStep right)
        {
            if (left == null)
            {
                throw new ArgumentNullException(nameof(left));
            }

            if (right == null)
            {
                throw new ArgumentNullException(nameof(right));
            }

            return !string.Equals(left.EpisodeId, right.EpisodeId, StringComparison.Ordinal);
        }

        internal static bool CanInterpolate(ReplayLogStep left, ReplayLogStep right)
        {
            if (left == null)
            {
                throw new ArgumentNullException(nameof(left));
            }

            if (right == null)
            {
                throw new ArgumentNullException(nameof(right));
            }

            return !IsEpisodeBoundary(left, right) &&
                left.StepIndex < int.MaxValue &&
                right.StepIndex == left.StepIndex + 1;
        }

        internal QuickstartReplayFrame Play()
        {
            if (currentIndex == steps.Count - 1)
            {
                ResetClock();
            }

            IsPlaying = true;
            return CurrentFrame;
        }

        internal QuickstartReplayFrame Stop()
        {
            IsPlaying = false;
            ResetClock();
            return CurrentFrame;
        }

        internal QuickstartReplayFrame Advance(double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) ||
                double.IsInfinity(deltaSeconds) ||
                deltaSeconds < 0D)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaSeconds),
                    "Replay delta time must be finite and non-negative.");
            }

            if (!IsPlaying || deltaSeconds == 0D)
            {
                return CurrentFrame;
            }

            double remainingSeconds = deltaSeconds;
            while (IsPlaying)
            {
                if (currentIndex >= steps.Count - 1)
                {
                    IsPlaying = false;
                    return CurrentFrame;
                }

                ReplayLogStep current = steps[currentIndex];
                ReplayLogStep next = steps[currentIndex + 1];
                if (IsEpisodeBoundary(current, next))
                {
                    if (episodePauseRemainingSeconds <= 0D)
                    {
                        episodePauseRemainingSeconds = EpisodePauseSeconds;
                    }

                    if (remainingSeconds < episodePauseRemainingSeconds)
                    {
                        episodePauseRemainingSeconds -= remainingSeconds;
                        return CurrentFrame;
                    }

                    remainingSeconds -= episodePauseRemainingSeconds;
                    episodePauseRemainingSeconds = 0D;
                    currentIndex++;
                    segmentElapsedSeconds = 0D;
                    if (remainingSeconds == 0D)
                    {
                        return CurrentFrame;
                    }

                    continue;
                }

                double segmentDuration = next.TimeSeconds - current.TimeSeconds;
                if (segmentDuration <= 0D)
                {
                    currentIndex++;
                    segmentElapsedSeconds = 0D;
                    if (remainingSeconds == 0D)
                    {
                        return CurrentFrame;
                    }

                    continue;
                }

                double remainingSegmentSeconds = segmentDuration - segmentElapsedSeconds;
                if (remainingSeconds < remainingSegmentSeconds)
                {
                    segmentElapsedSeconds += remainingSeconds;
                    return CurrentFrame;
                }

                remainingSeconds -= remainingSegmentSeconds;
                currentIndex++;
                segmentElapsedSeconds = 0D;
            }

            return CurrentFrame;
        }

        private QuickstartReplayFrame CreateCurrentFrame()
        {
            ReplayLogStep current = steps[currentIndex];
            if (currentIndex >= steps.Count - 1 ||
                !CanInterpolate(current, steps[currentIndex + 1]) ||
                episodePauseRemainingSeconds > 0D)
            {
                return new QuickstartReplayFrame(current, current, 0D);
            }

            ReplayLogStep next = steps[currentIndex + 1];
            double duration = next.TimeSeconds - current.TimeSeconds;
            double interpolation = duration <= 0D
                ? 1D
                : Math.Min(1D, segmentElapsedSeconds / duration);
            return new QuickstartReplayFrame(current, next, interpolation);
        }

        private void ResetClock()
        {
            currentIndex = 0;
            segmentElapsedSeconds = 0D;
            episodePauseRemainingSeconds = 0D;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFloatRepresentable(double value)
        {
            return IsFinite(value) &&
                value >= -float.MaxValue &&
                value <= float.MaxValue;
        }
    }
}
