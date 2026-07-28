using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one ringed element, keyed in ReconcilerContext.RingBindings by the
    // element itself. Holds the absolutely-positioned native-border overlay that paints the band, the
    // callbacks that keep it tracking the element (so they can be unregistered on detach), the latest class
    // list (the geometry sync re-resolves the corner radius from it) and the resolved spec.
    internal sealed class RingBinding
    {
        public readonly VisualElement Overlay;
        public string[] ClassNames = null!;
        public RingSpec Spec;
        public EventCallback<GeometryChangedEvent>? OnGeometry;
        public EventCallback<AttachToPanelEvent>? OnAttach;
        // Latches the "no element can host the overlay" warning so a per-frame geometry event cannot spam it.
        public bool HostWarningIssued;

        public RingBinding(VisualElement overlay) => Overlay = overlay;
    }

    /// <summary>
    /// Paints a <c>ring-*</c> / <c>outline-*</c> band as an absolutely-positioned native-border overlay that
    /// is a SIBLING of the ringed element — a reconciler-invisible trailing child of the element's parent,
    /// tracking the element's laid-out box.
    /// </summary>
    /// <remarks>
    /// UI Toolkit has neither <c>box-shadow</c> nor <c>outline</c>, so the band has to be drawn by Velvet. The
    /// two rejected alternatives are what shape this one:
    /// <para>
    /// A structural WRAPPER around the element (what this layer used to be) forwards only
    /// <c>flex-grow</c>/<c>flex-shrink</c>, so it alters every other layout relationship between the element
    /// and its real parent for the element's whole lifetime — and a ring behind a variant (<c>focus:ring-2</c>)
    /// would need that wrapper mounted permanently, since only a reconcile pass may add one. That cost is why
    /// a variant-applied ring used to be inert.
    /// </para>
    /// <para>
    /// Painting the band in the element's OWN <c>generateVisualContent</c> (the wrapper-less model
    /// <see cref="DropShadowSilhouette"/> uses) costs no layout at all, but was measured to lose the band
    /// entirely on an element that also carries <c>overflow: hidden</c>: an element's own overflow clip applies
    /// to its own generated content, not merely to its children. <c>overflow-hidden rounded-full ring-2</c> is
    /// the avatar pattern, so that is not an edge case. CSS clips neither an outline nor a box-shadow by the
    /// element's own overflow, so it would also have been a parity deviation.
    /// </para>
    /// <para>
    /// A sibling overlay has neither problem: the element's own layout relationship with its parent is
    /// untouched, and the overlay is outside the element's overflow clip while still inside an ANCESTOR's —
    /// which is what CSS does. Deviations it does carry: the overlay paints above the element's later
    /// siblings rather than in the element's own paint position, and <c>ring-inset</c> paints over an opaque
    /// full-bleed child rather than under it.
    /// </para>
    /// </remarks>
    internal static class RingOverlay
    {
        // Marks the overlay as a reconciler-invisible child of its host, recognized by
        // SilhouetteBoundsSpacer.IsSpacer — the single predicate every "real child" count and index site
        // already goes through, so the child reconciler, the structural variants, [&>*]: and the gap / grid /
        // divide manipulators all skip it without any of them learning about rings.
        internal const string MarkerClass = "velvet-ring-overlay";
        internal const string OverlayName = "velvet-ring-overlay";

        public static RingBinding Attach(VisualElement element, RingSpec spec, string[] classNames)
        {
            var overlay = new VisualElement
            {
                name = OverlayName,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, backgroundColor = Color.clear },
            };
            overlay.AddToClassList(MarkerClass);

            var binding = new RingBinding(overlay) { ClassNames = classNames, Spec = spec };
            ApplySpec(overlay, spec);

            binding.OnGeometry = _ => Place(element, binding);
            element.RegisterCallback(binding.OnGeometry);
            // A keyed move or a z-* relocation re-parents the element without moving its rect, so no geometry
            // event follows; the attach that ends such a move is the signal. Place re-derives the host every
            // time, so both callbacks are the same idempotent call.
            binding.OnAttach = _ => Place(element, binding);
            element.RegisterCallback(binding.OnAttach);

            Place(element, binding);
            return binding;
        }

        // Queues element for a placement retry at the top-level reconcile boundary, for the create path where
        // it has no parent yet (the factory hands the element to the caller to insert). An element that
        // already has a parent was placed by Attach and must NOT be queued: a variant re-sync attaches
        // outside any reconcile pass, so its entry might never be drained and the queue would grow with every
        // focus toggle.
        public static void RequestPlacement(ReconcilerContext ctx, VisualElement element)
        {
            if (element.parent != null)
            {
                return;
            }
            ctx.PendingRingPlacements.Add(element);
        }

        public static void DrainPendingPlacements(ReconcilerContext ctx)
        {
            if (ctx.PendingRingPlacements.Count == 0)
            {
                return;
            }
            foreach (var element in ctx.PendingRingPlacements)
            {
                // The binding may be gone already: the element could have been unmounted, or its ring
                // detached by a patch, between the create that queued it and this drain.
                if (ctx.RingBindings.TryGetValue(element, out var binding))
                {
                    Place(element, binding);
                }
            }
            ctx.PendingRingPlacements.Clear();
        }

        public static void Sync(VisualElement element, RingBinding binding, RingSpec spec, string[] classNames)
        {
            binding.Spec = spec;
            binding.ClassNames = classNames;
            ApplySpec(binding.Overlay, spec);
            Place(element, binding);
        }

        public static void Detach(VisualElement element, RingBinding binding)
        {
            if (binding.OnGeometry != null)
            {
                element.UnregisterCallback(binding.OnGeometry);
            }
            if (binding.OnAttach != null)
            {
                element.UnregisterCallback(binding.OnAttach);
            }
            // The overlay lives in the element's PARENT, so it does not leave with the element's own subtree
            // the way the old wrapper-hosted one did — a detach that skipped this would strand a live band.
            binding.Overlay.RemoveFromHierarchy();
        }

        // Native border on all four sides is the whole band; its geometry (position / size / radii) is set by
        // SyncGeometry once the element is laid out.
        private static void ApplySpec(VisualElement overlay, RingSpec spec)
        {
            overlay.style.borderTopWidth = spec.Width;
            overlay.style.borderRightWidth = spec.Width;
            overlay.style.borderBottomWidth = spec.Width;
            overlay.style.borderLeftWidth = spec.Width;
            overlay.style.borderTopColor = spec.Color;
            overlay.style.borderRightColor = spec.Color;
            overlay.style.borderBottomColor = spec.Color;
            overlay.style.borderLeftColor = spec.Color;
        }

        // Re-homes the overlay onto the element's CURRENT parent and re-sizes it. The host is re-derived on
        // every call rather than captured at attach: a keyed move, a z-* relocation into a layer container, or
        // an attach that had not happened yet at create time all change it, and each of those re-enters here.
        private static void Place(VisualElement element, RingBinding binding)
        {
            var host = element.parent;
            if (host == null)
            {
                // Before the factory inserts a freshly created element there is legitimately no host yet, and
                // the attach callback re-enters. A PANELLED element with no parent is the mount root itself,
                // which has no sibling slot the band could occupy — say so rather than rendering nothing
                // silently, which is the failure mode this whole layer exists to avoid.
                if (element.panel != null && !binding.HostWarningIssued)
                {
                    binding.HostWarningIssued = true;
                    FiberLogger.LogWarning("Ring",
                        "A ring-* / outline-* class on a panel's root element draws nothing: the band is "
                        + "hosted as a sibling of the ringed element, and a root has no sibling slot. "
                        + "Move the ring onto a child of the root.");
                }
                return;
            }

            if (!ReferenceEquals(binding.Overlay.parent, host))
            {
                binding.Overlay.RemoveFromHierarchy();
                host.Add(binding.Overlay);
            }

            SyncGeometry(element, binding, host);
        }

        // Sizes and positions the overlay over the element's laid-out box. Outset (the default): the band sits
        // OUTSIDE the element edge by Offset, so the overlay inflates by (Offset + Width) per side and each
        // outer corner radius is that corner's radius plus the same amount. Inset (ring-inset): the overlay
        // matches the element's box exactly, at the element's own radii.
        private static void SyncGeometry(VisualElement element, RingBinding binding, VisualElement host)
        {
            var overlay = binding.Overlay;
            var spec = binding.Spec;

            var width = element.resolvedStyle.width;
            var height = element.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                return; // pre-layout: the geometry callback re-enters with a real box
            }

            var originX = element.layout.x;
            var originY = element.layout.y;
            if (float.IsNaN(originX)) originX = 0f;
            if (float.IsNaN(originY)) originY = 0f;

            // element.layout is border-box-relative to the host while an absolute child's inline left/top
            // resolve against the host's PADDING box, so the host's border is subtracted here — the same
            // correction SilhouetteBoundsSpacer.ShiftToPaddingBox applies for the filter bounds-spacer. Read
            // off the host's resolved style rather than its class list because this only ever runs once the
            // element has a laid-out box, by which point the host's border has resolved too.
            var hostBorderLeft = host.resolvedStyle.borderLeftWidth;
            var hostBorderTop = host.resolvedStyle.borderTopWidth;
            if (float.IsNaN(hostBorderLeft)) hostBorderLeft = 0f;
            if (float.IsNaN(hostBorderTop)) hostBorderTop = 0f;

            var grow = spec.Inset ? 0f : spec.Offset + spec.Width;
            overlay.style.left = originX - grow - hostBorderLeft;
            overlay.style.top = originY - grow - hostBorderTop;
            overlay.style.width = width + (grow * 2f);
            overlay.style.height = height + (grow * 2f);

            var outerGrow = spec.Inset ? 0f : spec.Offset + spec.Width;
            overlay.style.borderTopLeftRadius = ResolveRadius(element, binding, RingCorner.TopLeft) + outerGrow;
            overlay.style.borderTopRightRadius = ResolveRadius(element, binding, RingCorner.TopRight) + outerGrow;
            overlay.style.borderBottomRightRadius = ResolveRadius(element, binding, RingCorner.BottomRight) + outerGrow;
            overlay.style.borderBottomLeftRadius = ResolveRadius(element, binding, RingCorner.BottomLeft) + outerGrow;
        }

        private enum RingCorner { TopLeft, TopRight, BottomRight, BottomLeft }

        // One corner's radius, preferring the laid-out resolved value (which handles %, arbitrary and inline
        // radii) and falling back to the rounded-* class scale. The `> 0f` test is deliberate: a USS rounded-*
        // class does not always reflect into resolvedStyle off-screen, reading 0 there, so a resolved 0 must
        // fall back to the class scale rather than be trusted as "not rounded" — else a rounded card would
        // wear a square ring. A genuinely unrounded element resolves 0 here and also misses the class scale,
        // landing at 0 correctly.
        private static float ResolveRadius(VisualElement element, RingBinding binding, RingCorner corner)
        {
            var rs = element.resolvedStyle;
            var resolved = corner switch
            {
                RingCorner.TopLeft => rs.borderTopLeftRadius,
                RingCorner.TopRight => rs.borderTopRightRadius,
                RingCorner.BottomRight => rs.borderBottomRightRadius,
                _ => rs.borderBottomLeftRadius,
            };
            if (element.panel != null && !float.IsNaN(resolved) && resolved > 0f)
            {
                return resolved;
            }
            return StyleRingClass.TryResolveCornerRadius(binding.ClassNames, out var classRadius) ? classRadius : 0f;
        }
    }
}
