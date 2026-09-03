#nullable enable

using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal sealed class QuickstartSemanticCamera : IDisposable
    {
        private readonly Camera camera;
        private readonly QuickstartOnnxContract contract;
        private RenderTexture? renderTexture;
        private Texture2D? readback;

        internal QuickstartSemanticCamera(
            Camera camera,
            QuickstartOnnxContract contract)
        {
            this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
            this.contract = contract ?? throw new ArgumentNullException(nameof(contract));
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                throw new InvalidOperationException(
                    "Inference requires a graphics device for semantic camera observations.");
            }

            try
            {
                renderTexture = new RenderTexture(
                    contract.ImageWidth,
                    contract.ImageHeight,
                    16,
                    RenderTextureFormat.ARGB32)
                {
                    name = "EmbodiedLab Quickstart Semantic Observation",
                };
                if (!renderTexture.Create())
                {
                    throw new InvalidOperationException(
                        "Semantic camera render texture could not be created.");
                }

                readback = new Texture2D(
                    contract.ImageWidth,
                    contract.ImageHeight,
                    TextureFormat.RGB24,
                    mipChain: false)
                {
                    name = "EmbodiedLab Quickstart Semantic Readback",
                };
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Capture(float[] destination)
        {
            RenderTexture activeRenderTexture = renderTexture ??
                throw new ObjectDisposedException(nameof(QuickstartSemanticCamera));
            Texture2D activeReadback = readback ??
                throw new ObjectDisposedException(nameof(QuickstartSemanticCamera));
            if (destination == null ||
                destination.Length != contract.ImageValueCount)
            {
                throw new InvalidDataException(
                    "Semantic image observation has an invalid destination size.");
            }

            RenderTexture? previousTarget = camera.targetTexture;
            RenderTexture? previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = activeRenderTexture;
                camera.Render();
                RenderTexture.active = activeRenderTexture;
                activeReadback.ReadPixels(
                    new Rect(
                        0,
                        0,
                        contract.ImageWidth,
                        contract.ImageHeight),
                    0,
                    0);
                activeReadback.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                QuickstartInferenceMath.ConvertRgbToVerticallyFlippedChw(
                    activeReadback.GetPixels32(),
                    contract.ImageWidth,
                    contract.ImageHeight,
                    contract.ImageChannelLayout,
                    destination);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Semantic camera observation capture failed.",
                    exception);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
            }
        }

        public void Dispose()
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                DestroyObject(renderTexture);
                renderTexture = null;
            }

            if (readback != null)
            {
                DestroyObject(readback);
                readback = null;
            }
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
}
