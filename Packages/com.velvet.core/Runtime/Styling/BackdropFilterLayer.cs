#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one element carrying backdrop-* utilities, keyed in
    // ReconcilerContext.BackdropBindings by the element itself.
    internal sealed class BackdropBinding
    {
        public readonly BackdropFilterLayer Layer;
        public EventCallback<GeometryChangedEvent> OnGeometry = null!;
        public EventCallback<AttachToPanelEvent> OnAttach = null!;
        public IVisualElementScheduledItem StateTick = null!;
        // Run once a transition to none has ended, by the applier that let the layer play it out.
        public Action? OnSettledToNone;

        public BackdropBinding(BackdropFilterLayer layer) => Layer = layer;
    }

    /// <summary>
    /// Paints an element's <c>backdrop-*</c> utilities: an immediate-mode sibling placed directly BEFORE the element
    /// and over its box, which, when the renderer reaches it, copies what the target holds beneath that box and
    /// paints it back filtered (<see cref="BackdropRenderer"/>).
    /// </summary>
    /// <remarks>
    /// The layer is a sibling rather than a child because a child paints after its parent's background and border,
    /// which the backdrop must not hold. Being a sibling also puts it inside every mask its element is inside, a
    /// clip-path wrapper's included.
    /// <para>
    /// A transform, an opacity, a visibility or a display on the element reaches its own subtree only, so the layer is
    /// given each of them itself, as <see cref="RingOverlay"/> gives its band.
    /// </para>
    /// </remarks>
    internal sealed class BackdropFilterLayer : ImmediateModeElement
    {
        // Marks the layer as a reconciler-invisible child of its host (SilhouetteBoundsSpacer.IsSpacer).
        internal const string MarkerClass = "velvet-backdrop-layer";

        private readonly VisualElement _element;
        private BackdropSpec _spec = BackdropSpec.None;
        private BackdropTransition? _transition;

        private BackdropFilterLayer(VisualElement element)
        {
            _element = element;
            name = MarkerClass;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            AddToClassList(MarkerClass);
        }

        internal bool Transitioning => _transition != null;

        // Attached at none and changed to spec, so an element gaining its first backdrop-* class on a panel
        // transitions from none as CSS does; one created off a panel takes spec at once.
        public static BackdropBinding Attach(VisualElement element, BackdropSpec spec)
        {
            var layer = new BackdropFilterLayer(element);
            var binding = new BackdropBinding(layer);
            binding.OnGeometry = _ => Place(element, binding);
            element.RegisterCallback(binding.OnGeometry);
            // A keyed move or a z-* relocation re-parents the element without moving its rect (RingOverlay.Attach).
            binding.OnAttach = _ => Place(element, binding);
            element.RegisterCallback(binding.OnAttach);
            // Each frame, so an animated transform or opacity reaches the layer and a transition advances.
            binding.StateTick = element.schedule.Execute(() => Tick(element, binding)).Every(StyleAnimateDriver.TickMs);
            layer.ChangeTo(spec);
            Place(element, binding);
            return binding;
        }

        // Places every layer the context holds, at the reconcile boundary: an element created in the pass has its
        // parent only from there, and a sibling the reconciler inserts at an element's slot
        // (LogicalChildSlots.ToPhysical) lands between the layer and the element. The boundary runs before the panel
        // paints the pass.
        public static void DrainPendingPlacements(ReconcilerContext ctx)
        {
            foreach (var (element, binding) in ctx.BackdropBindings)
            {
                Place(element, binding);
            }
        }

        public static void Sync(VisualElement element, BackdropBinding binding, BackdropSpec spec)
        {
            binding.OnSettledToNone = null;
            binding.Layer.ChangeTo(spec);
            Place(element, binding);
        }

        public static void Detach(VisualElement element, BackdropBinding binding)
        {
            element.UnregisterCallback(binding.OnGeometry);
            element.UnregisterCallback(binding.OnAttach);
            binding.StateTick.Pause();
            // The layer lives in the element's parent, so it does not leave with the element's own subtree.
            binding.Layer.RemoveFromHierarchy();
        }

        // A change of the declared backdrop-filter: transitioned where an entry runs for it, by CSS's interpolation
        // (BackdropFilterSteps); landed at once otherwise. transition-property names it only through `all`, since a
        // name UI Toolkit has no property for reads back as none of them (StyleFilterTransitionDriver.TryFindTransition).
        internal void ChangeTo(BackdropSpec spec)
        {
            var target = BackdropFilterSteps.Resolve(spec, _element);
            var shown = Shown();
            _spec = spec;
            if (_transition != null && BackdropFilterSteps.Same(_transition.To, target))
            {
                return;
            }
            if (_element.panel == null
                || !StyleFilterTransitionDriver.TryFindTransition(_element, "backdrop-filter", null, out var durationMs,
                    out var delayMs, out var easing)
                || BackdropFilterSteps.Same(shown, target)
                || !BackdropFilterSteps.TryPair(shown, target, out var from, out var to))
            {
                _transition = null;
                return;
            }
            _transition = Started(from, to, durationMs / 1000f, delayMs / 1000f, easing);
        }

        private BackdropTransition Started(BackdropStep[] from, BackdropStep[] to, float duration, float delay,
            EasingMode easing)
        {
            var next = new BackdropTransition
            {
                From = from,
                To = to,
                ReversingStart = from,
                Clock = MotionClock.Of(_element),
                Easing = easing,
            };
            next.StartSec = next.Clock.NowSec;
            // CSS's reversing shortening: a change back to where the running transition started runs for the part of
            // the duration the running one has covered.
            if (_transition != null && BackdropFilterSteps.TryPair(_transition.ReversingStart, to, out var start, out _)
                && BackdropFilterSteps.Same(start, to))
            {
                var factor = Mathf.Clamp01(Mathf.Abs((_transition.Eased * _transition.ReversingFactor)
                    + 1f - _transition.ReversingFactor));
                next.ReversingFactor = factor;
                next.ReversingStart = _transition.To;
                duration *= factor;
                delay = delay < 0f ? delay * factor : delay;
            }
            next.DurationSec = duration;
            next.DelaySec = delay;
            return next;
        }

        // What the layer paints now.
        internal BackdropStep[] Shown()
            => _transition != null ? _transition.Now : BackdropFilterSteps.Resolve(_spec, _element);

        private static void Tick(VisualElement element, BackdropBinding binding)
        {
            var layer = binding.Layer;
            if (layer._transition != null)
            {
                layer.MarkDirtyRepaint();
                if (layer._transition.Progress >= 1f)
                {
                    layer._transition = null;
                    if (layer._spec.Ops.Length == 0)
                    {
                        binding.OnSettledToNone?.Invoke();
                    }
                }
            }
            Place(element, binding);
        }

        // Directly BEFORE the element, re-derived on every call: paint order is child order, and the layer has to
        // paint after everything behind the element and before the element itself.
        private static void Place(VisualElement element, BackdropBinding binding)
        {
            var host = element.parent;
            var layer = binding.Layer;
            if (host == null)
            {
                return;
            }
            if (layer.parent != host || host.IndexOf(layer) != host.IndexOf(element) - 1)
            {
                layer.RemoveFromHierarchy();
                host.Insert(host.IndexOf(element), layer);
            }
            var x = element.layout.x;
            var y = element.layout.y;
            // An absolute child's left and top resolve against the host's padding box (RingOverlay.SyncGeometry).
            var hostLeft = host.resolvedStyle.borderLeftWidth;
            var hostTop = host.resolvedStyle.borderTopWidth;
            var style = layer.style;
            style.left = (float.IsNaN(x) ? 0f : x) - (float.IsNaN(hostLeft) ? 0f : hostLeft);
            style.top = (float.IsNaN(y) ? 0f : y) - (float.IsNaN(hostTop) ? 0f : hostTop);
            style.width = float.IsNaN(element.layout.width) ? 0f : element.layout.width;
            style.height = float.IsNaN(element.layout.height) ? 0f : element.layout.height;
            SyncElementState(element, layer);
        }

        private static void SyncElementState(VisualElement element, BackdropFilterLayer layer)
        {
            var rs = element.resolvedStyle;
            var style = layer.style;
            style.opacity = rs.opacity;
            style.visibility = rs.visibility;
            // A world-space panel draws no backdrop (styling-filters.md).
            style.display = BackdropRenderer.DrawnByCameras(element) ? DisplayStyle.None : rs.display;
            var origin = rs.transformOrigin;
            style.transformOrigin = new TransformOrigin(origin.x, origin.y, origin.z);
            style.translate = new Translate(rs.translate.x, rs.translate.y, rs.translate.z);
            style.scale = rs.scale;
            style.rotate = rs.rotate;
        }

        protected override void ImmediateRepaint()
        {
            // Should a sibling stand between the layer and the element, it would paint over the filtered backdrop.
            var host = parent;
            var next = host == null ? -1 : host.IndexOf(this) + 1;
            if (host == null || next >= host.childCount || host[next] != _element)
            {
                return;
            }
            var steps = Shown();
            if (steps.Length > 0)
            {
                BackdropRenderer.Draw(this, _element, steps);
            }
        }
    }
}
