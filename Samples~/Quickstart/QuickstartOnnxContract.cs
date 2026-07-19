#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal readonly struct QuickstartTensorMetadata
    {
        internal QuickstartTensorMetadata(
            string name,
            bool isFloat,
            IReadOnlyList<int> dimensions)
        {
            Name = name;
            IsFloat = isFloat;
            Dimensions = dimensions ?? throw new ArgumentNullException(nameof(dimensions));
        }

        internal string Name { get; }

        internal bool IsFloat { get; }

        internal IReadOnlyList<int> Dimensions { get; }
    }

    internal sealed class QuickstartOnnxContract
    {
        internal const string ImageInputName = "obs_0";
        internal const string NumericInputName = "obs_1";
        internal const int ImageChannels = 3;
        internal const int ImageHeight = 84;
        internal const int ImageWidth = 112;
        internal const int ImageValueCount = ImageChannels * ImageHeight * ImageWidth;
        internal const int NumericValueCount = 2;

        private QuickstartOnnxContract(
            int[] imageDimensions,
            int[] numericDimensions,
            string outputName)
        {
            ImageDimensions = imageDimensions;
            NumericDimensions = numericDimensions;
            OutputName = outputName;
        }

        internal int[] ImageDimensions { get; }

        internal int[] NumericDimensions { get; }

        internal string OutputName { get; }

        internal static QuickstartOnnxContract Validate(
            IReadOnlyList<QuickstartTensorMetadata> inputs,
            IReadOnlyList<QuickstartTensorMetadata> outputs)
        {
            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }

            if (outputs == null)
            {
                throw new ArgumentNullException(nameof(outputs));
            }

            if (inputs.Count != 2)
            {
                throw new InvalidDataException(
                    "ONNX policy must expose exactly obs_0 and obs_1 inputs.");
            }

            int[]? imageDimensions = null;
            int[]? numericDimensions = null;
            foreach (QuickstartTensorMetadata input in inputs)
            {
                if (!input.IsFloat)
                {
                    throw new InvalidDataException(
                        $"ONNX input '{input.Name}' must contain float values.");
                }

                if (string.Equals(input.Name, ImageInputName, StringComparison.Ordinal))
                {
                    imageDimensions = ResolveImageDimensions(input.Dimensions);
                }
                else if (string.Equals(
                    input.Name,
                    NumericInputName,
                    StringComparison.Ordinal))
                {
                    numericDimensions = ResolveNumericDimensions(input.Dimensions);
                }
                else
                {
                    throw new InvalidDataException(
                        $"Unsupported ONNX input '{input.Name}'. Expected only obs_0 and obs_1.");
                }
            }

            if (imageDimensions == null || numericDimensions == null)
            {
                throw new InvalidDataException(
                    "ONNX policy must expose both obs_0 and obs_1 inputs.");
            }

            foreach (QuickstartTensorMetadata output in outputs)
            {
                if (output.IsFloat && ContainsAtLeastTwoValues(output.Dimensions))
                {
                    return new QuickstartOnnxContract(
                        imageDimensions,
                        numericDimensions,
                        output.Name);
                }
            }

            throw new InvalidDataException(
                "ONNX policy must expose a float output containing at least two actions.");
        }

        private static int[] ResolveImageDimensions(IReadOnlyList<int> dimensions)
        {
            if (MatchesDimensions(
                dimensions,
                new[] { ImageChannels, ImageHeight, ImageWidth }))
            {
                return new[] { ImageChannels, ImageHeight, ImageWidth };
            }

            if (MatchesOptionalBatchDimensions(
                dimensions,
                new[] { ImageChannels, ImageHeight, ImageWidth }))
            {
                return new[] { 1, ImageChannels, ImageHeight, ImageWidth };
            }

            throw new InvalidDataException(
                "ONNX input 'obs_0' must have shape [3,84,112] or [batch,3,84,112].");
        }

        private static int[] ResolveNumericDimensions(IReadOnlyList<int> dimensions)
        {
            if (MatchesDimensions(dimensions, new[] { NumericValueCount }))
            {
                return new[] { NumericValueCount };
            }

            if (MatchesOptionalBatchDimensions(
                dimensions,
                new[] { NumericValueCount }))
            {
                return new[] { 1, NumericValueCount };
            }

            throw new InvalidDataException(
                "ONNX input 'obs_1' must have shape [2] or [batch,2].");
        }

        private static bool MatchesDimensions(
            IReadOnlyList<int> actual,
            IReadOnlyList<int> expected)
        {
            if (actual.Count != expected.Count)
            {
                return false;
            }

            for (int index = 0; index < expected.Count; index++)
            {
                if (actual[index] != expected[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesOptionalBatchDimensions(
            IReadOnlyList<int> actual,
            IReadOnlyList<int> expectedWithoutBatch)
        {
            if (actual.Count != expectedWithoutBatch.Count + 1 || actual[0] > 1)
            {
                return false;
            }

            for (int index = 0; index < expectedWithoutBatch.Count; index++)
            {
                if (actual[index + 1] != expectedWithoutBatch[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsAtLeastTwoValues(IReadOnlyList<int> dimensions)
        {
            if (dimensions.Count == 0)
            {
                return false;
            }

            long knownProduct = 1;
            foreach (int dimension in dimensions)
            {
                if (dimension > 0)
                {
                    knownProduct *= dimension;
                    if (knownProduct >= 2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
