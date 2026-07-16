#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one Anchored element, keyed in ReconcilerContext.AnchoredBindings by
    // the element itself. Holds the current settings (target/camera/offset), the recurring per-frame tick
    // that re-projects the target's position (see AnchoredDriver.Sync) so it can be paused on detach, the
    // registered geometry callback (so it can be unregistered on detach), and a one-shot flag so the
    // Editor-context degradation warns exactly once per binding instead of every tick.
    internal sealed class AnchoredBinding
    {
        public AnchoredSettings Settings;
        public IVisualElementScheduledItem? Tick;
        public EventCallback<GeometryChangedEvent>? OnGeometryChanged;
        public bool WarnedAboutUnsupportedPanel;

        public AnchoredBinding(AnchoredSettings settings)
        {
            Settings = settings;
        }
    }

    /// <summary>
    /// Drives an Anchored element's screen position: every tick, projects <see cref="AnchoredSettings.Target"/>'s
    /// world position through <see cref="AnchoredSettings.Camera"/> (or <see cref="Camera.main"/> when null) via
    /// <see cref="RuntimePanelUtils.CameraTransformWorldToPanel"/>, converts the result from panel-root space into
    /// the element's own PARENT-relative space (subtracting <c>element.parent.worldBound.position</c> — UI
    /// Toolkit resolves <c>position: absolute</c> <c>left</c>/<c>top</c> against the immediate parent, not the
    /// panel root, unlike CSS's nearest-positioned-ancestor walk), and writes the result as inline
    /// <c>left</c>/<c>top</c> — drei's
    /// <c>&lt;Html&gt;</c> parity (screen-space projection, not depth-tested against scene geometry, unlike
    /// <c>V.WorldSpace</c>). <c>RuntimePanelUtils.CameraTransformWorldToPanel</c> is correct here specifically
    /// because the element lives in an ordinary screen-space (Overlay/Camera) panel: <c>PanelSettings.
    /// ApplyPanelSettings</c> only resolves the scale factor this API divides by (<c>ScreenToPanel</c>'s
    /// <c>screen / scale</c>) for non-WorldSpace render modes — the same API returns the input essentially
    /// unchanged for a <c>V.WorldSpace</c>-hosted panel (see <c>PanelHostFactory</c>'s own note on this API's dual,
    /// panel-mode-dependent behavior). <b>Not supported:</b> nesting a <c>V.Anchored</c> element inside a
    /// <c>V.WorldSpace</c> panel's children — that panel is still <c>ContextType.Player</c> (the guard below can't
    /// distinguish it from an ordinary screen-space runtime panel without reflecting into an internal engine
    /// property), so it silently gets the same near-raw-world-space values the API is documented to degrade to
    /// there. <c>V.Anchored</c> targets an ordinary screen-space panel only.
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
            // Also re-sync on the element's own geometry changes (mirrors SceneViewDriver's geometry-driven
            // texture sync): the recurring tick can fire BEFORE the first layout pass has placed the
            // element's ancestors — the parent-relative conversion below then reads a not-yet-laid-out
            // parent origin — and the next tick is a full wall-clock interval away, one frame too late for
            // the position to be right when the element first paints. The geometry event fires the moment
            // the element's own rect settles, immediately after that same layout pass.
            binding.OnGeometryChanged = _ => Sync(element, binding);
            element.RegisterCallback(binding.OnGeometryChanged);
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
            if (binding.OnGeometryChanged != null)
            {
                element.UnregisterCallback(binding.OnGeometryChanged);
                binding.OnGeometryChanged = null;
            }
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

            // No panel yet (off-tree at Attach time): leave the element at its current position rather than
            // hiding it — the next tick once the element attaches resolves it.
            var panel = element.panel;
            if (panel == null)
            {
                return;
            }
            var camera = binding.Settings.Camera != null ? binding.Settings.Camera : Camera.main;
            if (camera == null)
            {
                // No camera to project through (an explicit Camera destroyed, or no MainCamera-tagged camera
                // in the scene) — there is no sensible position to hold, so hide rather than freeze at a
                // screen position that no longer corresponds to anything, mirroring the target==null branch.
                element.style.display = DisplayStyle.None;
                return;
            }

            // View-space depth test (drei's own isObjectBehindCamera equivalent): a target behind the camera
            // plane projects through WorldToScreenPoint with its x/y mirrored, which CameraTransformWorldToPanel
            // would otherwise turn into a wildly wrong on-screen position rather than an obviously-off-screen
            // one — including when HideWhenBehindCamera is false, so that opt-out does not attempt this
            // projection at all rather than "tracking" a mirrored/garbage point. Checked before the panel-type
            // guard below: whether the target even faces the camera is independent of what kind of panel this
            // element happens to live in.
            var toTarget = target.position - camera.transform.position;
            var isBehindCamera = Vector3.Dot(camera.transform.forward, toTarget) < 0f;
            if (isBehindCamera)
            {
                if (binding.Settings.HideWhenBehindCamera)
                {
                    element.style.display = DisplayStyle.None;
                }
                return;
            }

            // RuntimePanelUtils.CameraTransformWorldToPanel casts its panel argument to BaseRuntimePanel
            // internally — correct and necessary for the ordinary ScreenSpaceOverlay/ScreenSpaceCamera panel
            // V.Anchored targets, but an InvalidCastException for an EDITOR panel (contextType.Editor, e.g.
            // Velvet content mounted into an EditorWindow's rootVisualElement — a supported Velvet scenario,
            // see the preview-tooling docs). An unsupported panel has no well-defined position at all, so it
            // is treated the same as "no camera" above rather than showing at a stale/never-set position.
            if (panel.contextType != ContextType.Player)
            {
                if (!binding.WarnedAboutUnsupportedPanel)
                {
                    binding.WarnedAboutUnsupportedPanel = true;
                    FiberLogger.LogWarning("Anchored",
                        "This element's panel is not a runtime (Player-context) panel, so its screen "
                        + "projection is undefined here (e.g. Velvet content mounted into an EditorWindow). "
                        + "Hiding it instead of showing a stale or arbitrary position.");
                }
                element.style.display = DisplayStyle.None;
                return;
            }

            // Clears any inline override from a previous behind-camera/no-camera/unsupported-panel hide,
            // rather than forcing DisplayStyle.Flex: an inline display would otherwise permanently outrank
            // the "hidden" USS class Props.Visible = false toggles (FiberPropApplier.ApplyVisible), since a
            // non-!important stylesheet rule never beats an inline style. StyleKeyword.Null lets the normal
            // class-driven cascade (including Visible = false) decide instead.
            element.style.display = StyleKeyword.Null;

            var panelPoint = RuntimePanelUtils.CameraTransformWorldToPanel(panel, target.position, camera);
            // CameraTransformWorldToPanel returns a point in PANEL-ROOT space; position: absolute resolves
            // left/top against the element's own PARENT, not the panel root (UI Toolkit/Yoga, unlike CSS, has
            // no nearest-positioned-ancestor walk — it is always parent-relative). worldBound reports an
            // element's rect already resolved into panel-root space, so subtracting the parent's own
            // worldBound origin converts panelPoint into the parent-relative point position: absolute needs.
            // (VisualElement.WorldToLocal looked like the more principled tool for this, but empirically
            // returns its input unchanged for an ordinary layout-positioned — not CSS-transformed — parent,
            // i.e. it is NOT a general panel-to-parent-layout-space converter; worldBound subtraction is the
            // one that was verified correct via a real nested-margin PlayMode test.) No conversion needed
            // only when the element sits directly on the panel root (no parent), which already coincides
            // with panel space.
            var localPoint = panelPoint;
            if (element.parent != null)
            {
                var parentOrigin = (Vector2)element.parent.worldBound.position;
                // A parent that has never been laid out reports a NaN-sized worldBound whose origin cannot
                // be trusted yet — skip this write and let the geometry callback registered in Attach re-sync
                // the moment the first layout pass settles, rather than baking a wrong origin into left/top.
                if (float.IsNaN(parentOrigin.x) || float.IsNaN(parentOrigin.y))
                {
                    return;
                }
                localPoint = panelPoint - parentOrigin;
            }
            var offset = binding.Settings.Offset;
            element.style.left = localPoint.x + offset.x;
            element.style.top = localPoint.y + offset.y;
        }
    }
}
