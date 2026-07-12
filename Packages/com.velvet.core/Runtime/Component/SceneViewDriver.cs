#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one SceneView element, keyed in
    // ReconcilerContext.SceneViewBindings by the element itself. Holds the current settings (camera +
    // resolution scale), the framework-owned RenderTexture the camera renders into, the registered
    // geometry callback (so it can be unregistered on detach), and the recurring editor-panel repaint
    // tick (see SyncRepaintTick) so it can be paused whenever the texture is released.
    internal sealed class SceneViewBinding
    {
        public SceneViewSettings Settings;
        public RenderTexture? Texture;
        public EventCallback<GeometryChangedEvent>? OnGeometryChanged;
        public IVisualElementScheduledItem? RepaintTick;

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
        // Editor-panel repaint cadence (~60fps), matching the recurring-tick interval the style
        // animation drivers schedule with.
        private const long RepaintIntervalMs = 16;

        // The texture ceiling per axis. The sibling texture bakers bound their bake sizes the same
        // way — an unbounded element (a stretch inside a huge scroll canvas) must not translate into
        // an unbounded VRAM request. 4096 is comfortably above any sane on-screen element while still
        // universally supported.
        private const int MaxTextureSize = 4096;

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
        // element stays mounted and inert). NB the props diff gating this call compares the settings
        // RECORDS (value equality) while the camera fields here compare through Unity's overloaded ==;
        // the two disagree when one side is a DESTROYED camera and the other is literal null (distinct
        // record values, both "null" to Unity). Correctness therefore never rides on the record
        // verdict alone: SyncTexture re-derives everything from its own camera == null check.
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
        // (keyed on the live texture's own dimensions), and an unchanged-size texture is simply
        // re-targeted (where a camera swap lands).
        private static void SyncTexture(VisualElement element, SceneViewBinding binding)
        {
            var camera = binding.Settings.Camera;
            var w = element.layout.width;
            var h = element.layout.height;
            if (camera == null || w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h) || element.panel == null)
            {
                ReleaseOutput(element, binding);
                return;
            }

            // Size in device pixels: layout points times the resolution scale times the panel's pixel
            // density, so a scaled panel (HiDPI editor, runtime panel scaling) gets pixel-sharp output.
            // The floor keeps a sub-pixel rect from rounding to a zero-dimension texture (which
            // RenderTexture creation rejects); the ceiling bounds the VRAM request like the sibling
            // texture bakers bound theirs.
            var pixelScale = binding.Settings.ResolutionScale * element.scaledPixelsPerPoint;
            var pw = Mathf.Clamp(Mathf.RoundToInt(w * pixelScale), 1, MaxTextureSize);
            var ph = Mathf.Clamp(Mathf.RoundToInt(h * pixelScale), 1, MaxTextureSize);
            if (binding.Texture != null && binding.Texture.width == pw && binding.Texture.height == ph)
            {
                if (camera.targetTexture != binding.Texture)
                {
                    camera.targetTexture = binding.Texture;
                    // The texture object shown by the background is unchanged, so nothing else marks
                    // the element dirty on a bare camera swap — without this a dirty-on-demand panel
                    // would keep showing the old camera's last frame.
                    element.MarkDirtyRepaint();
                }
                SyncRepaintTick(element, binding);
                return;
            }

            // Target the new texture BEFORE destroying the old one so the camera never points at a
            // dead texture in between (the re-target is itself the polite release of our own texture).
            var previous = binding.Texture;
            var texture = new RenderTexture(pw, ph, 24);
            binding.Texture = texture;
            camera.targetTexture = texture;
            element.style.backgroundImage = Background.FromRenderTexture(texture);
            element.MarkDirtyRepaint();
            if (previous != null)
            {
                previous.Release();
                VelvetObjectUtil.Destroy(previous);
            }
            SyncRepaintTick(element, binding);
        }

        // An Editor-context panel repaints only when something marks it dirty, so a live camera feed
        // would freeze on its first frame there (nothing in UI Toolkit knows the sampled texture's
        // CONTENTS changed). Drive a recurring repaint while a texture is live on an editor panel;
        // runtime panels render every frame already and get no tick. The scheduled item fires only
        // when the panel ticks its scheduler, so a headless (batch) editor panel schedules it inertly.
        // Paused whenever the texture is released or the binding detaches.
        private static void SyncRepaintTick(VisualElement element, SceneViewBinding binding)
        {
            var wantTick = binding.Texture != null && element.panel?.contextType == ContextType.Editor;
            if (wantTick && binding.RepaintTick == null)
            {
                binding.RepaintTick = element.schedule.Execute(element.MarkDirtyRepaint).Every(RepaintIntervalMs);
            }
            else if (!wantTick)
            {
                StopRepaintTick(binding);
            }
        }

        private static void StopRepaintTick(SceneViewBinding binding)
        {
            binding.RepaintTick?.Pause();
            binding.RepaintTick = null;
        }

        // Releases both ends of the output pair: politely untargets the camera, destroys the texture,
        // and clears the background image so no dead-texture reference survives on the element.
        private static void ReleaseOutput(VisualElement element, SceneViewBinding binding)
        {
            StopRepaintTick(binding);
            ReleaseCameraTarget(binding.Settings.Camera, binding.Texture);
            if (binding.Texture == null)
            {
                return;
            }
            binding.Texture.Release();
            VelvetObjectUtil.Destroy(binding.Texture);
            binding.Texture = null;
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
