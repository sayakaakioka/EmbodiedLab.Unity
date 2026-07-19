#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal sealed class QuickstartOnnxPolicy : IDisposable
    {
        private InferenceSession? session;
        private readonly QuickstartOnnxContract contract;

        internal QuickstartOnnxPolicy(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new ArgumentException("ONNX model path is required.", nameof(modelPath));
            }

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("ONNX model was not found.", modelPath);
            }

            try
            {
                using var options = new SessionOptions
                {
                    EnableCpuMemArena = true,
                    EnableMemoryPattern = true,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    IntraOpNumThreads = 1,
                    InterOpNumThreads = 1,
                };
                session = new InferenceSession(modelPath, options);
                contract = QuickstartOnnxContract.Validate(
                    Describe(session.InputMetadata),
                    Describe(session.OutputMetadata));
            }
            catch
            {
                session?.Dispose();
                session = null;
                throw;
            }
        }

        internal QuickstartRawAction Run(
            float[] imageObservation,
            float[] numericObservation)
        {
            InferenceSession activeSession = session ??
                throw new ObjectDisposedException(nameof(QuickstartOnnxPolicy));
            if (imageObservation == null ||
                imageObservation.Length != QuickstartOnnxContract.ImageValueCount)
            {
                throw new InvalidDataException(
                    $"Image observation must contain {QuickstartOnnxContract.ImageValueCount} values.");
            }

            if (numericObservation == null ||
                numericObservation.Length != QuickstartOnnxContract.NumericValueCount)
            {
                throw new InvalidDataException(
                    $"Numeric observation must contain {QuickstartOnnxContract.NumericValueCount} values.");
            }

            var imageTensor = new DenseTensor<float>(
                imageObservation,
                contract.ImageDimensions);
            var numericTensor = new DenseTensor<float>(
                numericObservation,
                contract.NumericDimensions);
            var inputs = new[]
            {
                NamedOnnxValue.CreateFromTensor(
                    QuickstartOnnxContract.ImageInputName,
                    imageTensor),
                NamedOnnxValue.CreateFromTensor(
                    QuickstartOnnxContract.NumericInputName,
                    numericTensor),
            };

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
                activeSession.Run(inputs);
            foreach (DisposableNamedOnnxValue result in results)
            {
                if (!string.Equals(result.Name, contract.OutputName, StringComparison.Ordinal))
                {
                    continue;
                }

                Tensor<float> actions = result.AsTensor<float>();
                if (actions.Length < 2)
                {
                    break;
                }

                float forward = actions.GetValue(0);
                float turn = actions.GetValue(1);
                if (!IsFinite(forward) || !IsFinite(turn))
                {
                    throw new InvalidDataException(
                        "ONNX policy returned non-finite action values.");
                }

                return new QuickstartRawAction(forward, turn);
            }

            throw new InvalidDataException(
                $"ONNX output '{contract.OutputName}' did not contain two float actions.");
        }

        public void Dispose()
        {
            session?.Dispose();
            session = null;
        }

        private static List<QuickstartTensorMetadata> Describe(
            IReadOnlyDictionary<string, NodeMetadata> metadata)
        {
            var result = new List<QuickstartTensorMetadata>(metadata.Count);
            foreach (KeyValuePair<string, NodeMetadata> entry in metadata)
            {
                result.Add(
                    new QuickstartTensorMetadata(
                        entry.Key,
                        entry.Value.ElementDataType == TensorElementType.Float,
                        entry.Value.Dimensions));
            }

            return result;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
