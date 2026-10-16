using System;
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
        // bands are their children. Overlay sits after the element and holds the outset ring under its offset fill;
        // OutlineOverlay sits where the outline paints over the content around it (RingOverlay.OutlineHost).
        public readonly VisualElement Overlay;
        public readonly VisualElement RingElement;
        public readonly VisualElement OffsetElement;
        public readonly VisualElement OutlineOverlay;
        public readonly VisualElement OutlineElement;
        public string[] ClassNames = null!;
        public RingSpec Spec;
        // The element's size and its corner radii fitted to it, as the last geometry sync read them.
        public float Width;
        public float Height;
        public Vector4 Radii;
        // The colour each band is painted in this frame: a band naming no colour takes the element's text colour.
        public Color RingTint;
        public Color OutlineTint;
        public Color InsetRingTint;
        // Paints the bands ring-inset, inset-ring-* and inset-shadow-* put inside the padding box, in the
        // element's own content, so the element's children paint over them.
        public Action<MeshGenerationContext>? OnGenerateInside;
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
    /// Paints the <c>ring-*</c>, <c>ring-offset-*</c>, <c>outline-*</c>, <c>inset-ring-*</c> and
    /// <c>inset-shadow-*</c> bands of an element.
    /// </summary>
    /// <remarks>
    /// UI Toolkit has neither <c>box-shadow</c> nor <c>outline</c>, so each band is drawn by Velvet as a rounded
    /// rect with a rounded hole (<see cref="RingBandShape"/>), and where it is drawn follows where CSS paints it.
    /// <para>
    /// An outset ring and its offset fill are an outer box-shadow, painted with the element's background and
    /// outside its border edge. They are drawn on an overlay that is a SIBLING of the element, placed directly
    /// after it. Painting them in the element's own <c>generateVisualContent</c> was measured to lose them on an
    /// element that also carries <c>overflow: hidden</c>, whose clip applies to its own generated content; CSS
    /// clips neither an outline nor a box-shadow by the element's own overflow, and
    /// <c>overflow-hidden rounded-full ring-2</c> is the avatar pattern. A structural wrapper was rejected
    /// because it alters every layout relationship between the element and its parent. The sibling gets the
    /// element's paint position from child adjacency, because UI Toolkit paints an absolutely-positioned sibling
    /// in child order against an in-flow one; that engine fact is measured by pixel readback in
    /// <c>RingSiblingPaintOrderPlaybackTests</c>, and the placement rests on it.
    /// </para>
    /// <para>
    /// An inset ring, offset fill, inset ring and inset shadow lie inside the padding box, over the background and
    /// under the element's content, so they are drawn in the element's own <c>generateVisualContent</c>, ahead of
    /// a text element's own text, where the element's own clip is the one CSS applies too.
    /// </para>
    /// <para>
    /// An outline paints after all the content of its stacking context, so it is drawn on a second overlay, the
    /// last child of the nearest ancestor that composites or clips what it holds (<c>OutlineHost</c>).
    /// </para>
    /// A transform, an opacity, a visibility or a display on the ringed element reaches that element's own
    /// subtree only, which the overlays are not in, so they are given each of them (<c>SyncElementState</c>).
    /// </remarks>
    internal static class RingOverlay
    {
        // Lets the animation scheduler find a ring binding on the element it is animating. The overlays are
        // outside that element's subtree, so UI Toolkit's opacity compositing does not reach them, and only an
        // explicit co-fade can fade them with their element. Auto-drops entries when an element is GC'd; Detach
        // removes eagerly so a pooled element cannot ghost a prior consumer's band.
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

        private static readonly Func<VisualElement, bool>? s_shouldClip = ShouldClipDelegate();

        private static Func<VisualElement, bool>? ShouldClipDelegate()
        {
            try
            {
                return EngineMember.ElementShouldClip.ResolveMethod() is { } method
                    ? (Func<VisualElement, bool>)Delegate.CreateDelegate(typeof(Func<VisualElement, bool>), method)
                    : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static VisualElement NewOverlay(string name)
        {
            var overlay = new VisualElement
            {
                name = name,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute },
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
                NewBand(overlay, RingBandName, mgc => PaintSpread(mgc, binding, binding.Spec.RingSpread, binding.RingTint)),
                NewBand(overlay, OffsetBandName, mgc => PaintSpread(mgc, binding, binding.Spec.OffsetSpread, binding.Spec.OffsetColor)),
                outlineOverlay,
                NewBand(outlineOverlay, OutlineBandName, mgc => PaintOutline(mgc, binding)))
            {
                ClassNames = classNames,
                Spec = spec,
            };
            binding.OnGenerateInside = mgc => PaintInside(mgc, element, binding);
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
                PlaceOutline(element, binding);
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
            element.generateVisualContent -= binding.OnGenerateInside;
            element.MarkDirtyRepaint();
            // The overlays live outside the element's own subtree, so they do not leave with it — a detach that
            // skipped this would strand a live band.
            binding.Overlay.RemoveFromHierarchy();
            binding.OutlineOverlay.RemoveFromHierarchy();
            s_byElement.Remove(element);
        }

        // Which bands are shown, and whether the element paints the inside ones. Geometry follows in SyncGeometry
        // once the element is laid out.
        private static void ApplySpec(RingBinding binding, VisualElement element)
        {
            var spec = binding.Spec;
            Show(binding.RingElement, !spec.Inset && spec.RingSpread > 0f);
            Show(binding.OffsetElement, !spec.Inset && spec.OffsetSpread > 0f);
            Show(binding.OutlineElement, spec.Outline.Width > 0f);
            element.generateVisualContent -= binding.OnGenerateInside;
            if (spec.PaintsInside)
            {
                InsertBeforeOwnText(element, binding.OnGenerateInside!);
            }
            ResolveTints(element, binding);
            binding.RingElement.MarkDirtyRepaint();
            binding.OffsetElement.MarkDirtyRepaint();
            binding.OutlineElement.MarkDirtyRepaint();
            element.MarkDirtyRepaint();
        }

        private static void Show(VisualElement band, bool shown)
            => band.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

        // A text element paints its text in a callback it registered on itself at construction; the inside bands go
        // just ahead of it, and after every callback a face painter put first (DropShadowSilhouette, SkewSilhouette).
        private static void InsertBeforeOwnText(VisualElement element, Action<MeshGenerationContext> paint)
        {
            Action<MeshGenerationContext>? before = null;
            Action<MeshGenerationContext>? after = null;
            var reachedOwn = false;
            foreach (var callback in element.generateVisualContent?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                reachedOwn |= ReferenceEquals(callback.Target, element);
                if (reachedOwn)
                {
                    after += (Action<MeshGenerationContext>)callback;
                }
                else
                {
                    before += (Action<MeshGenerationContext>)callback;
                }
            }
            element.generateVisualContent = before + paint + after;
        }

        // The tint of each band naming no colour follows the element's text colour, as currentcolor does. Returns
        // whether one changed, which is what repaints it.
        private static bool ResolveTints(VisualElement element, RingBinding binding)
        {
            var spec = binding.Spec;
            var text = element.resolvedStyle.color;
            var ring = spec.Ring.CurrentColor ? text : spec.Ring.Color;
            var outline = spec.Outline.CurrentColor ? text : spec.Outline.Color;
            var insetRing = spec.InsetRing.CurrentColor ? text : spec.InsetRing.Color;
            var changed = ring != binding.RingTint || outline != binding.OutlineTint || insetRing != binding.InsetRingTint;
            binding.RingTint = ring;
            binding.OutlineTint = outline;
            binding.InsetRingTint = insetRing;
            return changed;
        }

        // Re-homes both overlays and re-sizes them. The hosts are re-derived on every call rather than captured at
        // attach: a keyed move, a z-* relocation into a layer container, or an attach that had not happened yet at
        // create time all change them, and each of those re-enters here.
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
            PlaceOutline(element, binding);
            SyncElementState(element, binding);
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
            binding.Radii = ResolveRadii(element, binding, width, height);

            if (!TryOffsetWithin(element, host, out var x, out var y))
            {
                return;
            }
            SetBox(binding.Overlay, x, y, width, height);

            var spec = binding.Spec;
            var outlineGrow = spec.Outline.Offset + spec.Outline.Width;
            PlaceBand(binding.RingElement, spec.RingSpread, binding, RingBandShape.SpreadRadii(binding.Radii, spec.RingSpread));
            PlaceBand(binding.OffsetElement, spec.OffsetSpread, binding, RingBandShape.SpreadRadii(binding.Radii, spec.OffsetSpread));
            PlaceBand(binding.OutlineElement, outlineGrow, binding, RingBandShape.OutlineRadii(binding.Radii, outlineGrow));
            binding.RingElement.MarkDirtyRepaint();
            binding.OffsetElement.MarkDirtyRepaint();
            binding.OutlineElement.MarkDirtyRepaint();
        }

        // Where element's border box sits in host's padding box, which an absolute child's left and top resolve
        // against: its layout offsets up to host, each relative to its parent's border box, less host's border.
        // False where a layout is not known yet.
        private static bool TryOffsetWithin(VisualElement element, VisualElement host, out float x, out float y)
        {
            x = 0f;
            y = 0f;
            for (var e = element; e != host && e != null; e = e.parent)
            {
                var layout = e.layout;
                if (float.IsNaN(layout.x) || float.IsNaN(layout.y))
                {
                    return false;
                }
                x += layout.x;
                y += layout.y;
            }
            x -= Sane(host.resolvedStyle.borderLeftWidth);
            y -= Sane(host.resolvedStyle.borderTopWidth);
            return true;
        }

        private static void SetBox(VisualElement e, float x, float y, float width, float height)
        {
            e.style.left = x;
            e.style.top = y;
            e.style.width = Mathf.Max(0f, width);
            e.style.height = Mathf.Max(0f, height);
        }

        // A band's box is the element's grown by grow, rounded as the band's outer edge is painted.
        private static void PlaceBand(VisualElement band, float grow, RingBinding binding, Vector4 radii)
        {
            SetBox(band, -grow, -grow, binding.Width + (grow * 2f), binding.Height + (grow * 2f));
            radii = RingBandShape.Fit(radii, Mathf.Max(0f, binding.Width + (grow * 2f)), Mathf.Max(0f, binding.Height + (grow * 2f)));
            band.style.borderTopLeftRadius = radii.x;
            band.style.borderTopRightRadius = radii.y;
            band.style.borderBottomRightRadius = radii.z;
            band.style.borderBottomLeftRadius = radii.w;
        }

        // The outline overlay's host: the nearest ancestor whose content an outline must not paint over from
        // outside, because it composites, transforms, hides or clips what it holds, or which is a z-index layer;
        // failing one, the topmost ancestor under the panel's root. The outline is that host's last child, so it
        // paints over the content CSS paints it over, and the host's own effects reach it as they reach the element.
        internal static VisualElement? OutlineHost(VisualElement element)
        {
            var candidate = element.parent;
            while (candidate != null)
            {
                var up = candidate.parent;
                if (up == null || up == candidate.panel?.visualTree || IsOutlineBoundary(candidate))
                {
                    return candidate;
                }
                candidate = up;
            }
            return null;
        }

        private static bool IsOutlineBoundary(VisualElement e)
        {
            var rs = e.resolvedStyle;
            // Velvet writes filters inline and its sheets declare none, so the inline list is the one read.
            var filter = e.style.filter;
            return rs.display == DisplayStyle.None || rs.opacity < 1f || rs.translate != Vector3.zero
                || rs.scale.value != Vector3.one || rs.rotate.angle.value != 0f
                || (filter.keyword == StyleKeyword.Undefined && filter.value is { Count: > 0 })
                || FiberZLayerCoordinator.IsLayerContainer(e) || Clips(e);
        }

        // Whether e clips its children, as UI Toolkit decides it; an inline overflow: hidden where the engine member
        // does not resolve.
        private static bool Clips(VisualElement e)
        {
            return s_shouldClip?.Invoke(e)
                ?? (e.style.overflow.keyword == StyleKeyword.Undefined && e.style.overflow.value == Overflow.Hidden);
        }

        // Keeps the outline overlay the last child of its host but for other outlines, and over the element's box.
        private static void PlaceOutline(VisualElement element, RingBinding binding)
        {
            var overlay = binding.OutlineOverlay;
            var host = binding.Spec.Outline.Width > 0f && element.parent != null ? OutlineHost(element) : null;
            if (host == null)
            {
                overlay.RemoveFromHierarchy();
                return;
            }
            if (overlay.parent != host || !LastButOutlines(host, overlay))
            {
                overlay.RemoveFromHierarchy();
                host.Add(overlay);
            }
            if (binding.Width > 0f && TryOffsetWithin(element, host, out var x, out var y))
            {
                SetBox(overlay, x, y, binding.Width, binding.Height);
            }
        }

        private static bool LastButOutlines(VisualElement host, VisualElement overlay)
        {
            for (var i = host.IndexOf(overlay) + 1; i < host.childCount; i++)
            {
                if (!host[i].ClassListContains(OutlineMarkerClass))
                {
                    return false;
                }
            }
            return true;
        }

        // Gives both overlays what the element's own subtree would have given them had they been inside: its
        // opacity, visibility and display, and its transform, pivoting where the element pivots. Each overlay's box
        // is the element's, so the bands, which reach outside it, turn about the element's pivot.
        private static void SyncElementState(VisualElement element, RingBinding binding)
        {
            if (ResolveTints(element, binding))
            {
                binding.RingElement.MarkDirtyRepaint();
                binding.OutlineElement.MarkDirtyRepaint();
                if (binding.Spec.PaintsInside)
                {
                    element.MarkDirtyRepaint();
                }
            }
            CopyState(element, binding.Overlay.style);
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

        // An outset box-shadow band: the element's box grown by spread, less the element's box, the outer corners
        // rounded as RingBandShape.SpreadRadius gives them.
        private static void PaintSpread(MeshGenerationContext mgc, RingBinding binding, float spread, Color tint)
        {
            if (spread <= 0f || binding.Width <= 0f)
            {
                return;
            }
            var outer = new Rect(0f, 0f, binding.Width + (spread * 2f), binding.Height + (spread * 2f));
            var hole = new Rect(spread, spread, binding.Width, binding.Height);
            RingBandShape.Fill(mgc.painter2D, outer, RingBandShape.SpreadRadii(binding.Radii, spread), hole, binding.Radii, tint);
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
            RingBandShape.Fill(mgc.painter2D, outer, RingBandShape.OutlineRadii(binding.Radii, grow), hole,
                RingBandShape.OutlineRadii(binding.Radii, outline.Offset), binding.OutlineTint);
        }

        // What CSS paints inside the padding box, bottom up as box-shadow lists them top down: the ring and its
        // offset fill where ring-inset puts them there, the inset ring, the inset shadow.
        private static void PaintInside(MeshGenerationContext mgc, VisualElement element, RingBinding binding)
        {
            var rs = element.resolvedStyle;
            var width = element.layout.width;
            var height = element.layout.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                return;
            }
            var (left, top) = (Sane(rs.borderLeftWidth), Sane(rs.borderTopWidth));
            var (right, bottom) = (Sane(rs.borderRightWidth), Sane(rs.borderBottomWidth));
            var padding = new Rect(left, top, width - left - right, height - top - bottom);
            if (padding.width <= 0f || padding.height <= 0f)
            {
                return;
            }
            var outer = RingBandShape.Fit(ResolveRadii(element, binding, width, height), width, height);
            var radii = new Vector4(Mathf.Max(0f, outer.x - Mathf.Max(left, top)), Mathf.Max(0f, outer.y - Mathf.Max(right, top)),
                Mathf.Max(0f, outer.z - Mathf.Max(right, bottom)), Mathf.Max(0f, outer.w - Mathf.Max(left, bottom)));

            var spec = binding.Spec;
            var painter = mgc.painter2D;
            if (spec.Inset)
            {
                PaintInsetBand(painter, padding, radii, spec.RingSpread, binding.RingTint);
                PaintInsetBand(painter, padding, radii, spec.OffsetSpread, spec.OffsetColor);
            }
            PaintInsetBand(painter, padding, radii, spec.InsetRing.Width, binding.InsetRingTint);
            if (spec.HasInsetShadow)
            {
                PaintInsetShadow(mgc, element, spec.InsetShadow, padding, radii);
            }
        }

        // An inset box-shadow band with no blur: the padding box less the padding box shrunk by spread.
        private static void PaintInsetBand(Painter2D painter, Rect padding, Vector4 radii, float spread, Color tint)
        {
            if (spread <= 0f)
            {
                return;
            }
            var hole = new Rect(padding.x + spread, padding.y + spread, padding.width - (spread * 2f), padding.height - (spread * 2f));
            RingBandShape.Fill(painter, padding, radii, hole, RingBandShape.SpreadRadii(radii, -spread), tint);
        }

        private static void PaintInsetShadow(MeshGenerationContext mgc, VisualElement element, in ShadowSpec shadow,
            Rect padding, Vector4 radii)
        {
            var tint = shadow.TintFor(element);
            if (tint.a <= 0f)
            {
                return;
            }
            var texture = InsetShadowBaker.Get(padding.width, padding.height, radii, shadow);
            if (texture == null)
            {
                return;
            }
            var (minX, minY) = (padding.xMin, padding.yMin);
            var (maxX, maxY) = (padding.xMin + texture.width, padding.yMin + texture.height);
            var mesh = mgc.Allocate(4, 6, texture);
            mesh.SetNextVertex(new Vertex { position = new Vector3(minX, minY, Vertex.nearZ), tint = tint, uv = new Vector2(0f, 1f) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(maxX, minY, Vertex.nearZ), tint = tint, uv = new Vector2(1f, 1f) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(maxX, maxY, Vertex.nearZ), tint = tint, uv = new Vector2(1f, 0f) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(minX, maxY, Vertex.nearZ), tint = tint, uv = new Vector2(0f, 0f) });
            mesh.SetNextIndex(0);
            mesh.SetNextIndex(1);
            mesh.SetNextIndex(2);
            mesh.SetNextIndex(0);
            mesh.SetNextIndex(2);
            mesh.SetNextIndex(3);
        }

        // The four corner radii the bands round to, fitted to the element's box. Laid out, the resolved values are
        // authoritative per corner — including a resolved 0, which is what a card rounded on one corner only has on
        // its other three. The class-scale fallback is taken ONLY when no corner resolved anything, which is the
        // pre-layout / off-screen case where a USS rounded-* does not reflect into resolvedStyle at all; a
        // per-corner resolved 0 must not reach it, because the fallback answers for the whole element (it takes the
        // top-left as representative, see StyleShadowClass.TryResolveCornerRadius) and would round all four
        // corners of a `rounded-tl-lg` band.
        private static Vector4 ResolveRadii(VisualElement element, RingBinding binding, float width, float height)
        {
            var rs = element.resolvedStyle;
            var radii = new Vector4(Sane(rs.borderTopLeftRadius), Sane(rs.borderTopRightRadius),
                Sane(rs.borderBottomRightRadius), Sane(rs.borderBottomLeftRadius));
            if (!(element.panel != null && (radii.x > 0f || radii.y > 0f || radii.z > 0f || radii.w > 0f))
                && StyleRingClass.TryResolveCornerRadius(binding.ClassNames, out var classRadius))
            {
                radii = new Vector4(classRadius, classRadius, classRadius, classRadius);
            }
            return RingBandShape.Fit(radii, width, height);
        }

        private static float Sane(float v) => float.IsNaN(v) ? 0f : v;
    }
}
