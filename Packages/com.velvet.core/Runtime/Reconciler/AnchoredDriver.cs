#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one Anchored element, keyed in ReconcilerContext.AnchoredBindings by
    // the element itself. Holds the current settings (target/camera/offset), the recurring per-frame tick
    // that re-projects the target's position (see AnchoredDriver.Sync) so it can be paused on detach, and the
    // registered geometry callback (so it can be unregistered on detach).
    internal sealed class AnchoredBinding
    {
        public AnchoredSettings Settings;
        public IVisualElementScheduledItem? Tick;
        public EventCallback<GeometryChangedEvent>? OnGeometryChanged;
        // The world-space document AnchoredDriver.DocumentHolding last resolved, and the root it resolved it for.
        public VisualElement? DocumentRoot;
        public UIDocument? Document;
        // Tracks whether THIS binding is the one currently holding element.style.scale, so it only ever
        // clears a value it actually wrote — see AnchoredDriver.ClearAppliedScale.
        public bool HasAppliedDistanceFactorScale;

        public AnchoredBinding(AnchoredSettings settings)
        {
            Settings = settings;
        }
    }

    /// <summary>
    /// Drives an Anchored element's position: every tick, projects <see cref="AnchoredSettings.Target"/>'s
    /// world position through <see cref="AnchoredSettings.Camera"/> (or <see cref="Camera.main"/> when null)
    /// into the element's panel and writes it, relative to the element's parent, as inline
    /// <c>left</c>/<c>top</c> — ordinary 2D UI with no inherent scene depth, unlike <c>V.WorldSpace</c>
    /// (<see cref="AnchoredSettings.Occlude"/> opts into a physics stand-in for that test). A screen-space
    /// runtime panel projects through <see cref="RuntimePanelUtils.CameraTransformWorldToPanel"/>, an editor
    /// panel lays the camera's viewport over itself, and a world-space panel takes the point where the
    /// camera's ray to the target crosses it.
    /// </summary>
    internal static class AnchoredDriver
    {
        // Per-frame cadence, matching the sibling per-frame element drivers (SceneViewDriver, ParticlesDriver).
        internal const long TickIntervalMs = 16;

        // Below this camera-to-target distance, distanceFactor / distance diverges toward whatever float
        // range is left rather than anything a screen-space scale should read as — a real-world Unity
        // scene can plausibly put a camera this close to a target (unlike Mathf.Epsilon, which only
        // excludes an exact, near-impossible 0), so scale holds at 1 instead of ballooning.
        private const float MinDistanceFactorDistance = 0.01f;

        public static AnchoredBinding Attach(VisualElement element, AnchoredSettings settings)
        {
            var binding = new AnchoredBinding(settings);
            // Forced inline, not through a USS class: dynamic left/top positioning has no other way to work,
            // and writing it here (rather than baking "absolute" into V.Anchored's className, which would grow
            // ParseClassNames' cache by one entry per distinct caller className) mirrors how
            // GeneralPathReconciler pins a PopLayout ghost out of flow via style.position directly. Position,
            // offsets and display go to the box its parent lays out, which is the clip wrapper of a clipped element
            // (ClipPathLayoutBox).
            ClipPathLayoutBox.Of(element).style.position = Position.Absolute;
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
            // absolute position (or a display:none from having last synced behind the camera) onto
            // whatever it is reused for next — the recurring pool-reuse footgun this codebase's own reset
            // helpers (e.g. FiberElementPoolReset) exist to avoid.
            var box = ClipPathLayoutBox.Of(element).style;
            box.position = StyleKeyword.Null;
            box.left = StyleKeyword.Null;
            box.top = StyleKeyword.Null;
            box.display = StyleKeyword.Null;
            ClearAppliedScale(element, binding);
        }

        // Clears element.style.scale ONLY when this binding is the one that last wrote it (distanceFactor
        // was in effect) — an unconditional clear here would also stomp a scale value some OTHER system
        // (a scale-* utility class, a Motion hover/tap/transition variant) is independently driving on the
        // same element, since nothing about V.Anchored implies it owns that style slot unless
        // distanceFactor actually opted it in.
        private static void ClearAppliedScale(VisualElement element, AnchoredBinding binding)
        {
            if (!binding.HasAppliedDistanceFactorScale) return;
            element.style.scale = StyleKeyword.Null;
            binding.HasAppliedDistanceFactorScale = false;
        }

        // The recurring "nothing sensible to show" reaction shared by every Sync guard below: hide the
        // element and drop any distanceFactor scale it applied on an earlier tick, so a guard that starts
        // failing never leaves a stale position/scale on screen.
        private static void HideAndClearScale(VisualElement element, AnchoredBinding binding)
        {
            ClipPathLayoutBox.Of(element).style.display = DisplayStyle.None;
            ClearAppliedScale(element, binding);
        }

        private static void Sync(VisualElement element, AnchoredBinding binding)
        {
            var target = binding.Settings.Target;
            if (target == null)
            {
                HideAndClearScale(element, binding);
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
                HideAndClearScale(element, binding);
                return;
            }

            // drei's Html hides a target behind the camera, and where an onOcclude handler takes that hide
            // over it keeps writing the projection rather than holding the last position.
            var toTarget = target.position - camera.transform.position;
            if (binding.Settings.HideWhenBehindCamera && Vector3.Dot(camera.transform.forward, toTarget) < 0f)
            {
                HideAndClearScale(element, binding);
                return;
            }

            // Opt-in physics stand-in for scene depth: a solid collider
            // between the camera and the target hides the element instead of painting it through scene
            // geometry. Linecast's endpoint sits exactly at the target's own pivot, so a target whose own
            // collider encloses that pivot will typically self-occlude — scope OccludeLayerMask to scene
            // geometry that excludes the target's layer rather than trying to filter the target out of a
            // hit result (RaycastAll's allocation is not worth paying for every tick of every binding).
            // Triggers never occlude: they are gameplay volumes, not solid geometry.
            if (binding.Settings.Occlude && Physics.Linecast(
                    camera.transform.position, target.position, binding.Settings.OccludeLayerMask, QueryTriggerInteraction.Ignore))
            {
                HideAndClearScale(element, binding);
                return;
            }

            var box = ClipPathLayoutBox.Of(element);
            Vector2 localPoint;
            if (panel is IRuntimePanel { panelSettings: { renderMode: PanelRenderMode.WorldSpace } })
            {
                // A world-space panel has no screen to project into: the element goes where the camera's ray
                // to the target crosses the panel's plane.
                // How far along toTarget, from the camera, the ray meets the plane: at or behind the camera, or
                // never where the ray runs along the plane, there is no crossing to show.
                var document = DocumentHolding(box, binding);
                var along = document != null ? Vector3.Dot(document.transform.forward, toTarget) : 0f;
                var enter = along != 0f
                    ? Vector3.Dot(document!.transform.forward, document.transform.position - camera.transform.position) / along
                    : -1f;
                if (enter <= 0f)
                {
                    HideAndClearScale(element, binding);
                    return;
                }
                // The panel's own space is its root document's local space, so the parent's inverse world
                // transform takes the crossing point into the space left/top resolve against.
                var crossing = document!.transform.InverseTransformPoint(camera.transform.position + toTarget * enter);
                localPoint = box.parent.WorldToLocal(new Vector2(crossing.x, crossing.y));
            }
            else
            {
                // position: absolute resolves left/top against the element's own PARENT (UI Toolkit has no
                // nearest-positioned-ancestor walk), so the panel point is taken to the parent's origin.
                var parentOrigin = (Vector2)box.parent.worldBound.position;
                // A parent that has never been laid out reports a NaN-sized worldBound whose origin cannot
                // be trusted yet — skip this write and let the geometry callback registered in Attach re-sync
                // the moment the first layout pass settles, rather than baking a wrong origin into left/top.
                if (float.IsNaN(parentOrigin.x) || float.IsNaN(parentOrigin.y))
                {
                    return;
                }
                localPoint = PanelPoint(panel, target.position, camera) - parentOrigin;
            }
            // Clears any inline override from a previous hide rather than forcing DisplayStyle.Flex: an inline
            // display would otherwise permanently outrank the "hidden" USS class Props.Visible = false toggles
            // (FiberPropApplier.ApplyVisible), since a non-!important stylesheet rule never beats an inline
            // style. StyleKeyword.Null lets the normal class-driven cascade (including Visible = false) decide
            // instead. A Suspense keeping the element hidden owns that inline display until it reveals it.
            if (!SuspenseHiddenElements.IsHidden(box)) box.style.display = StyleKeyword.Null;
            var offset = binding.Settings.Offset;
            box.style.left = localPoint.x + offset.x;
            box.style.top = localPoint.y + offset.y;

            var distanceFactor = binding.Settings.DistanceFactor;
            if (distanceFactor.HasValue)
            {
                // Flat screen-space content has no inherent size in
                // the scene, so faking perspective falloff means scaling it inversely with camera distance
                // — distanceFactor is the reference distance at which scale is exactly 1. toTarget (from the
                // behind-camera test above) already holds camera-to-target; reused here rather than a
                // second Vector3.Distance call.
                var distance = toTarget.magnitude;
                var scale = distance > MinDistanceFactorDistance ? distanceFactor.Value / distance : 1f;
                element.style.scale = new Scale(new Vector2(scale, scale));
                binding.HasAppliedDistanceFactorScale = true;
            }
            else
            {
                // Only clears a value THIS binding applied on an earlier tick (distanceFactor was set
                // then, cleared now) — never touches style.scale when distanceFactor has never been set,
                // so an Anchored element combined with a scale-* class or a Motion scale variant on the
                // same element is left entirely to whichever of those systems is actually driving it.
                ClearAppliedScale(element, binding);
            }
        }

        // RuntimePanelUtils.CameraTransformWorldToPanel casts its panel to BaseRuntimePanel, which an editor
        // panel (Velvet content mounted into an EditorWindow) is not. There the camera's viewport is laid
        // over the panel root, which is how drei's Html maps a projection onto its canvas.
        private static Vector2 PanelPoint(IPanel panel, Vector3 worldPosition, Camera camera)
        {
            if (panel.contextType == ContextType.Player)
            {
                return RuntimePanelUtils.CameraTransformWorldToPanel(panel, worldPosition, camera);
            }
            var viewport = camera.WorldToViewportPoint(worldPosition);
            var size = panel.visualTree.layout.size;
            return new Vector2(viewport.x * size.x, (1f - viewport.y) * size.y);
        }

        // The document whose root, directly under the panel's visual tree, holds box: several world-space
        // documents can share one panel, each placed by its own GameObject. Remembered per root so the scan
        // runs when box arrives under a root rather than every tick.
        private static UIDocument? DocumentHolding(VisualElement box, AnchoredBinding binding)
        {
            var root = box;
            while (root.hierarchy.parent != box.panel.visualTree)
            {
                root = root.hierarchy.parent;
            }
            if (binding.DocumentRoot != root)
            {
                binding.DocumentRoot = root;
                binding.Document = null;
                foreach (var document in Resources.FindObjectsOfTypeAll<UIDocument>())
                {
                    if (document.rootVisualElement == root)
                    {
                        binding.Document = document;
                        break;
                    }
                }
            }
            return binding.Document;
        }
    }
}
