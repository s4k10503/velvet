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
        public readonly VisualElement Overlay;
        public string[] ClassNames = null!;
        public RingSpec Spec;
        public EventCallback<GeometryChangedEvent>? OnGeometry;
        public EventCallback<AttachToPanelEvent>? OnAttach;
        public IVisualElementScheduledItem? StateTick;
        // True while the overlay is configured as the element's own first child (an inset band) rather than as
        // its following sibling; RingOverlay.SetMode is the only writer.
        public bool Inside;

        public RingBinding(VisualElement overlay) => Overlay = overlay;
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
    /// lifting positioned elements above the in-flow ones as CSS does; that engine fact is measured by
    /// pixel readback in <c>RingSiblingPaintOrderPlaybackTests</c>, and the whole placement rests on it.
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
    /// </remarks>
    internal static class RingOverlay
    {
        // Lets the animation scheduler find a ring binding on the element it is animating. The band is a
        // SIBLING of that element, so UI Toolkit's opacity compositing — which reaches every overlay
        // belonging to a descendant — does not reach this one, and only an explicit co-fade can fade it with
        // its element. Auto-drops entries when an element is GC'd; Detach removes eagerly so a pooled element
        // cannot ghost a prior consumer's band. Mirrors BoxShadowSilhouette's per-element side-channel.
        private static readonly ConditionalWeakTable<VisualElement, RingBinding> s_byElement = new();

        public static RingBinding? TryGet(VisualElement element)
            => s_byElement.TryGetValue(element, out var binding) ? binding : null;

        // Marks the overlay as a reconciler-invisible child of its host, recognized by
        // SilhouetteBoundsSpacer.IsSpacer — the single predicate every "real child" count and index site
        // already goes through, so the child reconciler, the structural variants, [&>*]: and the gap / grid /
        // divide manipulators all skip it without any of them learning about rings.
        internal const string MarkerClass = "velvet-ring-overlay";
        internal const string OverlayName = "velvet-ring-overlay";

        // The marker of an inset band, which is the element's own first child rather than its following sibling.
        // A separate class from MarkerClass because the two sit in different places: LogicalChildSlots must let
        // an append step over a leading one and stop before a following one, and FiberZLayerCoordinator keeps a
        // leading one first where it keeps a following one behind its element.
        internal const string InsetMarkerClass = "velvet-ring-inset-overlay";

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
            SetMode(binding);
            ApplySpec(overlay, spec, element);

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
            ApplySpec(binding.Overlay, spec, element);
            Place(element, binding);
        }

        // Moves the overlay between the two roles its spec can ask for. Leaving the inside role hands the
        // band back to the sibling path, which writes the inline values it needs; entering it clears the
        // inline opacity, visibility, display and transform that path copied from the element, because the
        // band now receives each of them from the element as its child and would show it twice.
        private static void SetMode(RingBinding binding)
        {
            var inside = binding.Spec.Inset;
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
            ProjectedCornerFace.Forget(binding.Overlay);
            // The overlay lives in the element's PARENT, so it does not leave with the element's own subtree
            // the way the old wrapper-hosted one did — a detach that skipped this would strand a live band.
            binding.Overlay.RemoveFromHierarchy();
            s_byElement.Remove(element);
        }

        // Native border on all four sides is the whole band; its geometry (position / size / radii) is set by
        // SyncGeometry once the element is laid out.
        private static void ApplySpec(VisualElement overlay, RingSpec spec, VisualElement element)
        {
            overlay.style.borderTopWidth = spec.Width;
            overlay.style.borderRightWidth = spec.Width;
            overlay.style.borderBottomWidth = spec.Width;
            overlay.style.borderLeftWidth = spec.Width;
            ApplyColor(overlay, spec.CurrentColor ? element.resolvedStyle.color : spec.Color);
        }

        private static void ApplyColor(VisualElement overlay, Color color)
        {
            overlay.style.borderTopColor = color;
            overlay.style.borderRightColor = color;
            overlay.style.borderBottomColor = color;
            overlay.style.borderLeftColor = color;
        }

        // Re-homes the overlay onto the element's CURRENT parent and re-sizes it. The host is re-derived on
        // every call rather than captured at attach: a keyed move, a z-* relocation among its siblings, or
        // an attach that had not happened yet at create time all change it, and each of those re-enters here.
        private static void Place(VisualElement element, RingBinding binding)
        {
            if (binding.Inside)
            {
                PlaceInside(element, binding);
                return;
            }
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
            SyncElementState(element, binding);
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

            var style = overlay.style;
            style.left = 0f;
            style.top = 0f;
            style.right = 0f;
            style.bottom = 0f;
            style.width = StyleKeyword.Null;
            style.height = StyleKeyword.Null;
            SyncElementState(element, binding);
        }

        // The padding box's corners are the element's less the border beside them, the larger side's where the
        // two differ, as SceneViewDriver rounds its feed. Read on the state tick as well as on a placement,
        // since a border that changes alone fires no geometry event.
        private static void SyncInsideRadii(VisualElement element, RingBinding binding)
        {
            var style = binding.Overlay.style;
            var rs = element.resolvedStyle;
            ResolveRadii(element, binding, out var tl, out var tr, out var br, out var bl);
            var left = Sane(rs.borderLeftWidth);
            var top = Sane(rs.borderTopWidth);
            var right = Sane(rs.borderRightWidth);
            var bottom = Sane(rs.borderBottomWidth);
            style.borderTopLeftRadius = Mathf.Max(0f, tl - Mathf.Max(left, top));
            style.borderTopRightRadius = Mathf.Max(0f, tr - Mathf.Max(right, top));
            style.borderBottomRightRadius = Mathf.Max(0f, br - Mathf.Max(right, bottom));
            style.borderBottomLeftRadius = Mathf.Max(0f, bl - Mathf.Max(left, bottom));
        }

        // Sizes and positions the outset overlay over the element's laid-out box: the band sits OUTSIDE the
        // element edge by Offset, or inside it by a negative outline offset, so the overlay inflates by
        // (Offset + Width) per side and each outer corner radius is that corner's radius plus the same amount.
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

            var grow = spec.Offset + spec.Width;
            overlay.style.left = originX - grow - hostBorderLeft;
            overlay.style.top = originY - grow - hostBorderTop;
            overlay.style.right = StyleKeyword.Null;
            overlay.style.bottom = StyleKeyword.Null;
            overlay.style.width = Mathf.Max(0f, width + (grow * 2f));
            overlay.style.height = Mathf.Max(0f, height + (grow * 2f));

            ResolveRadii(element, binding, out var tl, out var tr, out var br, out var bl);
            overlay.style.borderTopLeftRadius = Mathf.Max(0f, tl + grow);
            overlay.style.borderTopRightRadius = Mathf.Max(0f, tr + grow);
            overlay.style.borderBottomRightRadius = Mathf.Max(0f, br + grow);
            overlay.style.borderBottomLeftRadius = Mathf.Max(0f, bl + grow);
        }

        // Gives the band what the element's own subtree would have given it had it been inside: its opacity,
        // visibility and display, and its transform, pivoting where the element pivots. The overlay's box
        // starts (Offset + Width) up and left of the element's for an outset band, so the element's pivot sits
        // that much further into the overlay. The resolved values are pixels, so a percentage translate stays
        // a fraction of the element's box rather than becoming one of the larger overlay's.
        private static void SyncElementState(VisualElement element, RingBinding binding)
        {
            var rs = element.resolvedStyle;
            var style = binding.Overlay.style;
            // A band in the element's text colour follows it as it changes, as currentcolor does.
            if (binding.Spec.CurrentColor)
            {
                ApplyColor(binding.Overlay, rs.color);
            }
            if (binding.Inside)
            {
                // Everything else reaches the band from the element it is a child of (see SetMode).
                SyncInsideRadii(element, binding);
                if (!float.IsNaN(rs.width))
                {
                    SyncProjectedCorners(element, binding);
                }
                return;
            }
            style.opacity = rs.opacity;
            style.visibility = rs.visibility;
            style.display = rs.display;
            if (float.IsNaN(rs.width))
            {
                return; // pre-layout: a percentage pivot has no box to resolve against yet
            }
            SyncProjectedCorners(element, binding);
            var grow = binding.Spec.Offset + binding.Spec.Width;
            var origin = rs.transformOrigin;
            var translate = rs.translate;
            style.transformOrigin = new TransformOrigin(origin.x + grow, origin.y + grow, origin.z);
            style.translate = new Translate(translate.x, translate.y, translate.z);
            style.scale = rs.scale;
            style.rotate = rs.rotate;
        }

        // While a layoutId projection paints the element with a radius per axis (ProjectedCornerFace), the band is
        // painted the same way, each of its corners the element's grown on both axes as SyncGeometry grows them.
        private static void SyncProjectedCorners(VisualElement element, RingBinding binding)
        {
            if (!ProjectedCornerFace.TryGetRadii(element, out var radii))
            {
                ProjectedCornerFace.End(binding.Overlay);
                return;
            }
            var grow = binding.Spec.Inset ? 0f : binding.Spec.Offset + binding.Spec.Width;
            var fitted = BorderBandPainter.Fitted(radii, element.layout.width, element.layout.height);
            var g = new Vector2(grow, grow);
            ProjectedCornerFace.Draw(binding.Overlay, new CornerRadii
            {
                TopLeft = fitted.TopLeft + g,
                TopRight = fitted.TopRight + g,
                BottomRight = fitted.BottomRight + g,
                BottomLeft = fitted.BottomLeft + g,
            }, 0f);
        }

        // The four corner radii the band rounds to. Laid out, the resolved values are authoritative per
        // corner — including a resolved 0, which is what a card rounded on one corner only has on its other
        // three. The class-scale fallback is taken ONLY when no corner resolved anything, which is the
        // pre-layout / off-screen case where a USS rounded-* does not reflect into resolvedStyle at all; a
        // per-corner resolved 0 must not reach it, because the fallback answers for the whole element (it
        // takes the top-left as representative, see StyleShadowClass.TryResolveCornerRadius) and would round
        // all four corners of a `rounded-tl-lg` band. That fallback stays whole-element, so a per-corner
        // class is approximated until the first geometry event replaces it with the resolved values.
        private static void ResolveRadii(VisualElement element, RingBinding binding,
            out float tl, out float tr, out float br, out float bl)
        {
            var rs = element.resolvedStyle;
            tl = Sane(rs.borderTopLeftRadius);
            tr = Sane(rs.borderTopRightRadius);
            br = Sane(rs.borderBottomRightRadius);
            bl = Sane(rs.borderBottomLeftRadius);
            if (element.panel != null && (tl > 0f || tr > 0f || br > 0f || bl > 0f))
            {
                return;
            }
            if (StyleRingClass.TryResolveCornerRadius(binding.ClassNames, out var classRadius))
            {
                tl = tr = br = bl = classRadius;
            }
        }

        private static float Sane(float v) => float.IsNaN(v) ? 0f : v;
    }
}
