#if UNITY_EDITOR
#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// A runtime panel that renders into a texture, built from a copy of a <see cref="PanelSettings"/> with
    /// Velvet's utility stylesheet attached to its root. The preview window shows a story through one, so a
    /// harness that mounts a story without an explicit size on one built from the same settings at the window's
    /// screen size reads the pixels the window renders for it.
    /// </summary>
    /// <remarks>
    /// The copy targets <see cref="Texture"/>, which is cleared to transparent below everything drawn into it,
    /// a portal's layers included, and maps every screen position off the panel; input is sent to it as events to <see cref="Root"/>'s panel. A world-space
    /// settings asset is copied as a screen panel at a scale of one.
    /// </remarks>
    public sealed class VelvetPreviewPanel : IDisposable
    {
        private const int TextureDepth = 24;

        // Below any sorting order a layer host takes, which sits a few hundred from its declaring panel's.
        private const float ClearSortingOrder = -1e6f;

        private readonly GameObject _host;
        private readonly PanelSettings _settings;
        private readonly GameObject _clearHost;
        private readonly PanelSettings _clearSettings;
        private readonly PanelScaleMode _ownScaleMode;
        private readonly float _ownScale;
        private bool _disposed;

        /// <summary>
        /// Builds the panel at <paramref name="width"/> by <paramref name="height"/> pixels. With
        /// <paramref name="settings"/> <c>null</c> it is a constant-pixel-size panel at a scale of one with an
        /// empty theme. <paramref name="settings"/> itself is never modified.
        /// </summary>
        public VelvetPreviewPanel(PanelSettings? settings, int width, int height)
        {
            _settings = settings != null
                ? UnityEngine.Object.Instantiate(settings)
                : ScriptableObject.CreateInstance<PanelSettings>();
            _settings.hideFlags = HideFlags.HideAndDontSave;
            if (_settings.themeStyleSheet == null) _settings.themeStyleSheet = PanelHostFactory.SharedEmptyTheme;
            if (settings == null || _settings.renderMode == PanelRenderMode.WorldSpace)
            {
                _settings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
                _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
                _settings.scale = 1f;
            }

            _ownScaleMode = _settings.scaleMode;
            _ownScale = _settings.scale;
            _settings.SetScreenToPanelSpaceFunction(OffPanel);
            Texture = CreateTexture(width, height);
            _settings.targetTexture = Texture;

            // The clear is a panel of its own, sorted below everything else drawing into the texture: a
            // portal's Background layer host draws there before this panel does, so a clear on this
            // panel would erase it.
            _clearSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _clearSettings.hideFlags = HideFlags.HideAndDontSave;
            _clearSettings.themeStyleSheet = PanelHostFactory.SharedEmptyTheme;
            _clearSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _clearSettings.sortingOrder = ClearSortingOrder;
            _clearSettings.clearColor = true;
            _clearSettings.colorClearValue = Color.clear;
            _clearSettings.SetScreenToPanelSpaceFunction(OffPanel);
            _clearSettings.targetTexture = Texture;
            _clearHost = CreateDocument(_clearSettings).gameObject;

            var document = CreateDocument(_settings);
            _host = document.gameObject;
            Root = document.rootVisualElement;
            VelvetStyleUtilities.AttachTo(Root);
        }

        private static UIDocument CreateDocument(PanelSettings settings)
        {
            // Hidden in the active scene the way PanelHostFactory's hosts are, so a scene change destroys it;
            // IsAlive reports that.
            var host = new GameObject(nameof(VelvetPreviewPanel));
            VelvetObjectUtil.HideFrameworkSceneObject(host);
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            return document;
        }

        /// <summary>The panel's root, to mount a story onto.</summary>
        public VisualElement Root { get; }

        /// <summary>The texture the panel renders into. <see cref="Resize"/> resizes it in place.</summary>
        public RenderTexture Texture { get; }

        /// <summary>
        /// Resizes <see cref="Texture"/> to <paramref name="width"/> by <paramref name="height"/>, keeping the
        /// panel and whatever is mounted on it. Each size is clamped to at least one pixel.
        /// </summary>
        public void Resize(int width, int height)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VelvetPreviewPanel));
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (Texture.width == width && Texture.height == height) return;

            // In place rather than replaced: a portal's layer host copied this texture as its target, and
            // keeps drawing into it.
            Texture.Release();
            Texture.width = width;
            Texture.height = height;
            Texture.Create();
        }

        // A scale the preview window resolved for a screen other than this texture, held at that value
        // whatever the texture's size; null restores the copied settings' own scaling.
        internal void PinScale(float? scale)
        {
            var mode = scale.HasValue ? PanelScaleMode.ConstantPixelSize : _ownScaleMode;
            var value = scale ?? _ownScale;
            if (_settings.scaleMode != mode) _settings.scaleMode = mode;
            if (_settings.scale != value) _settings.scale = value;
        }

        // False once a scene change has destroyed the panel's GameObjects.
        internal bool IsAlive => !_disposed && _host != null && _clearHost != null;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            VelvetObjectUtil.Destroy(_host);
            VelvetObjectUtil.Destroy(_settings);
            VelvetObjectUtil.Destroy(_clearHost);
            VelvetObjectUtil.Destroy(_clearSettings);
            Texture.Release();
            VelvetObjectUtil.Destroy(Texture);
        }

        private static RenderTexture CreateTexture(int width, int height) =>
            new(Mathf.Max(1, width), Mathf.Max(1, height), TextureDepth)
            {
                name = nameof(VelvetPreviewPanel),
                hideFlags = HideFlags.HideAndDontSave,
            };

        private static Vector2 OffPanel(Vector2 screen) => new(float.NaN, float.NaN);
    }
}
#endif
