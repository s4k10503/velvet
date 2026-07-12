#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one SceneView element, keyed in
    // ReconcilerContext.SceneViewBindings by the element itself. Holds the current settings (camera +
    // resolution scale), the framework-owned RenderTexture the camera renders into, the registered
    // geometry callback (so it can be unregistered on detach), and the effective pixel size the
    // texture was created at (so a geometry change that does not alter the pixel size skips the
    // re-create).
    internal sealed class SceneViewBinding
    {
        public SceneViewSettings Settings;
        public RenderTexture? Texture;
        public EventCallback<GeometryChangedEvent>? OnGeometryChanged;
        // Effective pixel size (layout x ResolutionScale, rounded) the current Texture was created at.
        public (int W, int H) SizeKey;

        public SceneViewBinding(SceneViewSettings settings)
        {
            Settings = settings;
        }
    }

    /// <summary>
    /// Drives a <see cref="SceneViewElement"/>'s camera-output display: a framework-owned
    /// RenderTexture created at the element's laid-out pixel size (times the resolution scale),
    /// assigned to <c>camera.targetTexture</c>, and shown through the element's background image. The
    /// element samples the LIVE texture — the camera keeps rendering into it and UI Toolkit's command
    /// list samples it at draw time, so new frames appear with no Velvet re-render and no pixel copy.
    /// </summary>
    internal static class SceneViewDriver
    {
        // Wires the geometry-driven texture sync onto the element and returns the binding. The
        // synchronous sync attempt covers a binding attached to an ALREADY laid-out element (a camera
        // arriving through a patch, where no further geometry event may ever fire); at mount the
        // element has no panel yet, so that attempt bails and the geometry callback (fired once layout
        // settles) performs the first real sync.
        public static SceneViewBinding Attach(VisualElement element, SceneViewSettings settings)
        {
            var binding = new SceneViewBinding(settings);
            binding.OnGeometryChanged = _ => SyncTexture(element, binding);
            element.RegisterCallback(binding.OnGeometryChanged);
            SyncTexture(element, binding);
            return binding;
        }

        // Applies changed settings to a live binding: a swapped-out camera is released politely first,
        // then the sync re-derives the rest — the surviving texture is re-targeted by the new camera
        // when the pixel size is unchanged, and a camera removed to null releases both ends (the
        // element stays mounted and inert).
        public static void Update(VisualElement element, SceneViewBinding binding, SceneViewSettings settings)
        {
            var previousCamera = binding.Settings.Camera;
            binding.Settings = settings;
            if (previousCamera != settings.Camera)
            {
                ReleaseCameraTarget(previousCamera, binding.Texture);
            }
            SyncTexture(element, binding);
        }

        // Full teardown: unregisters the geometry callback and releases both ends of the output pair.
        public static void Detach(VisualElement element, SceneViewBinding binding)
        {
            if (binding.OnGeometryChanged != null)
            {
                element.UnregisterCallback(binding.OnGeometryChanged);
            }
            ReleaseOutput(element, binding);
        }

        // (Re)creates or releases the texture to match the CURRENT layout and settings — the one sync
        // point shared by attach, patch, and every geometry change. No camera, or no renderable size
        // (pre-layout NaN, a zero-sized rect, a detached element), releases any existing output;
        // otherwise the texture is re-created only when the effective pixel size actually changed
        // (the size-keyed skip), and an unchanged-size texture is simply re-targeted (where a camera
        // swap lands).
        private static void SyncTexture(VisualElement element, SceneViewBinding binding)
        {
            var camera = binding.Settings.Camera;
            if (camera == null)
            {
                ReleaseOutput(element, binding);
                return;
            }

            var w = element.layout.width;
            var h = element.layout.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h) || element.panel == null)
            {
                ReleaseOutput(element, binding);
                return;
            }

            // The clamp keeps a sub-pixel laid-out rect (a fractional size times a small scale) from
            // rounding to a zero-dimension texture, which RenderTexture creation rejects.
            var scale = binding.Settings.ResolutionScale;
            var pw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            var ph = Mathf.Max(1, Mathf.RoundToInt(h * scale));
            if (binding.Texture != null && binding.SizeKey == (pw, ph))
            {
                if (camera.targetTexture != binding.Texture)
                {
                    camera.targetTexture = binding.Texture;
                }
                return;
            }

            // Target the new texture BEFORE destroying the old one so the camera never points at a
            // dead texture in between (the re-target is itself the polite release of our own texture).
            var previous = binding.Texture;
            var texture = new RenderTexture(pw, ph, 24);
            binding.Texture = texture;
            binding.SizeKey = (pw, ph);
            camera.targetTexture = texture;
            element.style.backgroundImage = Background.FromRenderTexture(texture);
            element.MarkDirtyRepaint();
            if (previous != null)
            {
                previous.Release();
                VelvetObjectUtil.Destroy(previous);
            }
        }

        // Releases both ends of the output pair: politely untargets the camera, destroys the texture,
        // and clears the background image so no dead-texture reference survives on the element.
        private static void ReleaseOutput(VisualElement element, SceneViewBinding binding)
        {
            ReleaseCameraTarget(binding.Settings.Camera, binding.Texture);
            if (binding.Texture == null)
            {
                return;
            }
            binding.Texture.Release();
            VelvetObjectUtil.Destroy(binding.Texture);
            binding.Texture = null;
            binding.SizeKey = default;
            element.style.backgroundImage = StyleKeyword.Null;
            element.MarkDirtyRepaint();
        }

        // Polite camera release: untargets ONLY when the camera still points at the framework texture,
        // so a targetTexture the user reassigned after mount survives unmount untouched.
        private static void ReleaseCameraTarget(Camera? camera, RenderTexture? texture)
        {
            if (camera != null && texture != null && camera.targetTexture == texture)
            {
                camera.targetTexture = null;
            }
        }
    }
}
