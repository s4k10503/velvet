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
        private readonly GameObject _gameObject;
        private readonly PanelSettings _settings;

        public UIDocument Document { get; }

        /// <summary>The RenderTexture the panel's PanelSettings renders into.</summary>
        public RenderTexture TargetTexture { get; }

        /// <summary>The panel's root, ready to V.Mount a VNode tree onto.</summary>
        public VisualElement Root => Document.rootVisualElement;

        public RenderTexturePanelHost(string name, int width, int height, int depth = 32)
        {
            TargetTexture = new RenderTexture(width, height, depth);
            _gameObject = new GameObject(name);
            Document = _gameObject.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.targetTexture = TargetTexture;
            Document.panelSettings = _settings;
        }

        public void Dispose()
        {
            if (_gameObject != null) UnityEngine.Object.Destroy(_gameObject);
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
            RenderTexture.active = renderTexture;

            var texture = new Texture2D(region.width, region.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(region.x, region.y, region.width, region.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previouslyActive;

            var pixels = texture.GetPixels32();
            UnityEngine.Object.Destroy(texture);
            return pixels;
        }
    }
}
