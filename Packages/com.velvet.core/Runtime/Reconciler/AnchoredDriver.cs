#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one Anchored element, keyed in ReconcilerContext.AnchoredBindings by
    // the element itself. Holds the current settings (target/camera/offset) and the recurring per-frame tick
    // that re-projects the target's position (see AnchoredDriver.Sync) so it can be paused on detach.
    internal sealed class AnchoredBinding
    {
        public AnchoredSettings Settings;
        public IVisualElementScheduledItem? Tick;

        public AnchoredBinding(AnchoredSettings settings)
        {
            Settings = settings;
        }
    }

    /// <summary>
    /// Drives an Anchored element's screen position: every tick, projects <see cref="AnchoredSettings.Target"/>'s
    /// world position through <see cref="AnchoredSettings.Camera"/> (or <see cref="Camera.main"/> when null) via
    /// <see cref="RuntimePanelUtils.CameraTransformWorldToPanel"/> and writes the result as inline
    /// <c>left</c>/<c>top</c> — drei's <c>&lt;Html&gt;</c> parity (screen-space projection, not depth-tested
    /// against scene geometry, unlike <c>V.WorldSpace</c>). <c>RuntimePanelUtils.CameraTransformWorldToPanel</c>
    /// is correct here specifically because the element lives in an ordinary screen-space (Overlay/Camera) panel:
    /// <c>PanelSettings.ApplyPanelSettings</c> only resolves the scale factor this API divides by
    /// (<c>ScreenToPanel</c>'s <c>screen / scale</c>) for non-WorldSpace render modes — the same API returns the
    /// input essentially unchanged for a <c>V.WorldSpace</c>-hosted panel, which pins scale to 1 and sizes itself
    /// from the host Transform instead (see <c>PanelHostFactory</c>'s own note on this API's dual, panel-mode-
    /// dependent behavior).
    /// </summary>
    internal static class AnchoredDriver
    {
        // Per-frame cadence, matching the sibling per-frame element drivers (SceneViewDriver, ParticlesDriver).
        internal const long TickIntervalMs = 16;

        public static AnchoredBinding Attach(VisualElement element, AnchoredSettings settings)
        {
            var binding = new AnchoredBinding(settings);
            // Forced inline, not through a USS class: dynamic left/top positioning has no other way to work,
            // and writing it here (rather than baking "absolute" into V.Anchored's className, which would grow
            // ParseClassNames' cache by one entry per distinct caller className) mirrors how
            // GeneralPathReconciler pins a PopLayout ghost out of flow via style.position directly.
            element.style.position = Position.Absolute;
            binding.Tick = element.schedule.Execute(() => Sync(element, binding)).Every(TickIntervalMs);
            // Sync once synchronously too: the element may already be laid out (a binding arriving through a
            // patch rather than mount), and waiting for the first scheduled tick would show one frame at
            // whatever position the element last held (its layout-flow default, since Anchored forces
            // position: absolute) instead of its target's projected one.
            Sync(element, binding);
            return binding;
        }

        public static void Update(VisualElement element, AnchoredBinding binding, AnchoredSettings settings)
        {
            binding.Settings = settings;
            Sync(element, binding);
        }

        public static void Detach(VisualElement element, AnchoredBinding binding)
        {
            binding.Tick?.Pause();
            binding.Tick = null;
            // Release every inline style Attach/Sync forced, so a pooled element does not ghost a stale
            // absolute position (or a display:none from having last synced behind the camera) onto whatever
            // it is reused for next — the recurring pool-reuse footgun this codebase's own reset helpers
            // (e.g. FiberElementPoolReset) exist to avoid.
            element.style.position = StyleKeyword.Null;
            element.style.left = StyleKeyword.Null;
            element.style.top = StyleKeyword.Null;
            element.style.display = StyleKeyword.Null;
        }

        private static void Sync(VisualElement element, AnchoredBinding binding)
        {
            var target = binding.Settings.Target;
            if (target == null)
            {
                element.style.display = DisplayStyle.None;
                return;
            }

            // No panel yet (off-tree at Attach time) or no camera to project through: leave the element at
            // its current position rather than hiding it — Attach's own synchronous Sync call, or the next
            // tick once the element attaches, resolves it as soon as a panel/camera are available.
            var panel = element.panel;
            if (panel == null)
            {
                return;
            }
            var camera = binding.Settings.Camera != null ? binding.Settings.Camera : Camera.main;
            if (camera == null)
            {
                return;
            }

            // View-space depth test (drei's own isObjectBehindCamera equivalent): a target behind the camera
            // plane projects through WorldToScreenPoint with its x/y mirrored, which CameraTransformWorldToPanel
            // would otherwise turn into a wildly wrong on-screen position rather than an obviously-off-screen one.
            var toTarget = target.position - camera.transform.position;
            var isBehindCamera = Vector3.Dot(camera.transform.forward, toTarget) <= 0f;
            if (binding.Settings.HideWhenBehindCamera && isBehindCamera)
            {
                element.style.display = DisplayStyle.None;
                return;
            }
            element.style.display = DisplayStyle.Flex;

            // RuntimePanelUtils.CameraTransformWorldToPanel casts its panel argument to BaseRuntimePanel
            // internally — correct and necessary for the ordinary ScreenSpaceOverlay/ScreenSpaceCamera panel
            // V.Anchored targets, but an InvalidCastException for an EDITOR panel (contextType.Editor, e.g.
            // Velvet content mounted into an EditorWindow's rootVisualElement — a supported Velvet scenario,
            // see the preview-tooling docs). The element still shows/hides correctly from the behind-camera
            // check above; only its position stays wherever it last was rather than crashing, since an
            // Anchored element has no well-defined screen projection outside a runtime panel.
            if (panel.contextType != ContextType.Player)
            {
                return;
            }
            var panelPoint = RuntimePanelUtils.CameraTransformWorldToPanel(panel, target.position, camera);
            var offset = binding.Settings.Offset;
            element.style.left = panelPoint.x + offset.x;
            element.style.top = panelPoint.y + offset.y;
        }
    }
}
