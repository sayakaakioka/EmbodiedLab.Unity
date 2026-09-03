#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity.Internal;

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
        private static readonly string[] SupportedImageChannels =
        {
            "channel_0_unused",
            "channel_1_traversable",
            "channel_2_blocked_or_background",
        };

        private QuickstartOnnxContract(
            int imageHeight,
            int imageWidth,
            ModelInput imageInput,
            int[] imageDimensions,
            ModelInput numericInput,
            int[] numericDimensions,
            Values[] numericValues,
            string outputName,
            int actionValueCount,
            int forwardActionIndex,
            int turnActionIndex)
        {
            ImageHeight = imageHeight;
            ImageWidth = imageWidth;
            ImageInputName = imageInput.Name;
            ImageDimensions = imageDimensions;
            ImageChannelLayout = imageInput.Layout.ToArray();
            NumericInputName = numericInput.Name;
            NumericDimensions = numericDimensions;
            NumericValues = numericValues;
            OutputName = outputName;
            ActionValueCount = actionValueCount;
            ForwardActionIndex = forwardActionIndex;
            TurnActionIndex = turnActionIndex;
        }

        internal string ImageInputName { get; }

        internal int[] ImageDimensions { get; }

        internal IReadOnlyList<string> ImageChannelLayout { get; }

        internal int ImageChannels => ImageChannelLayout.Count;

        internal int ImageHeight { get; }

        internal int ImageWidth { get; }

        internal int ImageValueCount => checked(ImageChannels * ImageHeight * ImageWidth);

        internal string NumericInputName { get; }

        internal int[] NumericDimensions { get; }

        internal IReadOnlyList<Values> NumericValues { get; }

        internal int NumericValueCount => NumericValues.Count;

        internal string OutputName { get; }

        internal int ActionValueCount { get; }

        internal int ForwardActionIndex { get; }

        internal int TurnActionIndex { get; }

        internal static QuickstartOnnxContract Validate(
            ScenarioBundle scenario,
            OnnxModelArtifactLocation model,
            IReadOnlyList<QuickstartTensorMetadata> inputs,
            IReadOnlyList<QuickstartTensorMetadata> outputs)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }

            if (outputs == null)
            {
                throw new ArgumentNullException(nameof(outputs));
            }

            if (model.Format != OnnxModelArtifactLocationFormat.Onnx ||
                model.Target != OnnxModelArtifactLocationTarget.OnnxRuntime ||
                model.OpsetVersion != OnnxModelArtifactLocationOpsetVersion._18)
            {
                throw new InvalidDataException(
                    "The downloaded model metadata is not the supported ONNX contract.");
            }

            ForwardCameraSensor cameraSensor = RequireSingleSensor<ForwardCameraSensor>(scenario);
            GoalVectorSensor goalVectorSensor = RequireSingleSensor<GoalVectorSensor>(scenario);
            if (cameraSensor.Width <= 0 || cameraSensor.Height <= 0)
            {
                throw new InvalidDataException(
                    "Scenario camera dimensions must be positive.");
            }

            if (model.Inputs == null || model.Inputs.Count != 2 || inputs.Count != 2)
            {
                throw new InvalidDataException(
                    "ONNX policy must expose the two observations declared by the Scenario.");
            }

            ModelInput imageInput = RequireModelInput(model.Inputs, cameraSensor.ObservationName);
            ModelInput numericInput = RequireModelInput(
                model.Inputs,
                goalVectorSensor.ObservationName);
            string[] numericLayout = goalVectorSensor.Values
                .Select(ToWireValue)
                .ToArray();
            RequireExactLayout(
                imageInput.Layout,
                SupportedImageChannels,
                "semantic camera input");
            RequireExactLayout(
                numericInput.Layout,
                numericLayout,
                "goal-vector input");

            QuickstartTensorMetadata imageMetadata = RequireTensor(
                inputs,
                imageInput.Name,
                "input");
            QuickstartTensorMetadata numericMetadata = RequireTensor(
                inputs,
                numericInput.Name,
                "input");
            int[] imageDimensions = ValidateInput(
                imageInput,
                imageMetadata,
                new[] { imageInput.Layout.Count, cameraSensor.Height, cameraSensor.Width });
            int[] numericDimensions = ValidateInput(
                numericInput,
                numericMetadata,
                new[] { goalVectorSensor.Values.Count });

            ModelOutput output = model.Output ?? throw new InvalidDataException(
                "ONNX model output metadata is required.");
            ContractSemanticValidator.ValidateSupportedModelOutput(
                output,
                "ONNX model");
            if (outputs.Count != 1)
            {
                throw new InvalidDataException(
                    "ONNX policy must expose exactly one declared action output.");
            }

            QuickstartTensorMetadata outputMetadata = RequireTensor(
                outputs,
                output.Name,
                "output");
            if (!outputMetadata.IsFloat ||
                !HasExactOutputShape(
                    outputMetadata.Dimensions,
                    output.Layout.Count))
            {
                throw new InvalidDataException(
                    $"ONNX output '{output.Name}' does not match its declared action layout.");
            }

            string[] actionLayout = scenario.Robot?.ActionSpace?.Layout?
                .Select(ToWireValue)
                .ToArray() ?? throw new InvalidDataException(
                    "Scenario action layout is required.");
            RequireExactLayout(output.Layout, actionLayout, "model action output");
            int forwardIndex = Array.IndexOf(actionLayout, "forward");
            int turnIndex = Array.IndexOf(actionLayout, "turn");
            if (forwardIndex < 0 || turnIndex < 0 || actionLayout.Length != 2)
            {
                throw new InvalidDataException(
                    "Quickstart requires forward and turn actions in the Scenario layout.");
            }

            return new QuickstartOnnxContract(
                cameraSensor.Height,
                cameraSensor.Width,
                imageInput,
                imageDimensions,
                numericInput,
                numericDimensions,
                goalVectorSensor.Values.ToArray(),
                output.Name,
                actionLayout.Length,
                forwardIndex,
                turnIndex);
        }

        private static T RequireSingleSensor<T>(ScenarioBundle scenario)
            where T : SensorSpec
        {
            T[] matches = scenario.Sensors?.OfType<T>().ToArray() ?? Array.Empty<T>();
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"Scenario must declare exactly one {typeof(T).Name}.");
            }

            return matches[0];
        }

        private static ModelInput RequireModelInput(
            ICollection<ModelInput> inputs,
            string observationName)
        {
            if (string.IsNullOrWhiteSpace(observationName))
            {
                throw new InvalidDataException("Scenario observation_name is required.");
            }

            ModelInput[] matches = inputs
                .Where(input => input != null && string.Equals(
                    input.Name,
                    observationName,
                    StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"Model metadata must declare Scenario observation '{observationName}'.");
            }

            return matches[0];
        }

        private static QuickstartTensorMetadata RequireTensor(
            IReadOnlyList<QuickstartTensorMetadata> tensors,
            string name,
            string kind)
        {
            QuickstartTensorMetadata[] matches = tensors
                .Where(tensor => string.Equals(tensor.Name, name, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"ONNX session must expose declared {kind} '{name}'.");
            }

            return matches[0];
        }

        private static int[] ValidateInput(
            ModelInput declared,
            QuickstartTensorMetadata actual,
            IReadOnlyList<int> expectedPayload)
        {
            if (!string.Equals(declared.Dtype, "float32", StringComparison.Ordinal) ||
                !actual.IsFloat ||
                declared.Shape == null)
            {
                throw new InvalidDataException(
                    $"ONNX input '{declared.Name}' must contain float32 values.");
            }

            int[] declaredShape = declared.Shape.ToArray();
            bool hasBatch = declaredShape.Length == expectedPayload.Count + 1;
            if ((!hasBatch && declaredShape.Length != expectedPayload.Count) ||
                actual.Dimensions.Count != declaredShape.Length)
            {
                throw new InvalidDataException(
                    $"ONNX input '{declared.Name}' rank does not match its declared shape.");
            }

            int payloadOffset = hasBatch ? 1 : 0;
            if (hasBatch &&
                (!IsSupportedBatchDimension(declaredShape[0]) ||
                    !IsSupportedBatchDimension(actual.Dimensions[0])))
            {
                throw new InvalidDataException(
                    $"ONNX input '{declared.Name}' batch dimension must be dynamic or one.");
            }

            for (int index = 0; index < expectedPayload.Count; index++)
            {
                int declaredDimension = declaredShape[index + payloadOffset];
                if (declaredDimension != expectedPayload[index] ||
                    actual.Dimensions[index + payloadOffset] != declaredDimension)
                {
                    throw new InvalidDataException(
                        $"ONNX input '{declared.Name}' shape does not match Scenario/model metadata.");
                }
            }

            if (hasBatch)
            {
                declaredShape[0] = 1;
            }

            return declaredShape;
        }

        private static void RequireExactLayout(
            IEnumerable<string> actual,
            IEnumerable<string> expected,
            string description)
        {
            if (actual == null || !actual.SequenceEqual(expected, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"The {description} does not match the Scenario/model contract.");
            }
        }

        private static string ToWireValue(Values value) => value switch
        {
            Values.GoalAngleDegrees => "goal_angle_degrees",
            Values.GoalDistanceMeters => "goal_distance_meters",
            _ => throw new InvalidDataException(
                $"Unsupported goal-vector value '{value}'."),
        };

        private static string ToWireValue(Layout value) => value switch
        {
            Layout.Forward => "forward",
            Layout.Turn => "turn",
            _ => throw new InvalidDataException($"Unsupported action '{value}'."),
        };

        private static bool HasExactOutputShape(
            IReadOnlyList<int> dimensions,
            int requiredValues)
        {
            if (requiredValues <= 0)
            {
                return false;
            }

            return dimensions.Count == 1 && dimensions[0] == requiredValues ||
                dimensions.Count == 2 &&
                IsSupportedBatchDimension(dimensions[0]) &&
                dimensions[1] == requiredValues;
        }

        private static bool IsSupportedBatchDimension(int dimension)
        {
            return dimension == -1 || dimension == 1;
        }
    }
}
