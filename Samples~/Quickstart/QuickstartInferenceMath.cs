#nullable enable

using System;
using System.IO;
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
            float[] destination)
        {
            if (destination == null ||
                destination.Length != QuickstartOnnxContract.NumericValueCount)
            {
                throw new ArgumentException(
                    "Numeric observation destination must contain exactly two values.",
                    nameof(destination));
            }

            float deltaX = goalPosition.x - robotPosition.x;
            float deltaZ = goalPosition.z - robotPosition.z;
            float targetDegrees = Mathf.Atan2(deltaX, deltaZ) * Mathf.Rad2Deg;
            destination[0] = Mathf.Repeat(
                targetDegrees - robotYawDegrees + 180f,
                360f) - 180f;
            destination[1] = Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        internal static void ConvertRgbToVerticallyFlippedChw(
            Color32[] source,
            int width,
            int height,
            float[] destination)
        {
            if (source == null || source.Length != width * height)
            {
                throw new ArgumentException(
                    "RGB source size does not match its dimensions.",
                    nameof(source));
            }

            int planeSize = width * height;
            if (destination == null || destination.Length != planeSize * 3)
            {
                throw new ArgumentException(
                    "CHW destination must contain three complete color planes.",
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
                    destination[targetIndex] = pixel.r / 255f;
                    destination[planeSize + targetIndex] = pixel.g / 255f;
                    destination[planeSize * 2 + targetIndex] = pixel.b / 255f;
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
