#nullable enable

using System;
using System.Collections.Generic;
using EmbodiedLab.Contracts;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal sealed class QuickstartReplayPlayer
    {
        private QuickstartReplayTimeline? timeline;
        private Transform? robot;
        private float robotHeight;

        internal bool IsLoaded => timeline != null;

        internal bool IsPlaying => timeline?.IsPlaying == true;

        internal string SelectedChunk { get; private set; } = "-";

        internal string CurrentEpisode { get; private set; } = "-";

        internal string CurrentStep { get; private set; } = "-";

        internal string Status { get; private set; } = "No replay loaded.";

        internal void Load(
            Transform robot,
            IReadOnlyList<ReplayLogStep> steps,
            string selectedChunk)
        {
            this.robot = robot ?? throw new ArgumentNullException(nameof(robot));
            if (string.IsNullOrWhiteSpace(selectedChunk))
            {
                throw new ArgumentException(
                    "Selected replay chunk cannot be empty.",
                    nameof(selectedChunk));
            }

            timeline = new QuickstartReplayTimeline(steps);
            robotHeight = robot.position.y;
            SelectedChunk = selectedChunk;
            ApplyFrame(timeline.Stop());
            Status = "Replay ready.";
        }

        internal void Play()
        {
            QuickstartReplayTimeline activeTimeline = timeline ??
                throw new InvalidOperationException("Download a replay before playing it.");
            ApplyFrame(activeTimeline.Play());
            Status = "Playing replay.";
        }

        internal void Stop()
        {
            if (timeline == null)
            {
                return;
            }

            ApplyFrame(timeline.Stop());
            Status = "Replay stopped at the first step.";
        }

        internal void Tick(double deltaSeconds)
        {
            if (timeline == null || !timeline.IsPlaying)
            {
                return;
            }

            ApplyFrame(timeline.Advance(deltaSeconds));
            Status = timeline.IsPlaying
                ? timeline.IsEpisodePause
                    ? "Pausing between replay episodes."
                    : "Playing replay."
                : "Replay completed.";
        }

        internal void Clear()
        {
            if (timeline != null && robot != null)
            {
                ApplyFrame(timeline.Stop());
            }

            timeline = null;
            robot = null;
            SelectedChunk = "-";
            CurrentEpisode = "-";
            CurrentStep = "-";
            Status = "No replay loaded.";
        }

        private void ApplyFrame(QuickstartReplayFrame frame)
        {
            Transform activeRobot = robot ??
                throw new InvalidOperationException("Replay robot is unavailable.");
            ReplayRobotState fromRobot = frame.FromStep.Robot ??
                throw new InvalidOperationException("Replay step robot state is missing.");
            ReplayRobotState toRobot = frame.ToStep.Robot ??
                throw new InvalidOperationException("Replay step robot state is missing.");
            ReplayPosition fromPosition = fromRobot.Position ??
                throw new InvalidOperationException("Replay step robot position is missing.");
            ReplayPosition toPosition = toRobot.Position ??
                throw new InvalidOperationException("Replay step robot position is missing.");

            activeRobot.position = new Vector3(
                Convert.ToSingle(
                    frame.Interpolate(fromPosition.X, toPosition.X)),
                robotHeight,
                Convert.ToSingle(
                    frame.Interpolate(fromPosition.Z, toPosition.Z)));
            activeRobot.rotation = Quaternion.Euler(
                0f,
                Convert.ToSingle(
                    frame.InterpolateAngleDegrees(
                        fromRobot.RotationYDegrees,
                        toRobot.RotationYDegrees)),
                0f);
            CurrentEpisode = frame.FromStep.EpisodeId;
            CurrentStep = frame.FromStep.StepIndex.ToString();
        }
    }
}
