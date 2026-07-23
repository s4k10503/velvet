using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Keeps a filtered caster's own <c>generateVisualContent</c> overflow (a sheared skew silhouette, a
    /// drop-shadow bleed) from being clipped when UI Toolkit renders the element through a filter.
    /// </summary>
    /// <remarks>
    /// An inline <c>style.filter</c> promotes the element to a nested render tree whose offscreen texture is
    /// sized to the element's <c>boundingBox</c> — the layout rect unioned with each LAYOUT child's box, never
    /// the element's own gVC paint. So any paint drawn outside the layout rect (which the skew / shadow paints
    /// do by design) falls outside that texture and is dropped, collapsing a sheared silhouette back to a
    /// rectangle. UITK exposes no API to widen an element's own render bounds.
    /// <para>
    /// The fix is a transparent, picking-ignored, absolutely-positioned SPACER child sized to the paint's
    /// overflow AABB: <c>UpdateBoundingBox</c> unions a child's layout rect into the parent's boundingBox, which
    /// is exactly what sizes the filter texture, so the caster's own overflow now falls inside it and survives.
    /// The spacer is kept as the LAST child so the positional child reconciler (which only ever addresses the
    /// slots <c>[0, vnodeChildCount)</c>) never mistakes it for a rendered child, and it is torn down with the
    /// paint binding. It is added only while a filter is present (widening boundingBox otherwise would perturb
    /// picking / scroll-content bounds for no benefit).
    /// </para>
    /// </remarks>
    internal static class SilhouetteBoundsSpacer
    {
        // Marks the reconciler-invisible spacer so tests / tooling can identify it and cleanup can find it.
        internal const string MarkerClass = "velvet-bounds-spacer";
        internal const string SpacerName = "velvet-bounds-spacer";

        // A little slack beyond the computed AABB absorbs the stroke half-width and antialiasing (the
        // border-box vs padding-box origin offset is handled exactly in Sync, not by this fudge). Over-covering
        // only grows the offscreen texture slightly; it is never visible (the spacer paints nothing).
        private const float Slack = 4f;

        /// <summary>
        /// Ensures <paramref name="caster"/> carries (when <paramref name="want"/>) a last-child spacer whose
        /// layout rect covers <paramref name="aabbLocal"/> — the paint's overflow AABB in the caster's local
        /// space (its x/y may be negative for left/top overflow). Removes the spacer when not wanted.
        /// </summary>
        internal static void Sync(VisualElement caster, ref VisualElement? spacer, bool want, Rect aabbLocal)
        {
            if (!want)
            {
                Remove(caster, ref spacer);
                return;
            }

            // Attach the spacer as soon as it is wanted — even before layout gives a size (create time, or
            // EditMode where the player-loop never runs layout). It sizes to the paint's extent once the AABB
            // is known (the geometry callback re-syncs), so it widens boundingBox exactly when a real filter
            // pass can clip; an unsized spacer is a harmless empty trailing child in the meantime.
            if (spacer == null)
            {
                spacer = new VisualElement { name = SpacerName, pickingMode = PickingMode.Ignore };
                spacer.AddToClassList(MarkerClass);
                // position:absolute takes the spacer out of flex flow (no sibling shift) and, on a panel, makes
                // StyleOutOfFlowChild treat it as out-of-flow so the index-driven child manipulators (gap /
                // grid / divide) skip it. Off panel StyleOutOfFlowChild recognizes it by the MarkerClass
                // instead — the spacer deliberately carries NO "absolute" utility class, so a user's
                // has-[.absolute]: selector cannot false-match this internal child. Structural variants and the
                // child reconciler likewise skip it by the MarkerClass (they count DOM children, which CSS
                // structural selectors include — the spacer is the one internal child that must not).
                spacer.style.position = Position.Absolute;
            }

            // Keep it in the trailing spacer zone (after every rendered child) so the child reconciler's
            // [0, vnodeChildCount) indexing never reaches it. "After all rendered children" (not "strictly
            // last") keeps a second spacer — a skewed element that also drops a shadow — stable instead of the
            // two ping-ponging for the last slot each sync.
            if (!IsAfterAllRenderedChildren(caster, spacer))
            {
                if (spacer.parent == caster)
                {
                    caster.Remove(spacer);
                }
                caster.Add(spacer);
            }

            if (aabbLocal.width > 0f && aabbLocal.height > 0f
                && !float.IsNaN(aabbLocal.width) && !float.IsNaN(aabbLocal.height))
            {
                // aabbLocal is the paint extent in the caster's border-box gVC space; the caller has already
                // shifted its origin left/up by the caster's border so this padding-box-relative left/top lands
                // the coverage correctly in border-box space (see BorderInset / the paint layers' aabb shift).
                spacer.style.left = aabbLocal.xMin - Slack;
                spacer.style.top = aabbLocal.yMin - Slack;
                spacer.style.width = aabbLocal.width + (2f * Slack);
                spacer.style.height = aabbLocal.height + (2f * Slack);
            }
        }

        // True when child is a bounds-spacer (the internal, reconciler-invisible render-bounds child) OR a
        // z-index layer container (FiberZLayerCoordinator's front/back containers, which are equally
        // reconciler-invisible — a z-marked absolute child's real element lives inside one instead of at its
        // logical slot). NonSpacerChildCount below is the single centralized consumer every "real child"
        // count/index site already goes through, so broadening this one predicate makes the whole reconciler
        // treat both z-layer containers as invisible without touching any of those call sites.
        internal static bool IsSpacer(VisualElement child)
            => child != null
                && (child.ClassListContains(MarkerClass) || FiberZLayerCoordinator.IsLayerContainer(child));

        // True when spacer is a child of caster and no RENDERED (non-spacer) child follows it — the placement
        // invariant that keeps it outside the child reconciler's [0, renderedChildCount) index range.
        private static bool IsAfterAllRenderedChildren(VisualElement caster, VisualElement spacer)
        {
            var idx = caster.IndexOf(spacer);
            if (idx < 0)
            {
                return false;
            }
            for (var i = idx + 1; i < caster.childCount; i++)
            {
                if (!IsSpacer(caster[i]))
                {
                    return false;
                }
            }
            return true;
        }

        // The container's child count excluding the trailing bounds-spacer(s). The spacers are always kept
        // last, so the real children occupy [0, this) — the range the child reconciler and structural
        // variants must treat as the whole child list.
        // Floored at the LEADING run of z-layer containers (FiberZLayerCoordinator's own back container is the
        // one reconciler-invisible child ever placed leading, never trailing): IsSpacer also recognizes a layer
        // container, so an unguarded trailing trim would eat it too whenever it is (even transiently, mid-diff)
        // the parent's LAST child — e.g. the instant an interspersed ordinary sibling's own placeholder is
        // removed and the back container is momentarily all that is left. Once eaten from the count, an
        // ordinary insert clamped against that undercount lands BEFORE the back container instead of after it,
        // permanently misplacing it (it stops being the parent's leading child, corrupting LeadingOffset for
        // this parent from then on). Scanning the leading run first — not calling into the reconciler layer —
        // keeps the Styling -> Reconciler dependency direction this file already has via IsLayerContainer, one
        // level further. A front (trailing) container reached by this same leading scan only when it happens to
        // be the parent's OWN sole child protects it identically; every other trailing case (the ordinary,
        // overwhelming majority) is unaffected since the floor is 0 there.
        internal static int NonSpacerChildCount(VisualElement container)
        {
            var n = container.childCount;
            var floor = 0;
            while (floor < n && FiberZLayerCoordinator.IsLayerContainer(container[floor]))
            {
                floor++;
            }
            while (n > floor && IsSpacer(container[n - 1]))
            {
                n--;
            }
            return n;
        }

        internal static void Remove(VisualElement caster, ref VisualElement? spacer)
        {
            if (spacer == null)
            {
                return;
            }
            if (ReferenceEquals(spacer.parent, caster))
            {
                caster.Remove(spacer);
            }
            spacer = null;
        }

        // The overflow AABB of a sheared silhouette in the caster's local space, from the shear model
        // x' = x + (y - h/2)*tanX, y' = y + (x - w/2)*tanY over the box [0,w]x[0,h]: the shear pushes the box
        // out by half the cross-extent times the tangent on each side.
        internal static Rect ShearedAabb(float w, float h, float tanX, float tanY)
        {
            var ox = 0.5f * h * Mathf.Abs(tanX);
            var oy = 0.5f * w * Mathf.Abs(tanY);
            return new Rect(-ox, -oy, w + (2f * ox), h + (2f * oy));
        }

        // Grows a rect outward by the shear overhang its own extent produces (same model as ShearedAabb),
        // so a shadow quad on a skewed caster — which shears with the caster — stays covered.
        internal static Rect ExpandForShear(Rect r, float tanX, float tanY)
        {
            var ox = 0.5f * r.height * Mathf.Abs(tanX);
            var oy = 0.5f * r.width * Mathf.Abs(tanY);
            return new Rect(r.xMin - ox, r.yMin - oy, r.width + (2f * ox), r.height + (2f * oy));
        }

        // The left/top border width the class list implies, in px, so a caller can shift the paint AABB into
        // padding-box space (the origin an absolute child's left/top resolve against) and keep the spacer's
        // coverage correct under any border. Parsed straight from the classes — not resolvedStyle (unresolved
        // at the paint layers' one-shot geometry-callback sizing time) nor inline style (misses the USS-class
        // scale borders border/border-2/…). Variant layers are peeled so a state border (hover:border-8) is
        // reserved for too, and the MAX across classes is taken: over-covering is invisible, under-covering
        // clips. Mirrors the fixed scale in _borders.uss (border=1, border-N=N, per-side the same).
        internal static void BorderInset(string[] classNames, out float left, out float top)
        {
            left = 0f;
            top = 0f;
            if (classNames == null)
            {
                return;
            }
            foreach (var cls in classNames)
            {
                // Strip !important before peeling variants (a bang sits at the class edge, e.g. border-8! /
                // !border-8) so the border is still recognized under the important modifier.
                var leaf = StyleArbitraryValueResolver.StripImportant(cls, out _);
                while (StyleVariantClass.TryParse(leaf, out _, out var payload))
                {
                    leaf = payload;
                }
                if (TryParseBorderLeaf(leaf, out var affectsLeft, out var affectsTop, out var width))
                {
                    if (affectsLeft && width > left) { left = width; }
                    if (affectsTop && width > top) { top = width; }
                }
            }
        }

        // Parses one border-width leaf to which origin sides it insets and by how much. Returns false for
        // color / style / non-width border classes (border-red-500, border-dashed) and non-border classes.
        private static bool TryParseBorderLeaf(string leaf, out bool affectsLeft, out bool affectsTop, out float width)
        {
            affectsLeft = false;
            affectsTop = false;
            width = 0f;
            if (leaf == null)
            {
                return false;
            }
            if (leaf == "border")
            {
                affectsLeft = true;
                affectsTop = true;
                width = 1f;
                return true;
            }
            if (!leaf.StartsWith("border-", System.StringComparison.Ordinal))
            {
                return false;
            }
            var rest = leaf.Substring("border-".Length);
            // Per-side form: a side letter (t/r/b/l/x/y), optionally "-<width>". Only left-affecting (l, x) and
            // top-affecting (t, y) sides matter for the top-left origin; r/b are irrelevant to the shift.
            var c0 = rest.Length > 0 ? rest[0] : '\0';
            var isSide = (rest.Length == 1 || (rest.Length > 1 && rest[1] == '-')) && "trblxy".IndexOf(c0) >= 0;
            if (isSide)
            {
                var widthPart = rest.Length > 1 ? rest.Substring(2) : string.Empty;
                if (widthPart.Length == 0)
                {
                    width = 1f;
                }
                else if (!TryParseWidth(widthPart, out width))
                {
                    return false;
                }
                affectsLeft = c0 == 'l' || c0 == 'x';
                affectsTop = c0 == 't' || c0 == 'y';
                return affectsLeft || affectsTop;
            }
            // All-sides form: border-<width> / border-[Npx]. A color/style token fails TryParseWidth.
            if (TryParseWidth(rest, out width))
            {
                affectsLeft = true;
                affectsTop = true;
                return true;
            }
            return false;
        }

        // A border-width token to px: an integer (0/2/4/8/…) or an arbitrary bracket value. The bracket form
        // delegates to the resolver's own length grammar so it matches the width actually applied (px and rem;
        // a percent border width is meaningless and left unreserved — over-covering, never a clip).
        private static bool TryParseWidth(string s, out float width)
        {
            width = 0f;
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            if (s[0] == '[')
            {
                return StyleArbitraryValueResolver.TryParseArbitraryPixels(s, out width);
            }
            return int.TryParse(s, out var i) && i >= 0 && (width = i) >= 0f;
        }

        // The caster's laid-out size, or false when layout has not resolved one yet (pre-layout / EditMode,
        // where the spacer attaches unsized and the geometry callback re-sizes it). Shared by the skew and
        // shadow layers so the "laid out" gate cannot drift between them.
        internal static bool TryGetLayoutSize(VisualElement element, out float w, out float h)
        {
            w = element.layout.width;
            h = element.layout.height;
            return w > 0f && h > 0f && !float.IsNaN(w) && !float.IsNaN(h);
        }

        // Shifts a paint AABB (in the caster's border-box gVC space) into the padding-box space an absolute
        // child's left/top resolve against, by insetting its origin by the caster's border. Keeps the size, so
        // Sync's `xMin - Slack` / `xMax + Slack` land the coverage correctly in border-box space. A no-op for an
        // empty AABB (pre-layout) or a zero border.
        internal static Rect ShiftToPaddingBox(Rect aabbLocal, float borderLeft, float borderTop)
        {
            if (aabbLocal.width <= 0f || aabbLocal.height <= 0f)
            {
                return aabbLocal;
            }
            return new Rect(aabbLocal.xMin - borderLeft, aabbLocal.yMin - borderTop, aabbLocal.width, aabbLocal.height);
        }

        // Axis-aligned union of two rects.
        internal static Rect Union(Rect a, Rect b)
        {
            var xMin = Mathf.Min(a.xMin, b.xMin);
            var yMin = Mathf.Min(a.yMin, b.yMin);
            var xMax = Mathf.Max(a.xMax, b.xMax);
            var yMax = Mathf.Max(a.yMax, b.yMax);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }
    }
}
