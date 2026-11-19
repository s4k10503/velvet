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
        /// Call once the graph is playing, before the test reads its pixels: <c>yield return
        /// VfxGraphCapability.IgnoreIfCannotDraw(effect, measured => reading = measured);</c>. The effect is the
        /// mounted clone, which the caller has played. <paramref name="report"/> receives the
        /// measurement whether or not the test is ignored.
        /// </summary>
        public static IEnumerator IgnoreIfCannotDraw(VisualEffect effect, System.Action<string> report)
        {
            var go = new GameObject("velvet-vfx-capability");
            var texture = new RenderTexture(64, 64, 24);
            var alive = 0;
            var alpha = 0;
            var start = Time.realtimeSinceStartup;
            var renderers = effect.GetComponentsInChildren<Renderer>(true);
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 20f;
                camera.farClipPlane = 200f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.targetTexture = texture;
                go.transform.SetPositionAndRotation(
                    effect.transform.position - effect.transform.forward * 100f, effect.transform.rotation);
                while (alpha == 0 && Time.realtimeSinceStartup < start + WaitSeconds)
                {
                    yield return null;
                    alive = effect.aliveParticleCount;
                    if (alive > 0)
                    {
                        alpha = RenderAlpha(camera, texture, renderers);
                    }
                }
            }
            finally
            {
                texture.Release();
                Object.Destroy(texture);
                Object.Destroy(go);
            }
            var reading = $"capability alive={alive} alpha={alpha} waited={Time.realtimeSinceStartup - start:F1}s "
                + $"device={SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceVersion}";
            report(reading);
            if (alive > 0 && alpha == 0)
            {
                Assert.Ignore($"A plain camera draws nothing from the graph: {reading}.");
            }
        }

        // V.Particles keeps its clone's renderers off between its own renders, so they are switched on for this one.
        private static int RenderAlpha(Camera camera, RenderTexture texture, Renderer[] renderers)
        {
            try
            {
                foreach (var renderer in renderers)
                {
                    renderer.forceRenderingOff = false;
                }
                camera.Render();
                return AlphaTexels(texture);
            }
            finally
            {
                foreach (var renderer in renderers)
                {
                    renderer.forceRenderingOff = true;
                }
            }
        }

        private static int AlphaTexels(RenderTexture texture)
        {
            var pixels = RenderTexturePixelReader.ReadPixels(texture, new RectInt(0, 0, texture.width, texture.height));
            return System.Array.FindAll(pixels, pixel => pixel.a > 0).Length;
        }
    }
}
