#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using EmbodiedLab.Contracts;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
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

            if (!IsFinite(robotPosition.x) ||
                !IsFinite(robotPosition.z) ||
                !IsFinite(robotYawDegrees) ||
                !IsFinite(goalPosition.x) ||
                !IsFinite(goalPosition.z))
            {
                throw new InvalidDataException(
                    "Robot and goal observation values must be finite.");
            }

            float deltaX = goalPosition.x - robotPosition.x;
            float deltaZ = goalPosition.z - robotPosition.z;
            float targetDegrees = Mathf.Atan2(deltaX, deltaZ) * Mathf.Rad2Deg;
            float goalAngleDegrees = Mathf.Repeat(
                targetDegrees - robotYawDegrees + 180f,
                360f) - 180f;
            float goalDistanceMeters = Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            if (!IsFinite(goalAngleDegrees) || !IsFinite(goalDistanceMeters))
            {
                throw new InvalidDataException(
                    "Computed goal observation values must be finite.");
            }

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
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(width),
                    "RGB width must be positive.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(height),
                    "RGB height must be positive.");
            }

            int planeSize;
            try
            {
                planeSize = checked(width * height);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(width),
                    "RGB dimensions are too large.");
            }

            if (source == null || source.Length != planeSize)
            {
                throw new ArgumentException(
                    "RGB source size does not match its dimensions.",
                    nameof(source));
            }

            if (channelLayout == null)
            {
                throw new ArgumentNullException(nameof(channelLayout));
            }

            int requiredValues;
            try
            {
                requiredValues = checked(planeSize * channelLayout.Count);
            }
            catch (OverflowException)
            {
                throw new ArgumentException(
                    "CHW channel layout is too large.",
                    nameof(channelLayout));
            }

            if (destination == null ||
                destination.Length != requiredValues)
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

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
