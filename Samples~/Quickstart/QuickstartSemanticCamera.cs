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
        private RenderTexture? renderTexture;
        private Texture2D? readback;

        internal QuickstartSemanticCamera(Camera camera)
        {
            this.camera = camera ?? throw new ArgumentNullException(nameof(camera));
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                throw new InvalidOperationException(
                    "Inference requires a graphics device for semantic camera observations.");
            }

            renderTexture = new RenderTexture(
                QuickstartOnnxContract.ImageWidth,
                QuickstartOnnxContract.ImageHeight,
                16,
                RenderTextureFormat.ARGB32)
            {
                name = "EmbodiedLab Quickstart Semantic Observation",
            };
            if (!renderTexture.Create())
            {
                Dispose();
                throw new InvalidOperationException(
                    "Semantic camera render texture could not be created.");
            }

            readback = new Texture2D(
                QuickstartOnnxContract.ImageWidth,
                QuickstartOnnxContract.ImageHeight,
                TextureFormat.RGB24,
                mipChain: false)
            {
                name = "EmbodiedLab Quickstart Semantic Readback",
            };
        }

        internal string Capture(float[] destination)
        {
            RenderTexture activeRenderTexture = renderTexture ??
                throw new ObjectDisposedException(nameof(QuickstartSemanticCamera));
            Texture2D activeReadback = readback ??
                throw new ObjectDisposedException(nameof(QuickstartSemanticCamera));
            if (destination == null ||
                destination.Length != QuickstartOnnxContract.ImageValueCount)
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
                        QuickstartOnnxContract.ImageWidth,
                        QuickstartOnnxContract.ImageHeight),
                    0,
                    0);
                activeReadback.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                QuickstartInferenceMath.ConvertRgbToVerticallyFlippedChw(
                    activeReadback.GetPixels32(),
                    QuickstartOnnxContract.ImageWidth,
                    QuickstartOnnxContract.ImageHeight,
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

            int planeSize = QuickstartOnnxContract.ImageHeight *
                QuickstartOnnxContract.ImageWidth;
            double red = 0d;
            double green = 0d;
            double blue = 0d;
            for (int index = 0; index < planeSize; index++)
            {
                red += destination[index];
                green += destination[planeSize + index];
                blue += destination[planeSize * 2 + index];
            }

            return $"image mean r={red / planeSize:0.000} " +
                $"g={green / planeSize:0.000} b={blue / planeSize:0.000}";
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
