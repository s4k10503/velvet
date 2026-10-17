using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A receiver for the rounded-rect outline segments the shared builder emits. Two implementations: a
    // Painter2D passthrough (the skew fill + stroke path, whose call sequence must exactly match a
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
        public float Width { get; init; }
        public float Height { get; init; }
        public float TanX { get; init; }
        public float TanY { get; init; }
        public float Inset { get; init; }
        public float RadiusTopLeft { get; init; }
        public float RadiusTopRight { get; init; }
        public float RadiusBottomRight { get; init; }
        public float RadiusBottomLeft { get; init; }

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

    // Shared face-painting kernel for the wrapper-less silhouette layers. The skew and border-dashed layers
    // suppress an element's native background / border (UI Toolkit draws those at a fixed point in its pipeline,
    // BEFORE generateVisualContent, so a sheared face or a dashed outline cannot replace the native chrome) and
    // REPAINT it in the element's own generateVisualContent. The shared pieces are the captured-face STASH
    // (SilhouetteFaceStash) and the rounded-rect path BUILDER (sheared, an upright box being the zero-shear
    // case), which also outlines the border box the drop shadow is cut out of.
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
        internal static bool IsSentinel(Color c)
            => c.r == SuppressedColor.r && c.g == SuppressedColor.g
            && c.b == SuppressedColor.b && c.a == SuppressedColor.a;

        internal static bool IsUnset(Color c) => c.r == 0f && c.g == 0f && c.b == 0f && c.a == 0f;

        private const float Kappa = 0.5522848f; // cubic-bezier quarter-circle constant

        // CSS skew about the box center (transform-origin: center, the default skew origin):
        // x' = x + (y − h/2)·tan(θx), y' = y + (x − w/2)·tan(θy). With tanX = tanY = 0 this is the identity, so
        // the same path builder draws an upright box and a sheared one (the skew face).
        internal static Vector2 Shear(Vector2 p, float w, float h, float tanX, float tanY)
            => new(p.x + ((p.y - (h * 0.5f)) * tanX), p.y + ((p.x - (w * 0.5f)) * tanY));

        // Builds the rounded-rect path (inset on all sides) onto a Painter2D, shearing every point. A thin
        // wrapper over EmitShearedRoundedRectPath: BeginPath, emit the outline through a passthrough sink,
        // ClosePath, so skew fill + stroke rendering is unaffected by going through the shared sink
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

        // Emits the rounded-rect outline (inset on all sides) into a sink, shearing every point — a shear is
        // affine, so transforming the bezier control points transforms the curve exactly. tanX = tanY = 0
        // yields an upright rounded rect. The generic constraint keeps a struct sink (Painter2DSink)
        // boxing-free on the per-frame fill/stroke path.
        internal static void EmitShearedRoundedRectPath<TSink>(TSink sink, in ShearedRoundedRect box)
            where TSink : IRoundedRectPathSink
        {
            // Copied out of the by-reference box because the shear helper below is a local function, which
            // cannot capture an `in` parameter.
            var w = box.Width;
            var h = box.Height;
            var tanX = box.TanX;
            var tanY = box.TanY;

            var inset = box.Inset;
            var x0 = inset;
            var y0 = inset;
            var x1 = w - inset;
            var y1 = h - inset;
            // The radii are fitted to the element's box, as CSS fits the outer border edge, and the inset then
            // takes its own amount off each.
            var outerTl = Mathf.Max(0f, box.RadiusTopLeft);
            var outerTr = Mathf.Max(0f, box.RadiusTopRight);
            var outerBr = Mathf.Max(0f, box.RadiusBottomRight);
            var outerBl = Mathf.Max(0f, box.RadiusBottomLeft);
            var factor = CornerRadiusFit.ScaleFactor(w, h, CornerRadii.Circular(outerTl, outerTr, outerBr, outerBl));
            var tl = Mathf.Max(0f, (outerTl * factor) - inset);
            var tr = Mathf.Max(0f, (outerTr * factor) - inset);
            var br = Mathf.Max(0f, (outerBr * factor) - inset);
            var bl = Mathf.Max(0f, (outerBl * factor) - inset);

            Vector2 S(float x, float y) => Shear(new Vector2(x, y), w, h, tanX, tanY);

            sink.MoveTo(S(x0 + tl, y0));
            sink.LineTo(S(x1 - tr, y0));
            if (tr > 0f)
            {
                sink.BezierCurveTo(S(x1 - tr + (Kappa * tr), y0), S(x1, y0 + tr - (Kappa * tr)), S(x1, y0 + tr));
            }
            sink.LineTo(S(x1, y1 - br));
            if (br > 0f)
            {
                sink.BezierCurveTo(S(x1, y1 - br + (Kappa * br)), S(x1 - br + (Kappa * br), y1), S(x1 - br, y1));
            }
            sink.LineTo(S(x0 + bl, y1));
            if (bl > 0f)
            {
                sink.BezierCurveTo(S(x0 + bl - (Kappa * bl), y1), S(x0, y1 - bl + (Kappa * bl)), S(x0, y1 - bl));
            }
            sink.LineTo(S(x0, y0 + tl));
            if (tl > 0f)
            {
                sink.BezierCurveTo(S(x0, y0 + tl - (Kappa * tl)), S(x0 + tl - (Kappa * tl), y0), S(x0 + tl, y0));
            }
        }

        // Passthrough onto a Painter2D — the fill / stroke path. A struct so the generic emit call does not box.
        private readonly struct Painter2DSink : IRoundedRectPathSink
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
    // background/border so a silhouette layer can repaint them. Owned by a SkewBinding / BorderStyleBinding; its
    // lifecycle (TryStash on attach, SyncOnPatch on a class change, Release on detach) is driven by that binding.
    //
    // The inline bg/border slots are shared with the arbitrary-value resolver, and a fresh value there is
    // captured directly. A face a stylesheet rule colours cannot be read off the element while the sentinel
    // masks it, so it is read off a probe instead: a hidden child carrying the element's face-colouring and
    // transition classes, which resolves what the element itself would show. The probe
    // takes the class changes of a reconcile pass (SyncOnPatch), and of a variant payload, a gesture or drag
    // class and a play (NotifyClassesChanged).
    //
    // includeBackground gates every background touch: the skew layer owns the whole face (fill + border), so it
    // suppresses and repaints both (default true); the border-dashed layer restyles only the border, so it
    // constructs with includeBackground:false and never suppresses or re-captures the fill.
    internal sealed class SilhouetteFaceStash
    {
        // Marks the probe child, which SilhouetteBoundsSpacer.IsSpacer keeps out of every rendered-child count.
        internal const string ProbeMarkerClass = "velvet-face-probe";

        private static readonly StyleLonghandSet s_probeLonghands = StyleLonghandSet.Of(StyleLonghand.BackgroundColor)
            .Union(StyleLonghandSet.Of(StyleLonghand.BorderLeftColor))
            .Union(StyleLonghandSet.Of(StyleLonghand.TransitionProperty))
            .Union(StyleLonghandSet.Of(StyleLonghand.TransitionDuration))
            .Union(StyleLonghandSet.Of(StyleLonghand.TransitionTimingFunction))
            .Union(StyleLonghandSet.Of(StyleLonghand.TransitionDelay));

        // The stash whose suppression is on each element's inline slots, so a class change no reconcile pass
        // follows reaches it without a reconciler.
        private static readonly ConditionalWeakTable<VisualElement, SilhouetteFaceStash> s_owners = new();

        // The captured face colors (valid while HasStash). Border uses the LEFT side: a single uniform stroke,
        // so per-side border colors/widths collapse to one.
        public Color BgColor;
        public Color BorderColor;
        public bool HasStash;

        // Whether the captured background came from an INLINE value (the resolver's bg-[…] write) rather than a
        // USS rule. An inline color is authoritative and only ever changes via another resolver write, which
        // overwrites the sentinel and is caught by SyncOnPatch's re-capture branch.
        public bool BgFromInline;

        // The border equivalent of BgFromInline.
        public bool BorderFromInline;

        // Whether the sentinel suppression is currently written to the element's inline style.
        public bool SuppressionApplied;

        // Whether this stash owns (suppresses / repaints) the background face. False for a border-only layer.
        private readonly bool _includeBackground;

        private VisualElement? _probe;
        private int _probeTransitions;
        private int _repaintFrames;
        private IVisualElementScheduledItem? _repaintTicker;

        public SilhouetteFaceStash(bool includeBackground = true)
        {
            _includeBackground = includeBackground;
        }

        // The colors to paint: the probe's for a face a stylesheet rule colours once the probe has resolved, the
        // captured one otherwise. The probe's inline visibility reads back only after a style pass has run on it.
        public Color CurrentBg => !BgFromInline && ProbeResolved ? _probe!.resolvedStyle.backgroundColor : BgColor;

        public Color CurrentBorder
            => !BorderFromInline && ProbeResolved ? _probe!.resolvedStyle.borderLeftColor : BorderColor;

        private bool ProbeResolved
            => _probe?.panel != null && _probe.resolvedStyle.visibility == Visibility.Hidden;

        private bool HasStylesheetFace => (_includeBackground && !BgFromInline) || !BorderFromInline;

        // A class on element changed outside a reconcile pass — a variant payload, a gesture or drag class, a
        // play — so the face its suppression hides may have moved.
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

            var borderReclaimed = !SilhouetteFace.IsSentinel(element.style.borderLeftColor.value);
            var bgReclaimed = _includeBackground && !SilhouetteFace.IsSentinel(element.style.backgroundColor.value);
            if (borderReclaimed || bgReclaimed)
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

        public void Capture(VisualElement element)
        {
            if (_includeBackground)
            {
                CaptureFace(element.style.backgroundColor.value, element.resolvedStyle.backgroundColor,
                    ref BgColor, ref BgFromInline);
            }
            CaptureFace(element.style.borderLeftColor.value, element.resolvedStyle.borderLeftColor,
                ref BorderColor, ref BorderFromInline);
            HasStash = true;
        }

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
            if (_includeBackground)
            {
                element.style.backgroundColor = SilhouetteFace.SuppressedColor;
            }
            element.style.borderLeftColor = SilhouetteFace.SuppressedColor;
            element.style.borderRightColor = SilhouetteFace.SuppressedColor;
            element.style.borderTopColor = SilhouetteFace.SuppressedColor;
            element.style.borderBottomColor = SilhouetteFace.SuppressedColor;
            SuppressionApplied = true;
            s_owners.AddOrUpdate(element, this);
            SyncProbe(element);
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
        // defers to a skew layer that took the whole face in the same patch, the incoming layer's own Capture ran
        // while the outgoing layer's suppression sentinel masked the shared inline border slot, so it could
        // recover only its unset default — the real color lives in the outgoing stash. Transfer it here so the
        // sheared face repaints the same border. Only fills a border the new owner did not capture itself: if the
        // resolver rewrote the border color in this same patch, the new owner read that fresh value directly, so
        // its stash already holds a real color and must not be clobbered with the outgoing (stale) one.
        public void AdoptBorderFace(SilhouetteFaceStash source)
        {
            if (!source.HasStash || !SilhouetteFace.IsUnset(BorderColor))
            {
                return;
            }
            BorderColor = source.BorderColor;
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
            // Two frames: the style pass that resolves the probe can come after the next scheduler tick.
            _repaintFrames = 2;
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
