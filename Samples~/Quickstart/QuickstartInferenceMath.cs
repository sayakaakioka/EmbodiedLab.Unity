#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using EmbodiedLab.Contracts;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal readonly struct QuickstartRawAction
    {
        internal QuickstartRawAction(float forward, float turn)
        {
            Forward = forward;
            Turn = turn;
        }

        internal float Forward { get; }

        internal float Turn { get; }
    }

    internal readonly struct QuickstartAppliedAction
    {
        internal QuickstartAppliedAction(
            float rawForward,
            float rawTurn,
            float forward,
            float turn,
            bool contractViolation)
        {
            RawForward = rawForward;
            RawTurn = rawTurn;
            Forward = forward;
            Turn = turn;
            ContractViolation = contractViolation;
        }

        internal float RawForward { get; }

        internal float RawTurn { get; }

        internal float Forward { get; }

        internal float Turn { get; }

        internal bool ContractViolation { get; }

        internal string FormatSummary()
        {
            string violation = ContractViolation
                ? " | CONTRACT VIOLATION: action clamped"
                : string.Empty;
            return $"raw f={RawForward:0.000} t={RawTurn:0.000} | " +
                $"applied f={Forward:0.000} t={Turn:0.000}{violation}";
        }
    }

    internal static class QuickstartInferenceMath
    {
        internal static void WriteNumericObservation(
            Vector3 robotPosition,
            float robotYawDegrees,
            Vector3 goalPosition,
            IReadOnlyList<Values> values,
            float[] destination)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (destination == null || destination.Length != values.Count)
            {
                throw new ArgumentException(
                    "Numeric observation destination must match the Scenario value layout.",
                    nameof(destination));
            }

            float deltaX = goalPosition.x - robotPosition.x;
            float deltaZ = goalPosition.z - robotPosition.z;
            float targetDegrees = Mathf.Atan2(deltaX, deltaZ) * Mathf.Rad2Deg;
            float goalAngleDegrees = Mathf.Repeat(
                targetDegrees - robotYawDegrees + 180f,
                360f) - 180f;
            float goalDistanceMeters = Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            for (int index = 0; index < values.Count; index++)
            {
                destination[index] = values[index] switch
                {
                    Values.GoalAngleDegrees => goalAngleDegrees,
                    Values.GoalDistanceMeters => goalDistanceMeters,
                    _ => throw new InvalidDataException(
                        $"Unsupported goal-vector value '{values[index]}'."),
                };
            }
        }

        internal static void ConvertRgbToVerticallyFlippedChw(
            Color32[] source,
            int width,
            int height,
            IReadOnlyList<string> channelLayout,
            float[] destination)
        {
            if (source == null || source.Length != width * height)
            {
                throw new ArgumentException(
                    "RGB source size does not match its dimensions.",
                    nameof(source));
            }

            if (channelLayout == null)
            {
                throw new ArgumentNullException(nameof(channelLayout));
            }

            int planeSize = width * height;
            if (destination == null ||
                destination.Length != planeSize * channelLayout.Count)
            {
                throw new ArgumentException(
                    "CHW destination must match the declared channel layout.",
                    nameof(destination));
            }

            for (int row = 0; row < height; row++)
            {
                int flippedRow = height - 1 - row;
                for (int column = 0; column < width; column++)
                {
                    int sourceIndex = flippedRow * width + column;
                    int targetIndex = row * width + column;
                    Color32 pixel = source[sourceIndex];
                    for (int channel = 0; channel < channelLayout.Count; channel++)
                    {
                        byte value = channelLayout[channel] switch
                        {
                            "channel_0_unused" => pixel.r,
                            "channel_1_traversable" => pixel.g,
                            "channel_2_blocked_or_background" => pixel.b,
                            _ => throw new InvalidDataException(
                                $"Unsupported semantic channel '{channelLayout[channel]}'."),
                        };
                        destination[channel * planeSize + targetIndex] = value / 255f;
                    }
                }
            }
        }

        internal static QuickstartAppliedAction ApplyActionContract(
            QuickstartRawAction rawAction)
        {
            if (!IsFinite(rawAction.Forward) || !IsFinite(rawAction.Turn))
            {
                throw new InvalidDataException(
                    "ONNX policy returned non-finite action values.");
            }

            float forward = Mathf.Clamp01(rawAction.Forward);
            float turn = Mathf.Clamp(rawAction.Turn, -1f, 1f);
            bool violation = rawAction.Forward < 0f ||
                rawAction.Forward > 1f ||
                rawAction.Turn < -1f ||
                rawAction.Turn > 1f;
            return new QuickstartAppliedAction(
                rawAction.Forward,
                rawAction.Turn,
                forward,
                turn,
                violation);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
