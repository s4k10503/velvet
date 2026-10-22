using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A receiver for the rounded-rect outline segments the shared builder emits. Two implementations: a
    // Painter2D passthrough (the skew / shadow fill + stroke path, whose call sequence must exactly match a
    // direct Painter2D implementation so routing through this shared interface changes nothing visually) and
    // a polyline flattener (the dashed-border path, which needs queryable points a Painter2D cannot give
    // back). Only the primitive path ops are exposed — BeginPath / ClosePath stay Painter2D-specific and the
    // dash marcher closes the loop itself with an explicit `closed` flag.
    internal interface IRoundedRectPathSink
    {
        void MoveTo(Vector2 p);
        void LineTo(Vector2 p);
        void BezierCurveTo(Vector2 c1, Vector2 c2, Vector2 end);
    }

    // Inset shrinks the corner radii as well as the edges. The radii travel as VALUES rather than as an
    // element reference, so the outline builders need no laid-out element and stay exercisable without a live
    // panel; ForElement is the convenience for a caller that has one.
    internal readonly struct ShearedRoundedRect
    {
        private readonly CornerRadii _radii;

        public float Width { get; init; }
        public float Height { get; init; }
        public float TanX { get; init; }
        public float TanY { get; init; }
        public float Inset { get; init; }

        // Each of these four sets both axes of its corner.
        public float RadiusTopLeft
        {
            init => _radii = _radii with { TopLeft = new Vector2(value, value) };
        }

        public float RadiusTopRight
        {
            init => _radii = _radii with { TopRight = new Vector2(value, value) };
        }

        public float RadiusBottomRight
        {
            init => _radii = _radii with { BottomRight = new Vector2(value, value) };
        }

        public float RadiusBottomLeft
        {
            init => _radii = _radii with { BottomLeft = new Vector2(value, value) };
        }

        // Each corner's horizontal radius in x and its vertical one in y.
        public CornerRadii Radii
        {
            get => _radii;
            init => _radii = value;
        }

        public static ShearedRoundedRect ForElement(VisualElement ve, float inset,
            float w, float h, float tanX, float tanY)
        {
            var rs = ve.resolvedStyle;
            return new ShearedRoundedRect
            {
                Width = w,
                Height = h,
                TanX = tanX,
                TanY = tanY,
                Inset = inset,
                RadiusTopLeft = rs.borderTopLeftRadius,
                RadiusTopRight = rs.borderTopRightRadius,
                RadiusBottomRight = rs.borderBottomRightRadius,
                RadiusBottomLeft = rs.borderBottomLeftRadius,
            };
        }
    }

    // Shared face-painting kernel for the wrapper-less silhouette layers (skew + drop shadow). Both layers
    // suppress an element's native rectangular background / border (UI Toolkit draws those at a fixed point in
    // its pipeline, BEFORE generateVisualContent, so a painter that needs to sit behind OR ahead of the fill —
    // a sheared face, an offset shadow halo — cannot interleave with the native chrome) and REPAINT that chrome
    // in the element's own generateVisualContent: SkewSilhouette repaints it sheared; BoxShadowSilhouette
    // repaints it UPRIGHT (tanX = tanY = 0) on top of the outer shadow quads, keeping the fill above their
    // anti-aliased cut edge. The two shared pieces are the captured-face STASH (sentinel suppression + color capture +
    // patch-time re-sync) and the rounded-rect path BUILDER (sheared, an upright box being the zero-shear case).
    internal static class SilhouetteFace
    {
        // Suppression sentinel: visually nothing at 1/255 alpha, but distinguishable from both the unset
        // default (clear) and any realistic authored color, so the patch-time sync can tell "our suppression is
        // still in place" from "the resolver overwrote the slot". The inline background/border slot is shared
        // with StyleArbitraryValueResolver's bg-[…]/border-[…] writes, so suppression must re-sync on patch.
        internal static readonly Color SuppressedColor = new(0f, 0f, 0f, 1f / 255f);

        // Bit-exact sentinel / unset tests. Unity's Color operator== is APPROXIMATE (epsilon ~1e-5), so a real
        // authored color landing in that narrow band around the sentinel would be misread as "our suppression is
        // still in place" and dropped. The suppression write stores SuppressedColor verbatim, so a per-channel
        // == compare still matches our own sentinel exactly while rejecting any near color.
        // A slot ProjectedCornerFace has cleared reads as suppressed as well, holding no colour of the element's own: a
        // divide-dashed child's border slot it clears keeps the colour StyleDivideManipulator captured.
        internal static bool IsSentinel(Color c)
            => Same(c, SuppressedColor) || Same(c, ProjectedCornerFace.Cleared);

        private static bool Same(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        internal static bool IsUnset(Color c) => c.r == 0f && c.g == 0f && c.b == 0f && c.a == 0f;

        private const float Kappa = 0.5522848f; // cubic-bezier quarter-circle constant

        // Tailwind's `skewX() skewY()` about the box center (transform-origin: center, the default skew origin):
        // with u = x − w/2 and v = y − h/2, x' = x + u·tanX·tanY + v·tanX and y' = y + u·tanY. This is the same
        // product BoxShadowBaker.Shear and the shadow shader's unshear use, so a shadow and the face it sits
        // behind shear alike. With tanX = tanY = 0 it is the identity, so the same path builder draws an upright
        // box and a sheared one.
        internal static Vector2 Shear(Vector2 p, float w, float h, float tanX, float tanY)
        {
            var u = p.x - (w * 0.5f);
            var v = p.y - (h * 0.5f);
            return new Vector2(p.x + (u * tanX * tanY) + (v * tanX), p.y + (u * tanY));
        }

        // Builds the rounded-rect path (inset on all sides) onto a Painter2D, shearing every point. A thin
        // wrapper over EmitShearedRoundedRectPath: BeginPath, emit the outline through a passthrough sink,
        // ClosePath, so skew / shadow fill + stroke rendering is unaffected by going through the shared sink
        // abstraction. tanX = tanY = 0 yields an upright rounded rect.
        internal static void BuildShearedRoundedRect(Painter2D p, in ShearedRoundedRect box)
        {
            p.BeginPath();
            EmitShearedRoundedRectPath(new Painter2DSink(p), in box);
            p.ClosePath();
        }

        // Flattens the rounded-rect outline into a closed polyline (each corner bezier sampled into
        // bezierSamples straight chords), for a caller that must walk the outline by arc length (the dashed
        // border). Reuses the caller's list to avoid a per-frame allocation.
        internal static void BuildShearedRoundedRectPolyline(List<Vector2> points, in ShearedRoundedRect box,
            int bezierSamples = 8)
        {
            points.Clear();
            EmitShearedRoundedRectPath(new PolylineSink(points, bezierSamples), in box);
        }

        internal static float MaxRadius(float width, float height) => Mathf.Min(width, height) * 0.5f;

        // The border a face layer repaints: the stash's colours once it holds them, else the live ones, which the
        // native chrome still shows on the frame before the first stash.
        internal static BorderSides BorderOf(VisualElement ve, SilhouetteFaceStash face)
        {
            var rs = ve.resolvedStyle;
            return face.HasStash
                ? face.Border(rs)
                : BorderSides.Of(rs, rs.borderLeftColor, rs.borderTopColor, rs.borderRightColor, rs.borderBottomColor);
        }

        // Repaints a border over the face. One the same on every side is a stroke centred on a half-width-inset path,
        // approximating CSS's inside border; any other is filled side by side (BorderBandPainter).
        internal static void PaintBorder(Painter2D p, VisualElement ve, in BorderSides sides, in Shearing box)
        {
            if (!sides.Uniform)
            {
                BorderBandPainter.Paint(p, ShearedRoundedRect.ForElement(ve, 0f, box.Width, box.Height, box.TanX, box.TanY), sides);
                return;
            }
            if (sides.Left <= 0.01f || sides.LeftColor.a <= 0.004f) return;
            BuildShearedRoundedRect(p,
                ShearedRoundedRect.ForElement(ve, sides.Left * 0.5f, box.Width, box.Height, box.TanX, box.TanY));
            p.strokeColor = sides.LeftColor;
            p.lineWidth = sides.Left;
            p.lineJoin = LineJoin.Miter;
            p.Stroke();
        }

        // Emits the rounded-rect outline (inset on all sides) into a sink, shearing every point — a shear is
        // affine, so transforming the bezier control points transforms the curve exactly. tanX = tanY = 0
        // yields an upright rounded rect. The generic constraint keeps a struct sink (Painter2DSink)
        // boxing-free on the per-frame fill/stroke path.
        internal static void EmitShearedRoundedRectPath<TSink>(TSink sink, in ShearedRoundedRect box)
            where TSink : IRoundedRectPathSink
        {
            var w = box.Width;
            var h = box.Height;
            var inset = box.Inset;
            // The radii are fitted to the element's box, as CSS fits the outer border edge, and the inset then
            // takes its own amount off each.
            var outer = NonNegative(box.Radii);
            var factor = CornerRadiusFit.ScaleFactor(w, h, outer);
            var radii = new CornerRadii
            {
                TopLeft = Shrunk(outer.TopLeft * factor, inset),
                TopRight = Shrunk(outer.TopRight * factor, inset),
                BottomRight = Shrunk(outer.BottomRight * factor, inset),
                BottomLeft = Shrunk(outer.BottomLeft * factor, inset),
            };
            EmitRoundedRect(sink, Rect.MinMaxRect(inset, inset, w - inset, h - inset), radii,
                new Shearing(w, h, box.TanX, box.TanY));
        }

        // The shear of a w x h box the path builders apply to every point.
        internal readonly struct Shearing
        {
            public Shearing(float width, float height, float tanX, float tanY)
            {
                Width = width;
                Height = height;
                TanX = tanX;
                TanY = tanY;
            }

            public float Width { get; }
            public float Height { get; }
            public float TanX { get; }
            public float TanY { get; }

            public static Shearing None => new(0f, 0f, 0f, 0f);

            public Vector2 Apply(float x, float y) => Shear(new Vector2(x, y), Width, Height, TanX, TanY);
        }

        // Emits the outline of rect rounded by radii, taken as given. A corner with no extent on one axis is square,
        // as CSS draws one.
        internal static void EmitRoundedRect<TSink>(TSink sink, Rect rect, in CornerRadii radii, in Shearing shear)
            where TSink : IRoundedRectPathSink
        {
            var x0 = rect.xMin;
            var y0 = rect.yMin;
            var x1 = rect.xMax;
            var y1 = rect.yMax;
            var tl = Square(radii.TopLeft);
            var tr = Square(radii.TopRight);
            var br = Square(radii.BottomRight);
            var bl = Square(radii.BottomLeft);

            sink.MoveTo(shear.Apply(x0 + tl.x, y0));
            sink.LineTo(shear.Apply(x1 - tr.x, y0));
            if (tr.x > 0f)
            {
                sink.BezierCurveTo(shear.Apply(x1 - tr.x + (Kappa * tr.x), y0), shear.Apply(x1, y0 + tr.y - (Kappa * tr.y)),
                    shear.Apply(x1, y0 + tr.y));
            }
            sink.LineTo(shear.Apply(x1, y1 - br.y));
            if (br.x > 0f)
            {
                sink.BezierCurveTo(shear.Apply(x1, y1 - br.y + (Kappa * br.y)), shear.Apply(x1 - br.x + (Kappa * br.x), y1),
                    shear.Apply(x1 - br.x, y1));
            }
            sink.LineTo(shear.Apply(x0 + bl.x, y1));
            if (bl.x > 0f)
            {
                sink.BezierCurveTo(shear.Apply(x0 + bl.x - (Kappa * bl.x), y1), shear.Apply(x0, y1 - bl.y + (Kappa * bl.y)),
                    shear.Apply(x0, y1 - bl.y));
            }
            sink.LineTo(shear.Apply(x0, y0 + tl.y));
            if (tl.x > 0f)
            {
                sink.BezierCurveTo(shear.Apply(x0, y0 + tl.y - (Kappa * tl.y)), shear.Apply(x0 + tl.x - (Kappa * tl.x), y0),
                    shear.Apply(x0 + tl.x, y0));
            }
        }

        private static CornerRadii NonNegative(in CornerRadii radii) => new()
        {
            TopLeft = NonNegative(radii.TopLeft),
            TopRight = NonNegative(radii.TopRight),
            BottomRight = NonNegative(radii.BottomRight),
            BottomLeft = NonNegative(radii.BottomLeft),
        };

        private static Vector2 NonNegative(Vector2 radius) => new(Mathf.Max(0f, radius.x), Mathf.Max(0f, radius.y));

        private static Vector2 Shrunk(Vector2 radius, float inset) =>
            new(Mathf.Max(0f, radius.x - inset), Mathf.Max(0f, radius.y - inset));

        private static Vector2 Square(Vector2 radius) => radius.x > 0f && radius.y > 0f ? radius : Vector2.zero;

        // Passthrough onto a Painter2D — the fill / stroke path. A struct so the generic emit call does not box.
        internal readonly struct Painter2DSink : IRoundedRectPathSink
        {
            private readonly Painter2D _p;
            public Painter2DSink(Painter2D p) => _p = p;
            public void MoveTo(Vector2 p) => _p.MoveTo(p);
            public void LineTo(Vector2 p) => _p.LineTo(p);
            public void BezierCurveTo(Vector2 c1, Vector2 c2, Vector2 end) => _p.BezierCurveTo(c1, c2, end);
        }

        // Flattens the outline into a list of points, sampling each corner bezier into bezierSamples chords so
        // the dash marcher can walk arc length. Holds the running position so a bezier segment starts where the
        // previous op ended.
        private sealed class PolylineSink : IRoundedRectPathSink
        {
            private readonly List<Vector2> _points;
            private readonly int _samples;
            private Vector2 _current;

            public PolylineSink(List<Vector2> points, int samples)
            {
                _points = points;
                _samples = Mathf.Max(1, samples);
            }

            public void MoveTo(Vector2 p)
            {
                _current = p;
                _points.Add(p);
            }

            public void LineTo(Vector2 p)
            {
                _current = p;
                _points.Add(p);
            }

            public void BezierCurveTo(Vector2 c1, Vector2 c2, Vector2 end)
            {
                var p0 = _current;
                for (var i = 1; i <= _samples; i++)
                {
                    var t = (float)i / _samples;
                    _points.Add(CubicBezier(p0, c1, c2, end, t));
                }
                _current = end;
            }

            private static Vector2 CubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
            {
                var u = 1f - t;
                return (u * u * u * p0) + (3f * u * u * t * p1) + (3f * u * t * t * p2) + (t * t * t * p3);
            }
        }
    }

    // The captured native-face colors of one element, with the sentinel-suppression machinery that hides the
    // native rectangular background/border so a silhouette layer can repaint them. The inline bg/border slots
    // are shared with the arbitrary-value resolver, so the stash must re-sync on every patch: a fresh resolver
    // value re-stashes directly; a USS-DRIVEN stash releases the sentinel on a class change and re-stashes on
    // the next style resolution, while an INLINE-driven stash is kept (an inline color only changes through a
    // resolver write, which the re-stash branch already catches). Owned by a SkewBinding / BoxShadowBinding /
    // BorderStyleBinding; its lifecycle (TryStash on attach, SyncOnPatch on patch, Release on detach) is driven
    // by that binding.
    //
    // includeBackground gates every background touch: the skew / shadow layers own the whole face (fill +
    // border), so they suppress and repaint both (default true); the border-dashed layer restyles only the
    // border, so it constructs with includeBackground:false and never suppresses or re-captures the fill.
    internal sealed class SilhouetteFaceStash
    {
        // The captured face colors (valid while HasStash), each border side its own; BorderColor is the left one.
        public Color BgColor;
        public Color BorderColor;
        public Color BorderTopColor;
        public Color BorderRightColor;
        public Color BorderBottomColor;
        public bool HasStash;

        // Whether the captured background came from an INLINE value (the resolver's bg-[…] write) rather than a
        // USS rule. An inline color is authoritative and only ever changes via another resolver write — which
        // overwrites the sentinel and is caught by the "resolver overwrote us" branch — so an inline-driven stash
        // must NOT be released just because some unrelated class changed (doing so dropped the fill and brought
        // the native-rectangle double image back). Only a USS-driven stash can change beneath the sentinel.
        public bool BgFromInline;

        // The border equivalent of BgFromInline: whether a captured border side came from an inline border-[…]
        // write rather than a USS rule. Same authority rule — an inline-driven border stash is kept across an
        // unrelated class change (it only changes via a resolver write, caught by the re-stash branch), and the
        // release that change would take nulls every side's slot, so one inline side is enough to keep it.
        public bool BorderFromInline;

        // Whether the sentinel suppression is currently written to the element's inline style.
        public bool SuppressionApplied;

        // Whether this stash owns (suppresses / repaints) the background face. False for a border-only layer.
        private readonly bool _includeBackground;

        public SilhouetteFaceStash(bool includeBackground = true)
        {
            _includeBackground = includeBackground;
        }

        // First capture: prefers a meaningful inline value (the resolver's bg-[…] write), falls back to the
        // resolved style when attached. Off-panel with no inline value there is nothing to read yet — the
        // caller's CustomStyleResolvedEvent / GeometryChangedEvent retries after attach. A border-only stash
        // probes the border slot instead of the background (which it does not own).
        public void TryStash(VisualElement element)
        {
            var inlineProbe = _includeBackground
                ? element.style.backgroundColor.value
                : element.style.borderLeftColor.value;
            if (SilhouetteFace.IsUnset(inlineProbe) && element.panel == null)
            {
                return;
            }
            Capture(element);
            ApplySuppression(element);
        }

        // Patch-time stash sync, running AFTER the per-patch styling is applied (so the resolver's inline writes
        // for this patch are already on the element). Three cases:
        // - an owned inline slot (border always; background only when included) holds a fresh (non-sentinel)
        //   value → the resolver overwrote us (e.g. border-[#fff] → border-[#f00]): re-capture and re-suppress;
        // - the owned slots still hold our sentinel, the class list changed, AND the stash is USS-driven → a
        //   USS-side color may have changed beneath the sentinel: release it and let the next style resolution
        //   re-stash;
        // - otherwise the stash is current. In particular an INLINE-driven stash is NEVER released here: an inline
        //   bg-[…]/border-[…] is authoritative and only changes through a resolver write (the first branch), so
        //   releasing it on an unrelated add-only class change would drop the fill and re-expose the native
        //   rectangle (the double-image regression), since the resolver does not re-write an inline value that was
        //   not itself removed.
        // canReleaseFace is the caller's promise that a re-stash will in fact follow, which only a reconcile pass
        // can make: this method runs on EVERY patch, so a later one re-enters with HasStash false and
        // re-captures. A variant re-sync cannot promise it — it moves no rect, so no GeometryChangedEvent fires;
        // Velvet's utilities consume var(--…) without declaring a custom property, so UI Toolkit dispatches no
        // CustomStyleResolvedEvent either; and no further patch need ever come. A release taken there is
        // permanent, and the native rectangle paints behind the silhouette for good.
        public void SyncOnPatch(VisualElement element, bool classesChanged, bool canReleaseFace)
        {
            if (!HasStash)
            {
                TryStash(element);
                return;
            }

            var borderReclaimed = !SilhouetteFace.IsSentinel(element.style.borderLeftColor.value);
            var bgReclaimed = _includeBackground && !SilhouetteFace.IsSentinel(element.style.backgroundColor.value);
            if (borderReclaimed || bgReclaimed)
            {
                Capture(element);
                ApplySuppression(element);
                return;
            }

            // Release-and-re-stash ONLY when NEITHER owned face is inline-driven: releasing nulls the shared
            // inline slots, so for an inline-driven face that would drop the authored color (the double-image
            // regression). Documented limitation of the shared-slot scheme: a MIXED element (one inline face +
            // one USS face) whose USS face color changes beneath the sentinel is not picked up until the next
            // resolver write — a full per-face release would need separable suppression. (For a border-only
            // stash BgFromInline stays false, so this reduces to the border authority test.) Withholding the
            // release costs only that same delay. The release is two-phase even where it IS allowed: the patch
            // taking it only clears the stash, and the re-capture lands on the patch AFTER, which is the first
            // to re-enter with HasStash false.
            if (classesChanged && canReleaseFace && !BgFromInline && !BorderFromInline)
            {
                Release(element);
                element.MarkDirtyRepaint();
            }
        }

        public void Capture(VisualElement element)
        {
            if (_includeBackground)
            {
                CaptureFace(element.style.backgroundColor.value, element.resolvedStyle.backgroundColor,
                    ref BgColor, ref BgFromInline);
            }
            var style = element.style;
            var resolved = element.resolvedStyle;
            bool left = BorderFromInline, top = false, right = false, bottom = false;
            CaptureFace(style.borderLeftColor.value, resolved.borderLeftColor, ref BorderColor, ref left);
            CaptureFace(style.borderTopColor.value, resolved.borderTopColor, ref BorderTopColor, ref top);
            CaptureFace(style.borderRightColor.value, resolved.borderRightColor, ref BorderRightColor, ref right);
            CaptureFace(style.borderBottomColor.value, resolved.borderBottomColor, ref BorderBottomColor, ref bottom);
            BorderFromInline = left || top || right || bottom;
            HasStash = true;
        }

        // The captured border, at the widths the element is laid out with.
        public BorderSides Border(IResolvedStyle resolved)
            => BorderSides.Of(resolved, BorderColor, BorderTopColor, BorderRightColor, BorderBottomColor);

        // Captures one face color from its shared inline slot. Three cases:
        // - a fresh non-sentinel inline value → the resolver's bg-[…]/border-[…] write; it is authoritative;
        // - the SENTINEL → our suppression is still on the slot, so resolvedStyle would read back the sentinel
        //   (inline overrides USS), not the real color. KEEP the previously-captured value instead of clobbering
        //   it with the sentinel — this is what lets a border-only re-capture not wipe a USS-driven fill;
        // - unset → a USS-driven color: read it from resolvedStyle.
        private static void CaptureFace(Color inline, Color resolved, ref Color stash, ref bool fromInline)
        {
            if (!SilhouetteFace.IsUnset(inline) && !SilhouetteFace.IsSentinel(inline))
            {
                stash = inline;
                fromInline = true;
            }
            else if (!SilhouetteFace.IsSentinel(inline))
            {
                stash = resolved;
                fromInline = false;
            }
            // else: the sentinel is still applied — keep the existing stash (resolvedStyle is masked by it).
        }

        public void ApplySuppression(VisualElement element)
        {
            if (_includeBackground)
            {
                element.style.backgroundColor = SilhouetteFace.SuppressedColor;
            }
            element.style.borderLeftColor = SilhouetteFace.SuppressedColor;
            element.style.borderRightColor = SilhouetteFace.SuppressedColor;
            element.style.borderTopColor = SilhouetteFace.SuppressedColor;
            element.style.borderBottomColor = SilhouetteFace.SuppressedColor;
            SuppressionApplied = true;
            element.MarkDirtyRepaint();
        }

        // Releases the suppression (nulls the shared inline slots so the native chrome resolves again) and
        // clears the stash. A pooled element thus restores its native background/border on detach.
        public void Release(VisualElement element)
        {
            if (_includeBackground)
            {
                element.style.backgroundColor = StyleKeyword.Null;
            }
            element.style.borderLeftColor = StyleKeyword.Null;
            element.style.borderRightColor = StyleKeyword.Null;
            element.style.borderTopColor = StyleKeyword.Null;
            element.style.borderBottomColor = StyleKeyword.Null;
            SuppressionApplied = false;
            HasStash = false;
        }

        // Adopts another stash's captured BORDER face for a face-ownership HANDOFF: when a border-only layer
        // defers to a skew / shadow layer that took the whole face in the same patch, the incoming layer's own
        // Capture ran while the outgoing layer's suppression sentinel masked the shared inline border slot, so it
        // could recover only its unset default — the real color lives in the outgoing stash. Transfer it here so
        // the sheared / shadowed face repaints the same border. Only fills a border the new owner did not capture
        // itself: if the resolver rewrote the border color in this same patch, the new owner read that fresh value
        // directly, so its stash already holds a real color and must not be clobbered with the outgoing (stale) one.
        public void AdoptBorderFace(SilhouetteFaceStash source)
        {
            if (!source.HasStash || !SilhouetteFace.IsUnset(BorderColor))
            {
                return;
            }
            BorderColor = source.BorderColor;
            BorderTopColor = source.BorderTopColor;
            BorderRightColor = source.BorderRightColor;
            BorderBottomColor = source.BorderBottomColor;
            BorderFromInline = source.BorderFromInline;
            HasStash = true;
        }
    }
}
