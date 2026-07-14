using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Builds a UIDocument-backed runtime panel whose PanelSettings targets a RenderTexture, so a
    /// PlayMode fixture can read back what the panel actually drew. Previously hand-rolled per fixture
    /// across the SceneView and Particles playback specs: a GameObject + UIDocument + PanelSettings +
    /// RenderTexture, wired up and torn down the same way each time.
    /// Test-only. Must not be used from production code.
    /// </summary>
    public sealed class RenderTexturePanelHost : IDisposable
    {
        private readonly UIDocument _document;
        private readonly PanelSettings _settings;

        /// <summary>The RenderTexture the panel's PanelSettings renders into.</summary>
        public RenderTexture TargetTexture { get; }

        /// <summary>The panel's root, ready to V.Mount a VNode tree onto.</summary>
        public VisualElement Root => _document.rootVisualElement;

        public RenderTexturePanelHost(string name, int width, int height, int depth = 32)
        {
            TargetTexture = new RenderTexture(width, height, depth);
            _document = new GameObject(name).AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.targetTexture = TargetTexture;
            _document.panelSettings = _settings;
        }

        public void Dispose()
        {
            if (_document != null) UnityEngine.Object.Destroy(_document.gameObject);
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
            if (TargetTexture != null)
            {
                TargetTexture.Release();
                UnityEngine.Object.Destroy(TargetTexture);
            }
        }
    }

    /// <summary>
    /// Reads pixels back from a RenderTexture via a throwaway Texture2D, saving and restoring
    /// RenderTexture.active around the read. Previously hand-rolled per fixture across the
    /// SceneView/Particles/Portal playback specs.
    /// Test-only. Must not be used from production code.
    /// </summary>
    public static class RenderTexturePixelReader
    {
        public static Color32[] ReadPixels(RenderTexture renderTexture, RectInt region)
        {
            var previouslyActive = RenderTexture.active;
            var texture = new Texture2D(region.width, region.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(region.x, region.y, region.width, region.height), 0, 0);
                texture.Apply();
                return texture.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previouslyActive;
                UnityEngine.Object.Destroy(texture);
            }
        }

        /// <summary>
        /// Whether a sampled pixel reads as the saturated red used by the SceneView/Particles playback
        /// specs' test materials — a fixed threshold wide enough to absorb sRGB/anti-aliasing drift.
        /// </summary>
        public static bool IsRedPixel(Color32 pixel) => pixel.r > 140 && pixel.g < 90 && pixel.b < 90;
    }
}
