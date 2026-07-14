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
        // An explicit camera/settings pass (attach, props update) claims the camera even from a
        // foreign targetTexture; the intent is recorded here because the claiming sync itself may
        // bail pre-layout and the FIRST sync that gets past the validity gate must inherit it.
        // Layout- and tick-driven resyncs never claim a foreign target on their own.
        public bool ClaimOnNextSync;

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
        // Repaint cadence (~60fps), matching the recurring-tick interval the style animation drivers
        // schedule with. Shared with the sibling per-frame element driver (ParticlesDriver).
        internal const long RepaintIntervalMs = 16;

        // The texture ceiling per axis. The sibling texture bakers bound their bake sizes the same
        // way — an unbounded element (a stretch inside a huge scroll canvas) must not translate into
        // an unbounded VRAM request. 4096 is comfortably above any sane on-screen element while still
        // universally supported.
        private const int MaxTextureSize = 4096;

        // The pixel-size rounding grain TryComputePixelSize quantizes up to. 16 divides
        // MaxTextureSize evenly (4096 / 16 = 256), so the ceiling clamp and this step never leave a
        // leftover partial bucket at the boundary. Large enough to absorb the few-pixels-per-frame
        // deltas a drag-resize or an animated layout produces — so most jitter keeps landing on the
        // same bucket and reuses the existing RenderTexture instead of reallocating — while small
        // enough that the worst-case 15px-per-axis overshoot is negligible against any on-screen
        // SceneView size.
        private const int SizeQuantizationStep = 16;

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
            binding.ClaimOnNextSync = true;
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
            binding.ClaimOnNextSync = true;
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
        // point shared by attach, patch, geometry changes and the editor tick's staleness probe. No
        // camera, or no renderable size (pre-layout NaN, a zero-sized rect, a detached element),
        // releases any existing output; otherwise the texture is re-created only when the effective
        // pixel size actually changed (keyed on the live texture's own dimensions), and an
        // unchanged-size texture is simply re-targeted (where a camera swap lands).
        // Claim intent: an explicit camera/settings pass sets ClaimOnNextSync and the first sync past
        // the validity gate consumes it, claiming the camera even from a foreign targetTexture.
        // Without it a resync reclaims only from null or from the framework's own outgoing texture;
        // while user code holds the camera, the element keeps showing the last framework frame
        // rather than re-deriving a texture nothing would render into.
        private static void SyncTexture(VisualElement element, SceneViewBinding binding)
        {
            var camera = binding.Settings.Camera;
            if (camera == null || element.panel == null
                || !TryComputePixelSize(element, binding, out var pw, out var ph))
            {
                ReleaseOutput(element, binding);
                return;
            }
            var claim = binding.ClaimOnNextSync;
            binding.ClaimOnNextSync = false;

            if (binding.Texture != null && binding.Texture.width == pw && binding.Texture.height == ph)
            {
                if (camera.targetTexture != binding.Texture
                    && (claim || camera.targetTexture == null))
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

            var previous = binding.Texture;
            if (!claim && camera.targetTexture != null && camera.targetTexture != previous)
            {
                // The camera is borrowed: re-deriving the texture now would swap the background to an
                // image nothing renders into and destroy the last good frame. Keep showing that
                // frame; the next explicit camera/settings pass reclaims and re-derives the size.
                SyncRepaintTick(element, binding);
                return;
            }

            // Target the new texture BEFORE destroying the old one so the camera never points at a
            // dead texture in between (the re-target is itself the polite release of our own texture).
            var texture = new RenderTexture(pw, ph, 24);
            binding.Texture = texture;
            camera.targetTexture = texture;
            if (element is SceneViewElement sceneView)
            {
                // Taking the slot captures whatever the element was showing (a poster, a baked
                // gradient) as the deferred restore target; class/style writers keep updating that
                // deferred value through the ownership gate instead of clobbering the live feed.
                sceneView.BeginCameraOwnership();
            }
            element.style.backgroundImage = Background.FromRenderTexture(texture);
            element.MarkDirtyRepaint();
            if (previous != null)
            {
                previous.Release();
                VelvetObjectUtil.Destroy(previous);
            }
            SyncRepaintTick(element, binding);
        }

        // Derives the texture's target pixel size from the CURRENT layout, scale and pixel density —
        // shared by the sync and the editor tick's staleness probe so the two can never disagree
        // about what "current" means. Size in device pixels: layout points times the resolution
        // scale times the panel's pixel density, so a scaled panel (HiDPI editor, runtime panel
        // scaling) gets pixel-sharp output. The floor keeps a sub-pixel rect from rounding to a
        // zero-dimension texture (which RenderTexture creation rejects); the ceiling bounds the VRAM
        // request like the sibling texture bakers bound theirs — applied as ONE shared shrink when
        // either axis overflows, because clamping each axis independently would change the texture's
        // aspect and distort the camera picture. The result is then quantized UP to
        // SizeQuantizationStep: minor layout jitter (a drag-resize, an animated layout moving a
        // handful of pixels a frame) keeps landing on the SAME quantized size, so SyncTexture's
        // exact-match reuse check hits instead of reallocating the RenderTexture on every change.
        // Re-clamped to MaxTextureSize afterward — quantizing up could otherwise carry a
        // near-ceiling size into the next bucket past the cap.
        private static bool TryComputePixelSize(VisualElement element, SceneViewBinding binding, out int pw, out int ph)
        {
            pw = 0;
            ph = 0;
            var w = element.layout.width;
            var h = element.layout.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h))
            {
                return false;
            }
            var pixelScale = binding.Settings.ResolutionScale * element.scaledPixelsPerPoint;
            pw = Mathf.Max(1, Mathf.RoundToInt(w * pixelScale));
            ph = Mathf.Max(1, Mathf.RoundToInt(h * pixelScale));
            if (pw > MaxTextureSize || ph > MaxTextureSize)
            {
                var shrink = Mathf.Min((float)MaxTextureSize / pw, (float)MaxTextureSize / ph);
                pw = Mathf.Clamp(Mathf.FloorToInt(pw * shrink), 1, MaxTextureSize);
                ph = Mathf.Clamp(Mathf.FloorToInt(ph * shrink), 1, MaxTextureSize);
            }
            pw = Mathf.Min(QuantizeUp(pw), MaxTextureSize);
            ph = Mathf.Min(QuantizeUp(ph), MaxTextureSize);
            return true;
        }

        // Rounds a pixel size up to the next SizeQuantizationStep multiple (e.g. 100 -> 112 at the
        // 16px step); an already-aligned value passes through unchanged.
        private static int QuantizeUp(int value)
        {
            return (value + SizeQuantizationStep - 1) / SizeQuantizationStep * SizeQuantizationStep;
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
                binding.RepaintTick = element.schedule.Execute(() => OnRepaintTick(element, binding)).Every(RepaintIntervalMs);
            }
            else if (!wantTick)
            {
                StopRepaintTick(binding);
            }
        }

        // Beyond dirtying the panel, the tick doubles as the staleness probe: a pixel-density change
        // (a monitor-DPI move, a runtime panel-scale tweak) alters the derived pixel size with NO
        // geometry event — points are unchanged — so no other signal would ever re-derive the
        // texture. Runtime panels have no tick and heal on their next geometry or props pass instead.
        private static void OnRepaintTick(VisualElement element, SceneViewBinding binding)
        {
            if (binding.Texture != null
                && TryComputePixelSize(element, binding, out var pw, out var ph)
                && (binding.Texture.width != pw || binding.Texture.height != ph))
            {
                SyncTexture(element, binding);
            }
            element.MarkDirtyRepaint();
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
            if (element is SceneViewElement sceneView)
            {
                // Releasing the feed returns the slot to whatever writer was deferred while the
                // camera owned it (a poster, a baked gradient) — or clears it when none was. Every
                // binding is keyed by a SceneViewElement (the applier type-gates), so this is the
                // only live branch.
                sceneView.EndCameraOwnership();
            }
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
