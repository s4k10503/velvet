using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        // Each of these four sets both axes of its corner and reads its horizontal one.
        public float RadiusTopLeft
        {
            get => _radii.TopLeft.x;
            init => _radii = _radii with { TopLeft = new Vector2(value, value) };
        }

        public float RadiusTopRight
        {
            get => _radii.TopRight.x;
            init => _radii = _radii with { TopRight = new Vector2(value, value) };
        }

        public float RadiusBottomRight
        {
            get => _radii.BottomRight.x;
            init => _radii = _radii with { BottomRight = new Vector2(value, value) };
        }

        public float RadiusBottomLeft
        {
            get => _radii.BottomLeft.x;
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

#pragma warning disable CS8524 // no discard arm: a new edge has to name the side it reads
        internal static StyleColor InlineBorderColor(VisualElement element, DivideEdge side) => side switch
        {
            DivideEdge.Left => element.style.borderLeftColor,
            DivideEdge.Top => element.style.borderTopColor,
            DivideEdge.Right => element.style.borderRightColor,
            DivideEdge.Bottom => element.style.borderBottomColor,
        };

        internal static Color ResolvedBorderColor(IResolvedStyle style, DivideEdge side) => side switch
        {
            DivideEdge.Left => style.borderLeftColor,
            DivideEdge.Top => style.borderTopColor,
            DivideEdge.Right => style.borderRightColor,
            DivideEdge.Bottom => style.borderBottomColor,
        };
#pragma warning restore CS8524

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

    // The face colors of one element, with the sentinel-suppression machinery that hides the native rectangular
    // background/border so a silhouette layer can repaint them. Owned by a SkewBinding / BoxShadowBinding /
    // BorderStyleBinding; its lifecycle (TryStash on attach, SyncOnPatch on a class change, Release on detach) is
    // driven by that binding.
    //
    // The inline bg/border slots are shared with the arbitrary-value resolver, and a fresh value there is
    // captured directly. A face a stylesheet rule colours cannot be read off the element while the sentinel
    // masks it, so it is read off a probe instead: a hidden child carrying the element's face-colouring and
    // transition classes, which resolves what the element itself would show. The probe
    // takes the class changes of a reconcile pass (SyncOnPatch), and of a variant payload, a gesture or drag
    // class and a play (NotifyClassesChanged).
    //
    // includeBackground gates every background touch: the skew / shadow layers own the whole face (fill +
    // border), so they suppress and repaint both (default true); the border-dashed layer restyles only the
    // border, so it constructs with includeBackground:false and never suppresses or re-captures the fill.
    internal sealed class SilhouetteFaceStash
    {
        // Marks the probe child, which SilhouetteBoundsSpacer.IsSpacer keeps out of every rendered-child count.
        internal const string ProbeMarkerClass = "velvet-face-probe";

        private static readonly StyleLonghandSet s_probeLonghands = StyleLonghandSet.Of(StyleLonghand.BackgroundColor)
            .Union(StyleLonghandSet.Of(StyleLonghand.BorderLeftColor))
            .Union(StyleLonghandSet.Of(StyleLonghand.BorderTopColor))
            .Union(StyleLonghandSet.Of(StyleLonghand.BorderRightColor))
            .Union(StyleLonghandSet.Of(StyleLonghand.BorderBottomColor))
            .Union(StyleLonghandSet.Of(StyleLonghand.TransitionProperty))
            .Union(StyleLonghandSet.Of(StyleLonghand.TransitionDuration))
            .Union(StyleLonghandSet.Of(StyleLonghand.TransitionTimingFunction))
            .Union(StyleLonghandSet.Of(StyleLonghand.TransitionDelay));

        // The stash whose suppression is on each element's inline slots, so a class change no reconcile pass
        // follows reaches it without a reconciler.
        private static readonly ConditionalWeakTable<VisualElement, SilhouetteFaceStash> s_owners = new();

        // The captured face colors (valid while HasStash), the border's one per side and indexed by DivideEdge;
        // BorderColor is the left side's. Widths are never suppressed, so a painter reads them off the resolved
        // style rather than from here.
        public Color BgColor;
        public readonly Color[] BorderColors = new Color[4];
        public bool HasStash;

        public Color BorderColor => BorderColors[(int)DivideEdge.Left];

        // Whether the captured background came from an INLINE value (the resolver's bg-[…] write) rather than a
        // USS rule. An inline color is authoritative and only ever changes via another resolver write, which
        // overwrites the sentinel and is caught by SyncOnPatch's re-capture branch.
        public bool BgFromInline;

        // The border equivalent of BgFromInline, true where any side's is inline.
        public bool BorderFromInline;

        // Whether the sentinel suppression is currently written to the element's inline style.
        public bool SuppressionApplied;

        // Whether this stash owns (suppresses / repaints) the background face. False for a border-only layer.
        private readonly bool _includeBackground;

        private const int RepaintFramesAfterSync = 2;

        private VisualElement? _probe;
        private int _probeTransitions;
        private int _repaintFrames;
        private IVisualElementScheduledItem? _repaintTicker;

        public SilhouetteFaceStash(bool includeBackground = true)
        {
            _includeBackground = includeBackground;
        }

        // The colors to paint: the probe's for a face a stylesheet rule colours once its resolved style reads back the
        // hidden visibility it is created with (ProbeResolved), the captured one otherwise.
        public Color CurrentBg => !BgFromInline && ProbeResolved ? _probe!.resolvedStyle.backgroundColor : BgColor;

        public Color CurrentBorder => CurrentBorderOf(DivideEdge.Left);

        // A side whose captured color came inline, or from a dashed divider's mask, keeps it.
        public Color CurrentBorderOf(DivideEdge side)
            => !_sideKept[(int)side] && ProbeResolved
                ? SilhouetteFace.ResolvedBorderColor(_probe!.resolvedStyle, side)
                : BorderColors[(int)side];

        // Indexed like BorderColors: the sides the probe does not stand in for, and of those the ones taken from an
        // inline value.
        private readonly bool[] _sideKept = new bool[4];
        private readonly bool[] _sideInline = new bool[4];

        private bool ProbeResolved
            => _probe?.panel != null && _probe.resolvedStyle.visibility == Visibility.Hidden;

        private bool HasStylesheetFace
            => (_includeBackground && !BgFromInline) || System.Array.IndexOf(_sideKept, false) >= 0;

        // A class on element changed outside a reconcile pass — a variant payload, a gesture or drag class, a
        // play — so the face its suppression hides may have moved.
        // A per-frame driver wrote or released a slot this suppresses (StyleArbitraryValueResolver.ApplyDriven,
        // ReleaseDriven), or the element's own inline values were written back over it as a play ended: the face
        // takes what the slots hold and the sentinel goes back, as SyncOnPatch does for a resolver write, without
        // re-pointing the probe, whose classes a drive leaves as they were.
        internal static void Retake(VisualElement element)
        {
            if (s_owners.TryGetValue(element, out var stash) && stash.SuppressionApplied && stash.HasStash
                && stash.Reclaimed(element))
            {
                stash.Capture(element);
                stash.WriteSentinels(element);
                element.MarkDirtyRepaint();
            }
        }

        // The colour the face shows in place of side, or of the background where side is null, while its sentinel
        // stands in that slot, and whether the face took it from an inline value; false where no face suppresses it.
        internal static bool TryReadFace(VisualElement element, DivideEdge? side, out Color color, out bool fromInline)
        {
            color = default;
            fromInline = false;
            if (!s_owners.TryGetValue(element, out var stash) || !stash.SuppressionApplied || !stash.HasStash)
            {
                return false;
            }
            if (side is not { } edge)
            {
                if (!stash._includeBackground || !SilhouetteFace.IsSentinel(element.style.backgroundColor.value))
                {
                    return false;
                }
                color = stash.CurrentBg;
                fromInline = stash.BgFromInline;
                return true;
            }
            if (!SilhouetteFace.IsSentinel(SilhouetteFace.InlineBorderColor(element, edge).value))
            {
                return false;
            }
            color = stash.CurrentBorderOf(edge);
            fromInline = stash._sideInline[(int)edge];
            return true;
        }

        internal static void NotifyClassesChanged(VisualElement element)
        {
            if (s_owners.TryGetValue(element, out var stash))
            {
                stash.SyncOnPatch(element, classesChanged: true);
            }
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

        // Class-change stash sync, running AFTER the change's styling is applied (so the resolver's inline writes
        // are already on the element). Three cases:
        // - an owned inline slot (border always; background only when included) holds a fresh (non-sentinel)
        //   value → the resolver overwrote us (e.g. border-[#fff] → border-[#f00]): re-capture and re-suppress;
        // - the class list changed → the probe takes the element's classes, so a stylesheet face follows them;
        // - otherwise the stash is current.
        public void SyncOnPatch(VisualElement element, bool classesChanged)
        {
            if (!HasStash)
            {
                TryStash(element);
                return;
            }

            if (Reclaimed(element))
            {
                Capture(element);
                ApplySuppression(element);
                return;
            }

            if (classesChanged)
            {
                SyncProbe(element);
            }
        }

        // Whether something other than this stash's sentinel stands in a slot it suppresses.
        private bool Reclaimed(VisualElement element)
        {
            foreach (var side in s_sides)
            {
                if (!SilhouetteFace.IsSentinel(SilhouetteFace.InlineBorderColor(element, side).value))
                {
                    return true;
                }
            }
            return _includeBackground && !SilhouetteFace.IsSentinel(element.style.backgroundColor.value);
        }

        public void Capture(VisualElement element)
        {
            if (_includeBackground)
            {
                CaptureFace(element.style.backgroundColor.value, element.resolvedStyle.backgroundColor,
                    ref BgColor, ref BgFromInline);
            }
            var anyInline = false;
            foreach (var side in s_sides)
            {
                var inline = SilhouetteFace.InlineBorderColor(element, side).value;
                var fromInline = false;
                // A dashed divider's mask is not this stash's suppression, so a first capture under it takes the
                // color the divider paints there rather than keeping a stash it never had.
                var underDash = false;
                if (!HasStash && SilhouetteFace.IsSentinel(inline)
                    && DivideDashPainter.ColorUnderMask(element, side) is { } underMask)
                {
                    BorderColors[(int)side] = underMask;
                    underDash = true;
                }
                else
                {
                    CaptureFace(inline, SilhouetteFace.ResolvedBorderColor(element.resolvedStyle, side),
                        ref BorderColors[(int)side], ref fromInline);
                }
                _sideKept[(int)side] = fromInline || underDash;
                _sideInline[(int)side] = fromInline;
                anyInline |= fromInline;
            }
            BorderFromInline = anyInline;
            HasStash = true;
        }

        private static readonly DivideEdge[] s_sides = { DivideEdge.Left, DivideEdge.Top, DivideEdge.Right, DivideEdge.Bottom };

        // The border to paint, at the widths the element is laid out with.
        public BorderSides Border(IResolvedStyle resolved) => BorderSides.Of(resolved,
            CurrentBorderOf(DivideEdge.Left), CurrentBorderOf(DivideEdge.Top), CurrentBorderOf(DivideEdge.Right),
            CurrentBorderOf(DivideEdge.Bottom));

        // Captures one face color from its shared inline slot. Three cases:
        // - a fresh non-sentinel inline value → the resolver's bg-[…]/border-[…] write; it is authoritative;
        // - the SENTINEL → our suppression is still on the slot, so resolvedStyle would read back the sentinel
        //   (inline overrides USS), not the real color. KEEP the previously-captured value instead of clobbering
        //   it with the sentinel — this is what lets a border-only re-capture not wipe a USS-driven fill;
        // - unset → a USS-driven color: read it from resolvedStyle, unless the last style pass still saw the
        //   sentinel there (an inline value just cleared), which keeps the previous capture until the probe
        //   resolves.
        private static void CaptureFace(Color inline, Color resolved, ref Color stash, ref bool fromInline)
        {
            if (!SilhouetteFace.IsUnset(inline) && !SilhouetteFace.IsSentinel(inline))
            {
                stash = inline;
                fromInline = true;
            }
            else if (!SilhouetteFace.IsSentinel(inline))
            {
                if (!SilhouetteFace.IsSentinel(resolved))
                {
                    stash = resolved;
                }
                fromInline = false;
            }
            // else: the sentinel is still applied — keep the existing stash (resolvedStyle is masked by it).
        }

        public void ApplySuppression(VisualElement element)
        {
            WriteSentinels(element);
            SuppressionApplied = true;
            s_owners.AddOrUpdate(element, this);
            SyncProbe(element);
            element.MarkDirtyRepaint();
        }

        private void WriteSentinels(VisualElement element)
        {
            if (_includeBackground)
            {
                element.style.backgroundColor = SilhouetteFace.SuppressedColor;
            }
            element.style.borderLeftColor = SilhouetteFace.SuppressedColor;
            element.style.borderRightColor = SilhouetteFace.SuppressedColor;
            element.style.borderTopColor = SilhouetteFace.SuppressedColor;
            element.style.borderBottomColor = SilhouetteFace.SuppressedColor;
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
            // A hold or a driver on a border color slot is not this stash's to drop: a divider's mask or color
            // goes back over the slot it nulled.
            StyleArbitraryValueResolver.ReassertBorderColors(element);
            SuppressionApplied = false;
            HasStash = false;
            Abandon(element);
        }

        // Drops the probe and this stash's claim on element without touching the inline slots, for a layer that
        // hands its suppression on (BorderStyleSilhouette.DetachPreservingSuppression).
        public void Abandon(VisualElement element)
        {
            if (s_owners.TryGetValue(element, out var owner) && ReferenceEquals(owner, this))
            {
                s_owners.Remove(element);
            }
            _repaintTicker?.Pause();
            _repaintTicker = null;
            _probeTransitions = 0;
            _probe?.RemoveFromHierarchy();
            _probe = null;
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
            source.BorderColors.CopyTo(BorderColors, 0);
            source._sideKept.CopyTo(_sideKept, 0);
            source._sideInline.CopyTo(_sideInline, 0);
            BorderFromInline = source.BorderFromInline;
            HasStash = true;
        }

        // Puts the element's face-colouring and transition classes, and its inline transition, on the probe, and
        // repaints the element once the probe has resolved them. A probe needs a panel to resolve, so an element
        // off one gets none until its next class change on one; the captured colors paint meanwhile.
        private void SyncProbe(VisualElement element)
        {
            if (!SuppressionApplied || !HasStylesheetFace || element.panel == null)
            {
                return;
            }
            var probe = _probe ??= CreateProbe(element);
            probe.ClearClassList();
            probe.AddToClassList(ProbeMarkerClass);
            foreach (var cls in element.GetClasses())
            {
                if (!StyleUtilityProperties.TryGet(cls, out var rule) || rule.Properties.Overlaps(s_probeLonghands))
                {
                    probe.AddToClassList(cls);
                }
            }
            probe.style.transitionProperty = element.style.transitionProperty;
            probe.style.transitionDuration = element.style.transitionDuration;
            probe.style.transitionTimingFunction = element.style.transitionTimingFunction;
            probe.style.transitionDelay = element.style.transitionDelay;
            _repaintFrames = RepaintFramesAfterSync;
            KeepRepainting(element);
        }

        private VisualElement CreateProbe(VisualElement element)
        {
            var probe = new VisualElement { name = ProbeMarkerClass, pickingMode = PickingMode.Ignore };
            probe.style.position = Position.Absolute;
            probe.style.visibility = Visibility.Hidden;
            probe.style.left = 0f;
            probe.style.top = 0f;
            probe.style.width = 0f;
            probe.style.height = 0f;
            probe.style.marginLeft = 0f;
            probe.style.marginRight = 0f;
            probe.style.marginTop = 0f;
            probe.style.marginBottom = 0f;
            probe.style.paddingLeft = 0f;
            probe.style.paddingRight = 0f;
            probe.style.paddingTop = 0f;
            probe.style.paddingBottom = 0f;
            probe.style.borderLeftWidth = 0f;
            probe.style.borderRightWidth = 0f;
            probe.style.borderTopWidth = 0f;
            probe.style.borderBottomWidth = 0f;
            probe.RegisterCallback<TransitionRunEvent>(_ =>
            {
                _probeTransitions++;
                KeepRepainting(element);
            });
            probe.RegisterCallback<TransitionEndEvent>(_ => EndTransition());
            probe.RegisterCallback<TransitionCancelEvent>(_ => EndTransition());
            element.hierarchy.Add(probe);
            return probe;
        }

        private void EndTransition()
        {
            _probeTransitions = Mathf.Max(0, _probeTransitions - 1);
            _repaintFrames = Mathf.Max(_repaintFrames, 1);
        }

        // Repaints element every frame while the probe has a transition running or a resolve still to land.
        private void KeepRepainting(VisualElement element)
        {
            if (_repaintTicker == null)
            {
                _repaintTicker = element.schedule.Execute(() => RepaintTick(element)).Every(0);
                return;
            }
            _repaintTicker.Resume();
        }

        private void RepaintTick(VisualElement element)
        {
            element.MarkDirtyRepaint();
            if (_probeTransitions > 0)
            {
                return;
            }
            if (--_repaintFrames <= 0)
            {
                _repaintTicker?.Pause();
            }
        }
    }
}
