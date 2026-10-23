using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.VFX;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Guards tests that need a device able to draw a Visual Effect Graph. A software GL rasterizer can simulate a
    /// graph (particles alive) and still draw nothing through any camera; such a test is Ignored with the measured
    /// reason, and only when a plain default camera, with none of Velvet in the path, draws nothing from the graph
    /// while particles are alive. Where the control draws, or never gets a live particle, the test runs.
    /// </summary>
    public static class VfxGraphCapability
    {
        private const float WaitSeconds = 10f;

        /// <summary>
        /// Call as the first yield of a test that needs the graph drawn: <c>yield return
        /// VfxGraphCapability.IgnoreIfCannotDraw(asset);</c>. A null asset is left to the test to fail on.
        /// </summary>
        public static IEnumerator IgnoreIfCannotDraw(VisualEffectAsset asset)
        {
            if (asset == null)
            {
                yield break;
            }
            var go = new GameObject("velvet-vfx-capability");
            var texture = new RenderTexture(64, 64, 24);
            var alive = 0;
            var alpha = 0;
            try
            {
                var effect = go.AddComponent<VisualEffect>();
                effect.visualEffectAsset = asset;
                effect.Play();
                var cameraGo = new GameObject("velvet-vfx-capability-camera");
                cameraGo.transform.SetParent(go.transform, false);
                cameraGo.transform.localPosition = new Vector3(0f, 0f, -100f);
                var camera = cameraGo.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 20f;
                camera.farClipPlane = 200f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.targetTexture = texture;
                var deadline = Time.realtimeSinceStartup + WaitSeconds;
                while (alpha == 0 && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    alive = effect.aliveParticleCount;
                    if (alive > 0)
                    {
                        camera.Render();
                        alpha = AlphaTexels(texture);
                    }
                }
            }
            finally
            {
                texture.Release();
                Object.Destroy(texture);
                Object.Destroy(go);
            }
            if (alive > 0 && alpha == 0)
            {
                Assert.Ignore(
                    $"A plain camera draws nothing from the graph with {alive} particles alive on "
                    + $"{SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceVersion}): alpha texels {alpha}.");
            }
        }

        private static int AlphaTexels(RenderTexture texture)
        {
            var pixels = RenderTexturePixelReader.ReadPixels(texture, new RectInt(0, 0, texture.width, texture.height));
            return System.Array.FindAll(pixels, pixel => pixel.a > 0).Length;
        }
    }
}
