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
            // While a layoutId projection draws the element's corners, its radius slots hold none and the corners
            // it draws take a radius per axis (ProjectedCornerFace).
            if (ProjectedCornerFace.TryGetRadii(ve, out var projected))
            {
                return new ShearedRoundedRect
                {
                    Width = w, Height = h, TanX = tanX, TanY = tanY, Inset = inset, Radii = projected,
                };
            }
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

        // Repaints a border over the face in the line style the element gives it, a dashed or dotted one side by side
        // (DashedBorderPainter). A side a dashed divider paints is left to it (DivideDashPainter), as the dashed layer
        // leaves it (BorderStyleSilhouette.PaintsSide).
        internal static void PaintBorder(Painter2D p, VisualElement ve, in BorderSides sides, BorderLineStyle style,
            in Shearing box)
        {
            var divided = DivideDashPainter.BoundEdge(ve);
            if (style == BorderLineStyle.Solid)
            {
                PaintBorder(p, ve, divided is { } edge ? Without(sides, edge) : sides, box);
                return;
            }
            if (BorderStyleSilhouette.BoxOf(ve) is not { } outline)
            {
                return;
            }
            foreach (var side in s_borderSides)
            {
                if (side != divided)
                {
                    DashedBorderPainter.PaintSide(p, side, outline, sides.ColorOf(side), style, box);
                }
            }
        }

        private static readonly DivideEdge[] s_borderSides =
            { DivideEdge.Top, DivideEdge.Right, DivideEdge.Bottom, DivideEdge.Left };

        private static BorderSides Without(in BorderSides sides, DivideEdge edge) => new()
        {
            Left = sides.Left,
            Top = sides.Top,
            Right = sides.Right,
            Bottom = sides.Bottom,
            LeftColor = edge == DivideEdge.Left ? Color.clear : sides.LeftColor,
            TopColor = edge == DivideEdge.Top ? Color.clear : sides.TopColor,
            RightColor = edge == DivideEdge.Right ? Color.clear : sides.RightColor,
            BottomColor = edge == DivideEdge.Bottom ? Color.clear : sides.BottomColor,
        };

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
    // masks it, so it is read from the rules beneath the inline slots (StyleCascade.FaceColor), whichever selector
    // matched, and carried onto each colour they give on the element's transition of it (ColorCarry). The
    // colours are read again only once the rules matched to the element change (StyleCascade.RulesHash), and the
    // panel's check repaints the element while a colour is carried or the rules give a new one.
    //
    // includeBackground gates every background touch: the skew / shadow layers own the whole face (fill +
    // border), so they suppress and repaint both (default true); the border-dashed layer restyles only the
    // border, so it constructs with includeBackground:false and never suppresses or re-captures the fill.
    internal sealed class SilhouetteFaceStash
    {
        // The sides of StyleCascade.FaceColor's border slots, 1 through 4, and the property each slot transitions.
        private static readonly DivideEdge[] s_slotSides = { DivideEdge.Top, DivideEdge.Right, DivideEdge.Bottom, DivideEdge.Left };
        private static readonly string[] s_slotProperties =
            { "background-color", "border-top-color", "border-right-color", "border-bottom-color", "border-left-color" };

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

        // The element whose rules colour the face while this suppresses it, each slot's colour carried in
        // StyleCascade.FaceColor's order, the key of the rules matched to it when those colours were last read
        // (StyleCascade.RulesHash; null where it cannot be read, which reads them every time), and the panel whose
        // per-frame check carries them.
        private VisualElement? _element;
        private readonly ColorCarry[] _carries = { new(), new(), new(), new(), new() };
        private long? _rulesHash;
        private IPanel? _checkedBy;

        public SilhouetteFaceStash(bool includeBackground = true)
        {
            _includeBackground = includeBackground;
        }

        // The colors to paint: where the rules colour the face, theirs as carried, else the captured one.
        public Color CurrentBg => BgFromInline ? BgColor : Current(0, BgColor);

        public Color CurrentBorder => CurrentBorderOf(DivideEdge.Left);

        // A side whose captured color came inline, or from a dashed divider's mask, keeps it.
        public Color CurrentBorderOf(DivideEdge side)
            => _sideKept[(int)side]
                ? BorderColors[(int)side]
                : Current(System.Array.IndexOf(s_slotSides, side) + 1, BorderColors[(int)side]);

        // Indexed like BorderColors: the sides the rules do not colour, and of those the ones taken from an inline
        // value.
        private readonly bool[] _sideKept = new bool[4];
        private readonly bool[] _sideInline = new bool[4];

        // A slot's colour carried to what the rules give it, else to captured where they cannot be read; captured
        // alone while this suppresses no element.
        private Color Current(int slot, Color captured)
        {
            if (_element == null)
            {
                return captured;
            }
            Refresh(_element);
            return _carries[slot].Value;
        }

        // Reads the colours the rules give the face again where the rules matched to element changed since the
        // last read, each then carried on the element's transition of it; true where it read them.
        private bool Refresh(VisualElement element)
        {
            EnsureChecked(element);
            var hash = StyleCascade.RulesHash(element);
            if (hash != null && hash == _rulesHash)
            {
                return false;
            }
            _rulesHash = hash;
            for (var slot = 0; slot < _carries.Length; slot++)
            {
                if (RulesColor(slot))
                {
                    _carries[slot].Step(StyleCascade.FaceColor(element, slot) ?? Captured(slot), 0f, element,
                        s_slotProperties[slot], Shorthand(slot));
                }
            }
            return true;
        }

        private Color Captured(int slot) => slot == 0 ? BgColor : BorderColors[(int)s_slotSides[slot - 1]];

        private static string? Shorthand(int slot) => slot == 0 ? null : "border-color";

        private bool RulesColor(int slot)
            => slot == 0 ? _includeBackground && !BgFromInline : !_sideKept[(int)s_slotSides[slot - 1]];

        // A class on element changed outside a reconcile pass — a variant payload, a gesture or drag class, a
        // play — so the face its suppression hides may have moved.
        // A per-frame driver wrote or released a slot this suppresses (StyleArbitraryValueResolver.ApplyDriven,
        // ReleaseDriven), or the element's own inline values were written back over it as a play ended: the face
        // takes what the slots hold and the sentinel goes back, as SyncOnPatch does for a resolver write.
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
        // - the class list changed → the element repaints, reading the face its rules now give;
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
                element.MarkDirtyRepaint();
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
        //   sentinel there (an inline value just cleared), which keeps the previous capture.
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
            Watch(element);
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

        // Releases the suppression and clears the stash, handing the slots back to the element: a colour captured
        // from an inline value is written back and the rest are nulled, so the native chrome resolves again and an
        // inline bg-[…] or border-[…] stays the element's (a face handed from a shadow to a skew keeps it).
        public void Release(VisualElement element)
        {
            if (_includeBackground)
            {
                element.style.backgroundColor = BgFromInline ? BgColor : StyleKeyword.Null;
            }
            foreach (var side in s_sides)
            {
                WriteBorderColor(element, side,
                    _sideInline[(int)side] ? BorderColors[(int)side] : new StyleColor(StyleKeyword.Null));
            }
            // A hold or a driver on a border color slot is not this stash's to drop: a divider's mask or color
            // goes back over the slot it nulled.
            StyleArbitraryValueResolver.ReassertBorderColors(element);
            SuppressionApplied = false;
            HasStash = false;
            Abandon(element);
        }

#pragma warning disable CS8524 // no discard arm: a new edge has to name the slot it writes
        private static void WriteBorderColor(VisualElement element, DivideEdge side, StyleColor value)
        {
            var style = element.style;
            _ = side switch
            {
                DivideEdge.Left => style.borderLeftColor = value,
                DivideEdge.Top => style.borderTopColor = value,
                DivideEdge.Right => style.borderRightColor = value,
                DivideEdge.Bottom => style.borderBottomColor = value,
            };
        }
#pragma warning restore CS8524

        // Drops the per-frame check and this stash's claim on element without touching the inline slots, for a layer
        // that hands its suppression on (BorderStyleSilhouette.DetachPreservingSuppression).
        public void Abandon(VisualElement element)
        {
            if (s_owners.TryGetValue(element, out var owner) && ReferenceEquals(owner, this))
            {
                s_owners.Remove(element);
            }
            Unregister();
            _element = null;
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

        // Takes element's face from the rules, the colours landed where they give them now, so that taking the face
        // is not a change they transition.
        private void Watch(VisualElement element)
        {
            if (ReferenceEquals(_element, element))
            {
                return;
            }
            _element = element;
            _rulesHash = StyleCascade.RulesHash(element);
            for (var slot = 0; slot < _carries.Length; slot++)
            {
                _carries[slot].Land(StyleCascade.FaceColor(element, slot) ?? Captured(slot));
            }
            EnsureChecked(element);
        }

        private sealed class PanelCheck
        {
            public readonly List<SilhouetteFaceStash> Stashes = new();
            public IVisualElementScheduledItem Item = null!;
        }

        private static readonly ConditionalWeakTable<IPanel, PanelCheck> s_checks = new();

        // Every face a panel shows is carried by one check run each frame among the panel's scheduled items, as a
        // dashed divider's mask is checked (DivideDashPainter.EnsureCheck).
        private void EnsureChecked(VisualElement element)
        {
            var panel = element.panel;
            if (panel == null || ReferenceEquals(panel, _checkedBy))
            {
                return;
            }
            Unregister();
            if (!s_checks.TryGetValue(panel, out var check))
            {
                var created = new PanelCheck();
                created.Item = panel.visualTree.schedule.Execute(ts => Check(panel, created, ts.deltaTime / 1000f))
                    .Every(0);
                s_checks.Add(panel, created);
                check = created;
            }
            check.Stashes.Add(this);
            check.Item.Resume();
            _checkedBy = panel;
        }

        private void Unregister()
        {
            if (_checkedBy != null)
            {
                if (s_checks.TryGetValue(_checkedBy, out var check))
                {
                    check.Stashes.Remove(this);
                }
            }
            _checkedBy = null;
        }

        private static void Check(IPanel panel, PanelCheck check, float dtSec)
        {
            for (var i = check.Stashes.Count - 1; i >= 0; i--)
            {
                var stash = check.Stashes[i];
                var element = stash._element;
                if (element == null || element.panel != panel)
                {
                    stash._checkedBy = null;
                    check.Stashes.RemoveAt(i);
                    continue;
                }
                if (stash.Advance(element, dtSec))
                {
                    element.MarkDirtyRepaint();
                }
            }
            if (check.Stashes.Count == 0)
            {
                check.Item.Pause();
            }
        }

        // Carries each colour the rules give over dtSec more; true where one moved or the rules gave a new one.
        private bool Advance(VisualElement element, float dtSec)
        {
            var moved = Refresh(element);
            for (var slot = 0; slot < _carries.Length; slot++)
            {
                var carry = _carries[slot];
                if (RulesColor(slot) && !carry.Landed)
                {
                    carry.Step(carry.Target, dtSec, element, s_slotProperties[slot], Shorthand(slot));
                    moved = true;
                }
            }
            return moved;
        }
    }
}
