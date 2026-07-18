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

        // A little slack beyond the computed AABB absorbs the stroke half-width, antialiasing, and any
        // border/padding offset between the gVC origin and the absolute-positioning origin. Over-covering only
        // grows the offscreen texture slightly; it is never visible (the spacer paints nothing).
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
                spacer.style.left = aabbLocal.xMin - Slack;
                spacer.style.top = aabbLocal.yMin - Slack;
                spacer.style.width = aabbLocal.width + (2f * Slack);
                spacer.style.height = aabbLocal.height + (2f * Slack);
            }
        }

        // True when child is a bounds-spacer (the internal, reconciler-invisible render-bounds child).
        internal static bool IsSpacer(VisualElement child)
            => child != null && child.ClassListContains(MarkerClass);

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
        internal static int NonSpacerChildCount(VisualElement container)
        {
            var n = container.childCount;
            while (n > 0 && IsSpacer(container[n - 1]))
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

        // The caster's laid-out size, or false when layout has not resolved one yet (pre-layout / EditMode,
        // where the spacer attaches unsized and the geometry callback re-sizes it). Shared by the skew and
        // shadow layers so the "laid out" gate cannot drift between them.
        internal static bool TryGetLayoutSize(VisualElement element, out float w, out float h)
        {
            w = element.layout.width;
            h = element.layout.height;
            return w > 0f && h > 0f && !float.IsNaN(w) && !float.IsNaN(h);
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
