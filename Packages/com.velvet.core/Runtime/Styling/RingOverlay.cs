using System.Runtime.CompilerServices;
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
        // Covers the element's box and carries its transform, opacity, visibility and display; the bands are its
        // children, in CSS's paint order: the ring's box-shadow, the offset fill over it, the outline over both.
        public readonly VisualElement Overlay;
        public readonly VisualElement RingElement;
        public readonly VisualElement OffsetElement;
        public readonly VisualElement OutlineElement;
        public string[] ClassNames = null!;
        public RingSpec Spec;
        public EventCallback<GeometryChangedEvent>? OnGeometry;
        public EventCallback<AttachToPanelEvent>? OnAttach;
        public IVisualElementScheduledItem? StateTick;

        public RingBinding(VisualElement overlay, VisualElement ringElement, VisualElement offsetElement, VisualElement outlineElement)
        {
            Overlay = overlay;
            RingElement = ringElement;
            OffsetElement = offsetElement;
            OutlineElement = outlineElement;
        }
    }

    /// <summary>
    /// Paints a <c>ring-*</c> / <c>outline-*</c> band as an absolutely-positioned native-border overlay that
    /// is a SIBLING of the ringed element — a reconciler-invisible child of the element's parent placed
    /// directly after it, tracking the element's laid-out box.
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
    /// which is what CSS does. It gets the element's own paint position from child adjacency, because UI
    /// Toolkit paints an absolutely-positioned sibling in child order against an in-flow one rather than
    /// lifting positioned elements above the in-flow ones as CSS does; that engine fact is measured by
    /// pixel readback in <c>RingSiblingPaintOrderPlaybackTests</c>, and the whole placement rests on it.
    /// The deviation it carries: <c>ring-inset</c> paints over an opaque full-bleed child rather than under
    /// it. A transform, an opacity, a visibility or a display on the ringed element reaches that element's own
    /// subtree only, which the band is not in, so the band is given each of them itself
    /// (<c>SyncElementState</c>).
    /// </para>
    /// </remarks>
    internal static class RingOverlay
    {
        // Lets the animation scheduler find a ring binding on the element it is animating. The band is a
        // SIBLING of that element, so UI Toolkit's opacity compositing — which reaches every overlay
        // belonging to a descendant — does not reach this one, and only an explicit co-fade can fade it with
        // its element. Auto-drops entries when an element is GC'd; Detach removes eagerly so a pooled element
        // cannot ghost a prior consumer's band. Mirrors DropShadowSilhouette's per-element side-channel.
        private static readonly ConditionalWeakTable<VisualElement, RingBinding> s_byElement = new();

        public static RingBinding? TryGet(VisualElement element)
            => s_byElement.TryGetValue(element, out var binding) ? binding : null;

        // Marks the overlay as a reconciler-invisible child of its host, recognized by
        // SilhouetteBoundsSpacer.IsSpacer — the single predicate every "real child" count and index site
        // already goes through, so the child reconciler, the structural variants, [&>*]: and the gap / grid /
        // divide manipulators all skip it without any of them learning about rings.
        internal const string MarkerClass = "velvet-ring-overlay";
        internal const string OverlayName = "velvet-ring-overlay";
        internal const string RingBandName = "velvet-ring-band";
        internal const string OffsetBandName = "velvet-ring-offset-band";
        internal const string OutlineBandName = "velvet-outline-band";

        private static VisualElement NewBand(VisualElement overlay, string name)
        {
            var band = new VisualElement
            {
                name = name,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, backgroundColor = Color.clear },
            };
            overlay.Add(band);
            return band;
        }

        public static RingBinding Attach(VisualElement element, RingSpec spec, string[] classNames)
        {
            var overlay = new VisualElement
            {
                name = OverlayName,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, backgroundColor = Color.clear },
            };
            overlay.AddToClassList(MarkerClass);

            var binding = new RingBinding(overlay, NewBand(overlay, RingBandName), NewBand(overlay, OffsetBandName),
                NewBand(overlay, OutlineBandName)) { ClassNames = classNames, Spec = spec };
            ApplySpec(binding, element);

            binding.OnGeometry = _ => Place(element, binding);
            element.RegisterCallback(binding.OnGeometry);
            // A keyed move or a z-* relocation re-parents the element without moving its rect, so no geometry
            // event follows; the attach that ends such a move is the signal. Place re-derives the host every
            // time, so both callbacks are the same idempotent call.
            binding.OnAttach = _ => Place(element, binding);
            element.RegisterCallback(binding.OnAttach);

            // The band samples its element's resolved state once per frame, which carries an animated value
            // onto it as well as the one Place copies. Scheduled on the element, so it pauses while the
            // element is off a panel.
            binding.StateTick = element.schedule.Execute(() => SyncElementState(element, binding))
                .Every(StyleAnimateDriver.TickMs);

            s_byElement.AddOrUpdate(element, binding);
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
            ApplySpec(binding, element);
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
            binding.StateTick?.Pause();
            // The overlay lives in the element's PARENT, so it does not leave with the element's own subtree
            // the way the old wrapper-hosted one did — a detach that skipped this would strand a live band.
            binding.Overlay.RemoveFromHierarchy();
            s_byElement.Remove(element);
        }

        // A native border on all four sides is the whole of each band; its geometry (position / size / radii) is
        // set by SyncGeometry once the element is laid out. The ring's border spans its offset as well as its
        // width, as Tailwind's ring spread does, so a translucent offset fill shows the ring through it.
        private static void ApplySpec(RingBinding binding, VisualElement element)
        {
            var spec = binding.Spec;
            var text = element.resolvedStyle.color;
            ApplyBand(binding.RingElement, spec.RingSpread, spec.Ring.CurrentColor ? text : spec.Ring.Color);
            ApplyBand(binding.OffsetElement, spec.OffsetSpread, spec.OffsetColor);
            ApplyBand(binding.OutlineElement, spec.Outline.Width, spec.Outline.CurrentColor ? text : spec.Outline.Color);
        }

        private static void ApplyBand(VisualElement band, float width, Color color)
        {
            band.style.display = width > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            band.style.borderTopWidth = width;
            band.style.borderRightWidth = width;
            band.style.borderBottomWidth = width;
            band.style.borderLeftWidth = width;
            ApplyColor(band, color);
        }

        private static void ApplyColor(VisualElement band, Color color)
        {
            band.style.borderTopColor = color;
            band.style.borderRightColor = color;
            band.style.borderBottomColor = color;
            band.style.borderLeftColor = color;
        }

        // Re-homes the overlay onto the element's CURRENT parent and re-sizes it. The host is re-derived on
        // every call rather than captured at attach: a keyed move, a z-* relocation into a layer container, or
        // an attach that had not happened yet at create time all change it, and each of those re-enters here.
        private static void Place(VisualElement element, RingBinding binding)
        {
            var host = element.parent;
            if (host == null)
            {
                // Legitimately parentless between the factory building the element and the caller inserting
                // it; the placement drain and the attach callback both re-enter after that.
                return;
            }

            // Directly AFTER the element, re-derived on every sync. Paint order in UI Toolkit is child
            // order, so this is what makes a band occlude its OWN element without also covering the
            // siblings that follow it — `flex -space-x-4` avatars each carrying `ring-2 ring-white` rely on
            // the next avatar's face hiding the previous one's band. A trailing run gave every band to every
            // later sibling, and ordered the bands among themselves by attach order, so two `focus:ring-2`
            // siblings painted in whichever order they were focused.
            //
            // Adjacency is only viable because the reconciler addresses LOGICAL slots and converts at each
            // DOM touch (LogicalChildSlots): an interleaved invisible child is skipped rather than counted.
            // Against the older trailing-only machinery this desynced slot indexing outright.
            //
            // Re-placing on every sync, not just when the host changes: an insert or a keyed move among the
            // siblings shifts the element without notifying this binding.
            if (host.IndexOf(binding.Overlay) != host.IndexOf(element) + 1)
            {
                // Re-read the element's index AFTER the removal — an overlay currently sitting before it
                // shifts the element down one when it leaves.
                binding.Overlay.RemoveFromHierarchy();
                host.Insert(host.IndexOf(element) + 1, binding.Overlay);
            }

            SyncGeometry(element, binding, host);
            SyncElementState(element, binding);
        }

        // Sets the overlay over the element's laid-out box, and each band around it. Outset, a band's box inflates
        // by the band's spread per side (an outline's offset plus its width) and each outer corner radius is that
        // corner's radius plus the same amount. Inset (ring-inset, which takes the ring and its offset fill and
        // not the outline), a band matches the element's box at the element's own radii.
        private static void SyncGeometry(VisualElement element, RingBinding binding, VisualElement host)
        {
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

            var overlay = binding.Overlay.style;
            overlay.left = originX - hostBorderLeft;
            overlay.top = originY - hostBorderTop;
            overlay.width = width;
            overlay.height = height;

            ResolveRadii(element, binding, out var radii);
            PlaceBand(binding.RingElement, spec.Inset ? 0f : spec.RingSpread, width, height, radii);
            PlaceBand(binding.OffsetElement, spec.Inset ? 0f : spec.OffsetSpread, width, height, radii);
            PlaceBand(binding.OutlineElement, spec.Outline.Offset + spec.Outline.Width, width, height, radii);
        }

        // Positions a band against the overlay, whose box is the element's.
        private static void PlaceBand(VisualElement band, float grow, float width, float height, Vector4 radii)
        {
            var style = band.style;
            style.left = -grow;
            style.top = -grow;
            // A negative outline offset shrinks the band's box, which cannot go below nothing.
            style.width = Mathf.Max(0f, width + (grow * 2f));
            style.height = Mathf.Max(0f, height + (grow * 2f));
            style.borderTopLeftRadius = Mathf.Max(0f, radii.x + grow);
            style.borderTopRightRadius = Mathf.Max(0f, radii.y + grow);
            style.borderBottomRightRadius = Mathf.Max(0f, radii.z + grow);
            style.borderBottomLeftRadius = Mathf.Max(0f, radii.w + grow);
        }

        // Gives the bands what the element's own subtree would have given them had they been inside: its opacity,
        // visibility and display, and its transform, pivoting where the element pivots. The overlay's box is the
        // element's, so the bands, which reach outside it, turn about the element's pivot rather than their own.
        private static void SyncElementState(VisualElement element, RingBinding binding)
        {
            var rs = element.resolvedStyle;
            var style = binding.Overlay.style;
            // A band in the element's text colour follows it as it changes, as currentcolor does.
            if (binding.Spec.Ring.CurrentColor)
            {
                ApplyColor(binding.RingElement, rs.color);
            }
            if (binding.Spec.Outline.CurrentColor)
            {
                ApplyColor(binding.OutlineElement, rs.color);
            }
            style.opacity = rs.opacity;
            style.visibility = rs.visibility;
            style.display = rs.display;
            if (float.IsNaN(rs.width))
            {
                return; // pre-layout: a percentage pivot has no box to resolve against yet
            }
            var origin = rs.transformOrigin;
            var translate = rs.translate;
            style.transformOrigin = new TransformOrigin(origin.x, origin.y, origin.z);
            style.translate = new Translate(translate.x, translate.y, translate.z);
            style.scale = rs.scale;
            style.rotate = rs.rotate;
        }

        // The four corner radii the band rounds to. Laid out, the resolved values are authoritative per
        // corner — including a resolved 0, which is what a card rounded on one corner only has on its other
        // three. The class-scale fallback is taken ONLY when no corner resolved anything, which is the
        // pre-layout / off-screen case where a USS rounded-* does not reflect into resolvedStyle at all; a
        // per-corner resolved 0 must not reach it, because the fallback answers for the whole element (it
        // takes the top-left as representative, see StyleShadowClass.TryResolveCornerRadius) and would round
        // all four corners of a `rounded-tl-lg` band. That fallback stays whole-element, so a per-corner
        // class is approximated until the first geometry event replaces it with the resolved values.
        // radii is top-left, top-right, bottom-right, bottom-left as x, y, z, w.
        private static void ResolveRadii(VisualElement element, RingBinding binding, out Vector4 radii)
        {
            var rs = element.resolvedStyle;
            radii = new Vector4(Sane(rs.borderTopLeftRadius), Sane(rs.borderTopRightRadius),
                Sane(rs.borderBottomRightRadius), Sane(rs.borderBottomLeftRadius));
            if (element.panel != null && (radii.x > 0f || radii.y > 0f || radii.z > 0f || radii.w > 0f))
            {
                return;
            }
            if (StyleRingClass.TryResolveCornerRadius(binding.ClassNames, out var classRadius))
            {
                radii = new Vector4(classRadius, classRadius, classRadius, classRadius);
            }
        }

        private static float Sane(float v) => float.IsNaN(v) ? 0f : v;
    }
}
