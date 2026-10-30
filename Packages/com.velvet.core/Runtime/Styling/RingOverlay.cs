using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one ringed element, keyed in ReconcilerContext.RingBindings by the
    // element itself. Holds the overlays that paint the bands, the callbacks that keep them tracking the element
    // (so they can be unregistered on detach), the latest class list (the geometry sync re-resolves the corner
    // radius from it) and the resolved spec.
    internal sealed class RingBinding
    {
        // Each overlay covers the element's box and carries its transform, opacity, visibility and display; the
        // bands are their children. Overlay holds the ring under its offset fill, after the element or, for an
        // inset ring, as its first child; OutlineOverlay sits where the outline paints over the content around it
        // (OutlineHosting).
        public readonly VisualElement Overlay;
        public readonly VisualElement RingElement;
        public readonly VisualElement OffsetElement;
        public readonly VisualElement OutlineOverlay;
        public readonly VisualElement OutlineElement;
        public string[] ClassNames = null!;
        public RingSpec Spec;
        // True while the overlay is configured as the element's own first child (an inset band) rather than as
        // its following sibling; RingOverlay.SetMode is the only writer.
        public bool Inside;
        // The element's size and its corner radii fitted to it, as the last geometry sync read them; for an inset
        // band, the padding box's radii the last tick read.
        public float Width;
        public float Height;
        public CornerRadii Radii;
        public CornerRadii InsideRadii;
        // The colour each band is painted in this frame: a band naming no colour takes the element's text colour.
        public Color RingTint;
        public Color OutlineTint;
        public Color OffsetTint;
        // The clip proxies standing in for the outline's clipping ancestors, outermost first (OutlineHosting), and
        // what the last placement read: the host, the clipping ancestors and the boxes and masks the proxies copied,
        // the element's offset in the host and its size, and the panel's hierarchy version.
        public readonly List<VisualElement> OutlineClips = new();
        public readonly List<VisualElement> OutlineClipOwners = new();
        public readonly List<(Rect Layout, object? Mask)> OutlineClipBoxes = new();
        public VisualElement? OutlineHost;
        public Vector2 OutlineOffset;
        public Vector2 OutlineSize;
        public uint? OutlineVersion;
        public EventCallback<GeometryChangedEvent>? OnGeometry;
        public EventCallback<AttachToPanelEvent>? OnAttach;
        public IVisualElementScheduledItem? StateTick;

        public RingBinding(VisualElement overlay, VisualElement ringElement, VisualElement offsetElement,
            VisualElement outlineOverlay, VisualElement outlineElement)
        {
            Overlay = overlay;
            RingElement = ringElement;
            OffsetElement = offsetElement;
            OutlineOverlay = outlineOverlay;
            OutlineElement = outlineElement;
        }
    }

    /// <summary>
    /// Paints the <c>ring-*</c>, <c>ring-offset-*</c> and <c>outline-*</c> bands of an element.
    /// </summary>
    /// <remarks>
    /// UI Toolkit has neither <c>box-shadow</c> nor <c>outline</c>, so Velvet draws each band as a rounded rect with
    /// a rounded hole (<see cref="RingBandShape"/>), and where it is drawn follows where CSS paints it. The two
    /// rejected alternatives are what shape the ring's host:
    /// <para>
    /// A structural WRAPPER around the element (what this layer used to be) forwards only
    /// <c>flex-grow</c>/<c>flex-shrink</c>, so it alters every other layout relationship between the element
    /// and its real parent for the element's whole lifetime — and a ring behind a variant (<c>focus:ring-2</c>)
    /// would need that wrapper mounted permanently, since only a reconcile pass may add one. That cost is why
    /// a variant-applied ring used to be inert.
    /// </para>
    /// <para>
    /// Painting the band in the element's OWN <c>generateVisualContent</c> (the wrapper-less model
    /// <see cref="BoxShadowSilhouette"/> uses) costs no layout at all, but was measured to lose the band
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
    /// lifting positioned elements above the in-flow ones as CSS does; that engine fact is measured by pixel
    /// readback in <c>RingSiblingPaintOrderPlaybackTests</c>, and the whole placement rests on it.
    /// A transform, an opacity, a visibility or a display on the ringed element reaches that element's own
    /// subtree only, which the band is not in, so the band is given each of them itself
    /// (<c>SyncElementState</c>).
    /// </para>
    /// <para>
    /// <c>ring-inset</c> is the exception to all of that. An inset band is CSS's inset box-shadow, which paints
    /// above the element's own background and under its children, so it is the element's own first child
    /// (<see cref="InsetMarkerClass"/>) stretched over the padding box, and no sibling at all. A child needs
    /// none of the state copying above, and would show it twice if given it (<c>SetMode</c>).
    /// </para>
    /// <para>
    /// An outline paints after all the content of its stacking context, so it is drawn on a second overlay that
    /// <see cref="OutlineHosting"/> places at the end of that context, behind clip proxies for the ancestors whose
    /// overflow clips it.
    /// </para>
    /// </remarks>
    internal static class RingOverlay
    {
        // Lets the animation scheduler find a ring binding on the element it is animating. The overlays are
        // outside that element's subtree, so UI Toolkit's opacity compositing — which reaches every overlay
        // belonging to a descendant — does not reach them, and only an explicit co-fade can fade them with their
        // element. Auto-drops entries when an element is GC'd; Detach removes eagerly so a pooled element
        // cannot ghost a prior consumer's band. Mirrors BoxShadowSilhouette's per-element side-channel.
        private static readonly ConditionalWeakTable<VisualElement, RingBinding> s_byElement = new();

        public static RingBinding? TryGet(VisualElement element)
            => s_byElement.TryGetValue(element, out var binding) ? binding : null;

        // Marks an overlay as a reconciler-invisible child of its host, recognized by
        // SilhouetteBoundsSpacer.IsSpacer — the single predicate every "real child" count and index site
        // already goes through, so the child reconciler, the structural variants, [&>*]: and the gap / grid /
        // divide manipulators all skip it without any of them learning about rings.
        internal const string MarkerClass = "velvet-ring-overlay";
        internal const string OutlineMarkerClass = "velvet-outline-overlay";
        internal const string OverlayName = "velvet-ring-overlay";
        internal const string OutlineOverlayName = "velvet-outline-overlay";
        internal const string RingBandName = "velvet-ring-band";
        internal const string OffsetBandName = "velvet-ring-offset-band";
        internal const string OutlineBandName = "velvet-outline-band";

        // The marker of an inset band, which is the element's own first child rather than its following sibling.
        // A separate class from MarkerClass because the two sit in different places: LogicalChildSlots must let
        // an append step over a leading one and stop before a following one, and FiberZLayerCoordinator keeps a
        // leading one first where it keeps a following one behind its element.
        internal const string InsetMarkerClass = "velvet-ring-inset-overlay";

        private static VisualElement NewOverlay(string name)
        {
            var overlay = new VisualElement
            {
                name = name,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, backgroundColor = Color.clear },
            };
            overlay.AddToClassList(MarkerClass);
            return overlay;
        }

        private static VisualElement NewBand(VisualElement overlay, string name, Action<MeshGenerationContext> paint)
        {
            var band = new VisualElement
            {
                name = name,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute },
            };
            band.generateVisualContent += paint;
            overlay.Add(band);
            return band;
        }

        public static RingBinding Attach(VisualElement element, RingSpec spec, string[] classNames)
        {
            var overlay = NewOverlay(OverlayName);
            var outlineOverlay = NewOverlay(OutlineOverlayName);
            outlineOverlay.AddToClassList(OutlineMarkerClass);
            RingBinding binding = null!;
            binding = new RingBinding(overlay,
                NewBand(overlay, RingBandName, mgc => PaintRing(mgc, binding, binding.Spec.RingSpread, binding.RingTint)),
                NewBand(overlay, OffsetBandName, mgc => PaintRing(mgc, binding, binding.Spec.OffsetSpread, binding.OffsetTint)),
                outlineOverlay,
                NewBand(outlineOverlay, OutlineBandName, mgc => PaintOutline(mgc, binding)))
            {
                ClassNames = classNames,
                Spec = spec,
            };
            SetMode(binding);
            ApplySpec(binding, element);

            binding.OnGeometry = _ => Place(element, binding);
            element.RegisterCallback(binding.OnGeometry);
            // A keyed move or a z-* relocation re-parents the element without moving its rect, so no geometry
            // event follows; the attach that ends such a move is the signal. Place re-derives the hosts every
            // time, so both callbacks are the same idempotent call.
            binding.OnAttach = _ => Place(element, binding);
            element.RegisterCallback(binding.OnAttach);

            // The bands sample their element's resolved state once per frame, which carries an animated value
            // onto them as well as the one Place copies, and re-place the outline, whose host an ancestor's
            // change can move without telling the element. Scheduled on the element, so it pauses while the
            // element is off a panel.
            binding.StateTick = element.schedule.Execute(() =>
            {
                OutlineHosting.Place(element, binding, force: false);
                SyncElementState(element, binding);
            }).Every(StyleAnimateDriver.TickMs);

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

        // Places element's band again, after a face border overlay came or went beside it.
        internal static void Replace(VisualElement element)
        {
            if (s_byElement.TryGetValue(element, out var binding))
            {
                Place(element, binding);
            }
        }

        public static void Sync(VisualElement element, RingBinding binding, RingSpec spec, string[] classNames)
        {
            binding.Spec = spec;
            binding.ClassNames = classNames;
            SetMode(binding);
            ApplySpec(binding, element);
            Place(element, binding);
        }

        // Moves the overlay between the two roles its spec can ask for. Leaving the inside role hands the
        // band back to the sibling path, which writes the inline values it needs; entering it clears the
        // inline opacity, visibility, display and transform that path copied from the element, because the
        // band now receives each of them from the element as its child and would show it twice.
        private static void SetMode(RingBinding binding)
        {
            var inside = binding.Spec.PaintsInside;
            if (binding.Inside == inside)
            {
                return;
            }
            binding.Inside = inside;
            var overlay = binding.Overlay;
            overlay.EnableInClassList(MarkerClass, !inside);
            overlay.EnableInClassList(InsetMarkerClass, inside);
            if (!inside)
            {
                return;
            }
            var style = overlay.style;
            style.opacity = StyleKeyword.Null;
            style.visibility = StyleKeyword.Null;
            style.display = StyleKeyword.Null;
            style.transformOrigin = StyleKeyword.Null;
            style.translate = StyleKeyword.Null;
            style.scale = StyleKeyword.Null;
            style.rotate = StyleKeyword.Null;
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
            // The overlays live in the element's PARENT and in the outline's host rather than in its own subtree,
            // so they do not leave with it — a detach that skipped this would strand a live band.
            binding.Overlay.RemoveFromHierarchy();
            OutlineHosting.Remove(binding);
            s_byElement.Remove(element);
        }

        // Which bands are shown. Geometry follows in SyncGeometry once the element is laid out.
        private static void ApplySpec(RingBinding binding, VisualElement element)
        {
            var spec = binding.Spec;
            Show(binding.RingElement, spec.RingSpread > 0f);
            Show(binding.OffsetElement, spec.OffsetSpread > 0f);
            Show(binding.OutlineElement, spec.Outline.Width > 0f);
            ResolveTints(element, binding);
            MarkBandsDirty(binding);
        }

        private static void Show(VisualElement band, bool shown)
            => band.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

        private static void MarkBandsDirty(RingBinding binding)
        {
            binding.RingElement.MarkDirtyRepaint();
            binding.OffsetElement.MarkDirtyRepaint();
            binding.OutlineElement.MarkDirtyRepaint();
        }

        // The tint of each band naming no colour follows the element's text colour, as currentcolor does. Returns
        // whether one changed, which is what repaints it.
        private static bool ResolveTints(VisualElement element, RingBinding binding)
        {
            var spec = binding.Spec;
            var text = element.resolvedStyle.color;
            var ring = TintOf(element, spec.Ring, BandColourInheritance.Family.Ring, text);
            var outline = TintOf(element, spec.Outline, BandColourInheritance.Family.Outline, text);
            var offset = spec.OffsetSource switch
            {
                ColorReference.Current => text,
                ColorReference.Inherit => BandColourInheritance.Resolve(element, BandColourInheritance.Family.Offset),
                _ => spec.OffsetColor,
            };
            var changed = ring != binding.RingTint || outline != binding.OutlineTint || offset != binding.OffsetTint;
            (binding.RingTint, binding.OutlineTint, binding.OffsetTint) = (ring, outline, offset);
            return changed;
        }

        private static Color TintOf(VisualElement element, in RingBand band, BandColourInheritance.Family family, Color text)
            => band.InheritColor ? BandColourInheritance.Resolve(element, family) : band.CurrentColor ? text : band.Color;

        // Re-homes both overlays and re-sizes them. The hosts are re-derived on every call rather than captured at
        // attach: a keyed move, a z-* relocation into a layer container, or an attach that had not happened yet at
        // create time all change them, and each of those re-enters here.
        private static void Place(VisualElement element, RingBinding binding)
        {
            if (binding.Inside)
            {
                PlaceInside(element, binding);
            }
            else if (!PlaceBeside(element, binding))
            {
                return;
            }
            OutlineHosting.Place(element, binding, force: true);
            SyncElementState(element, binding);
        }

        // False while the element has no parent to sit beside.
        private static bool PlaceBeside(VisualElement element, RingBinding binding)
        {
            var host = element.parent;
            if (host == null)
            {
                // Legitimately parentless between the factory building the element and the caller inserting
                // it; the placement drain and the attach callback both re-enter after that.
                return false;
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
            // A face border overlay (FaceBorderOverlay) sits between the element and its band.
            var after = FaceBorderOverlay.Has(element) ? 2 : 1;
            if (host.IndexOf(binding.Overlay) != host.IndexOf(element) + after)
            {
                // Re-read the element's index AFTER the removal — an overlay currently sitting before it
                // shifts the element down one when it leaves.
                binding.Overlay.RemoveFromHierarchy();
                host.Insert(Mathf.Min(host.IndexOf(element) + after, host.childCount), binding.Overlay);
            }

            SyncGeometry(element, binding, host);
            return true;
        }

        // Keeps the inset band the element's first child, ahead of every rendered child so that it paints under
        // them. Goes through hierarchy rather than the element itself so a ScrollView takes the band as its own
        // child and not as one of its content. Needs no parent and no layout: the band stretches over the padding
        // box by its offsets alone, so only its corner radii wait on a resolved style.
        private static void PlaceInside(VisualElement element, RingBinding binding)
        {
            var overlay = binding.Overlay;
            if (overlay.parent != element || element.hierarchy.IndexOf(overlay) != 0)
            {
                overlay.RemoveFromHierarchy();
                element.hierarchy.Insert(0, overlay);
            }

            Stretch(overlay);
            Stretch(binding.RingElement);
            Stretch(binding.OffsetElement);
            SyncInsideRadii(element, binding);
        }

        private static void Stretch(VisualElement e)
        {
            var style = e.style;
            style.left = 0f;
            style.top = 0f;
            style.right = 0f;
            style.bottom = 0f;
            style.width = StyleKeyword.Null;
            style.height = StyleKeyword.Null;
        }

        // The padding box's corners are the element's less the border beside them on each axis
        // (RingBandShape.PaddingRadii). Read on the state tick as well as on a placement, since a border that changes
        // alone fires no geometry event.
        private static void SyncInsideRadii(VisualElement element, RingBinding binding)
        {
            var rs = element.resolvedStyle;
            var width = element.layout.width;
            var height = element.layout.height;
            var outer = float.IsNaN(width) || float.IsNaN(height)
                ? binding.Radii
                : BorderBandPainter.Fitted(ResolveRadii(element, binding), width, height);
            var radii = RingBandShape.PaddingRadii(outer, Sane(rs.borderLeftWidth), Sane(rs.borderTopWidth),
                Sane(rs.borderRightWidth), Sane(rs.borderBottomWidth));
            if (!Same(radii, binding.InsideRadii))
            {
                binding.InsideRadii = radii;
                MarkBandsDirty(binding);
            }
        }

        // The overlay over the element's laid-out box, and each band around it: a band's box is the element's grown
        // by its spread, which is where it paints.
        private static void SyncGeometry(VisualElement element, RingBinding binding, VisualElement host)
        {
            var width = element.resolvedStyle.width;
            var height = element.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                return; // pre-layout: the geometry callback re-enters with a real box
            }
            binding.Width = width;
            binding.Height = height;
            binding.Radii = BorderBandPainter.Fitted(ResolveRadii(element, binding), width, height);

            if (!OutlineHosting.TryOffsetWithin(element, host, out var x, out var y))
            {
                return;
            }
            OutlineHosting.SetBox(binding.Overlay, x, y, width, height);

            var spec = binding.Spec;
            PlaceBand(binding.RingElement, spec.RingSpread, binding);
            PlaceBand(binding.OffsetElement, spec.OffsetSpread, binding);
            PlaceBand(binding.OutlineElement, spec.Outline.Offset + spec.Outline.Width, binding);
            MarkBandsDirty(binding);
        }

        // A band's box is the element's grown by grow.
        private static void PlaceBand(VisualElement band, float grow, RingBinding binding)
            => OutlineHosting.SetBox(band, -grow, -grow, binding.Width + (grow * 2f), binding.Height + (grow * 2f));

        // Gives both overlays what the element's own subtree would have given them had they been inside: its
        // opacity, visibility and display, and its transform, pivoting where the element pivots. Each overlay's box
        // is the element's, so the bands, which reach outside it, turn about the element's pivot. An inset band is the
        // element's child and receives all of that from it.
        private static void SyncElementState(VisualElement element, RingBinding binding)
        {
            if (ResolveTints(element, binding))
            {
                MarkBandsDirty(binding);
            }
            if (binding.Inside)
            {
                SyncInsideRadii(element, binding);
            }
            else
            {
                SyncProjectedRadii(element, binding);
                CopyState(element, binding.Overlay.style);
            }
            CopyState(element, binding.OutlineOverlay.style);
        }

        private static void CopyState(VisualElement element, IStyle style)
        {
            var rs = element.resolvedStyle;
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

        // While a layoutId projection paints the element with a radius per axis (ProjectedCornerFace), the bands are
        // painted with the same radii, which a band's own corner reads from the element's.
        private static void SyncProjectedRadii(VisualElement element, RingBinding binding)
        {
            if (binding.Width <= 0f || !ProjectedCornerFace.TryGetRadii(element, out var projected))
            {
                return;
            }
            var radii = BorderBandPainter.Fitted(projected, binding.Width, binding.Height);
            if (!Same(radii, binding.Radii))
            {
                binding.Radii = radii;
                MarkBandsDirty(binding);
            }
        }

        // The ring or its offset fill: an outset box-shadow band, the element's box grown by spread less the
        // element's box, its outer corners rounded as RingBandShape.SpreadRadius gives them; inside the padding box
        // for an inset ring, the padding box less the padding box shrunk by spread.
        private static void PaintRing(MeshGenerationContext mgc, RingBinding binding, float spread, Color tint)
        {
            if (spread <= 0f)
            {
                return;
            }
            if (binding.Inside)
            {
                var padding = mgc.visualElement.contentRect;
                var hole = new Rect(spread, spread, padding.width - (spread * 2f), padding.height - (spread * 2f));
                RingBandShape.Fill(mgc.painter2D, new Rect(0f, 0f, padding.width, padding.height), binding.InsideRadii, hole,
                    RingBandShape.Shrink(binding.InsideRadii, spread), tint);
                return;
            }
            if (binding.Width <= 0f)
            {
                return;
            }
            var outer = new Rect(0f, 0f, binding.Width + (spread * 2f), binding.Height + (spread * 2f));
            RingBandShape.Fill(mgc.painter2D, outer, RingBandShape.Spread(binding.Radii, spread),
                new Rect(spread, spread, binding.Width, binding.Height), binding.Radii, tint);
        }

        // An outline: Width wide, Offset past the border edge, which may be negative.
        private static void PaintOutline(MeshGenerationContext mgc, RingBinding binding)
        {
            var outline = binding.Spec.Outline;
            if (outline.Width <= 0f || binding.Width <= 0f)
            {
                return;
            }
            var grow = outline.Offset + outline.Width;
            var outer = new Rect(0f, 0f, binding.Width + (grow * 2f), binding.Height + (grow * 2f));
            var hole = new Rect(outline.Width, outline.Width, binding.Width + (outline.Offset * 2f),
                binding.Height + (outline.Offset * 2f));
            RingBandShape.Fill(mgc.painter2D, outer, RingBandShape.Outline(binding.Radii, grow), hole,
                RingBandShape.Outline(binding.Radii, outline.Offset), binding.OutlineTint);
        }

        // The element's own corner radii. Laid out, the resolved values are authoritative per corner — including a
        // resolved 0, which is what a card rounded on one corner only has on its other three. The class-scale
        // fallback is taken ONLY when no corner resolved anything, which is the pre-layout / off-screen case where a
        // USS rounded-* does not reflect into resolvedStyle at all; a per-corner resolved 0 must not reach it,
        // because the fallback answers for the whole element (it takes the top-left as representative, see
        // StyleShadowClass.TryResolveCornerRadius) and would round all four corners of a `rounded-tl-lg` band. That
        // fallback stays whole-element, so a per-corner class is approximated until the first geometry event replaces
        // it with the resolved values.
        private static CornerRadii ResolveRadii(VisualElement element, RingBinding binding)
        {
            if (ProjectedCornerFace.TryGetRadii(element, out var projected))
            {
                return projected;
            }
            var rs = element.resolvedStyle;
            var tl = Sane(rs.borderTopLeftRadius);
            var tr = Sane(rs.borderTopRightRadius);
            var br = Sane(rs.borderBottomRightRadius);
            var bl = Sane(rs.borderBottomLeftRadius);
            if (!(element.panel != null && (tl > 0f || tr > 0f || br > 0f || bl > 0f))
                && StyleRingClass.TryResolveCornerRadius(binding.ClassNames, out var classRadius))
            {
                tl = tr = br = bl = classRadius;
            }
            return CornerRadii.Circular(tl, tr, br, bl);
        }

        private static bool Same(in CornerRadii a, in CornerRadii b)
            => a.TopLeft == b.TopLeft && a.TopRight == b.TopRight && a.BottomRight == b.BottomRight
                && a.BottomLeft == b.BottomLeft;

        private static float Sane(float v) => float.IsNaN(v) ? 0f : v;
    }
}
