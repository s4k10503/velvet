using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A receiver for the rounded-rect outline segments the shared builder emits. Two implementations: a
    // Painter2D passthrough (the skew / shadow fill + stroke path, byte-identical to the old inline builder)
    // and a polyline flattener (the dashed-border path, which needs queryable points a Painter2D cannot give
    // back). Only the primitive path ops are exposed — BeginPath / ClosePath stay Painter2D-specific and the
    // dash marcher closes the loop itself with an explicit `closed` flag.
    internal interface IRoundedRectPathSink
    {
        void MoveTo(Vector2 p);
        void LineTo(Vector2 p);
        void BezierCurveTo(Vector2 c1, Vector2 c2, Vector2 end);
    }

    // Shared face-painting kernel for the wrapper-less silhouette layers (skew + drop shadow). Both layers
    // suppress an element's native rectangular background / border (UI Toolkit draws those at a fixed point in
    // its pipeline, BEFORE generateVisualContent, so a painter that needs to sit behind OR ahead of the fill —
    // a sheared face, an offset shadow halo — cannot interleave with the native chrome) and REPAINT that chrome
    // in the element's own generateVisualContent: SkewSilhouette repaints it sheared; DropShadowSilhouette
    // repaints it UPRIGHT (tanX = tanY = 0) on top of a shadow quad so the opaque fill covers the shadow's
    // interior. The two shared pieces are the captured-face STASH (sentinel suppression + color capture +
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
        internal static bool IsSentinel(Color c)
            => c.r == SuppressedColor.r && c.g == SuppressedColor.g
            && c.b == SuppressedColor.b && c.a == SuppressedColor.a;

        internal static bool IsUnset(Color c) => c.r == 0f && c.g == 0f && c.b == 0f && c.a == 0f;

        private const float Kappa = 0.5522848f; // cubic-bezier quarter-circle constant

        // CSS skew about the box center (transform-origin: center, the default skew origin):
        // x' = x + (y − h/2)·tan(θx), y' = y + (x − w/2)·tan(θy). With tanX = tanY = 0 this is the identity, so
        // the same path builder draws an upright box (the drop-shadow fill) and a sheared one (the skew face).
        internal static Vector2 Shear(Vector2 p, float w, float h, float tanX, float tanY)
            => new(p.x + ((p.y - (h * 0.5f)) * tanX), p.y + ((p.x - (w * 0.5f)) * tanY));

        // Builds the element's rounded-rect path (inset on all sides) onto a Painter2D, shearing every point.
        // A thin wrapper over EmitShearedRoundedRectPath: BeginPath, emit the outline through a passthrough
        // sink, ClosePath — byte-identical to the old inline builder, so skew / shadow fill + stroke are
        // unchanged. tanX = tanY = 0 yields an upright rounded rect.
        internal static void BuildShearedRoundedRect(Painter2D p, VisualElement ve, float inset,
            float w, float h, float tanX, float tanY)
        {
            var rs = ve.resolvedStyle;
            p.BeginPath();
            EmitShearedRoundedRectPath(new Painter2DSink(p), inset, w, h, tanX, tanY,
                rs.borderTopLeftRadius, rs.borderTopRightRadius, rs.borderBottomRightRadius, rs.borderBottomLeftRadius);
            p.ClosePath();
        }

        // Flattens the element's rounded-rect outline into a closed polyline (each corner bezier sampled into
        // bezierSamples straight chords), for a caller that must walk the outline by arc length (the dashed
        // border). Reuses the caller's list to avoid a per-frame allocation.
        internal static void BuildShearedRoundedRectPolyline(List<Vector2> points, VisualElement ve, float inset,
            float w, float h, float tanX, float tanY, int bezierSamples = 8)
        {
            var rs = ve.resolvedStyle;
            BuildShearedRoundedRectPolyline(points, inset, w, h, tanX, tanY,
                rs.borderTopLeftRadius, rs.borderTopRightRadius, rs.borderBottomRightRadius, rs.borderBottomLeftRadius,
                bezierSamples);
        }

        // Explicit-radii overload — the outline built from radii the caller already knows, independent of a
        // laid-out element (so it is exercisable without a live panel).
        internal static void BuildShearedRoundedRectPolyline(List<Vector2> points, float inset,
            float w, float h, float tanX, float tanY,
            float radTL, float radTR, float radBR, float radBL, int bezierSamples = 8)
        {
            points.Clear();
            EmitShearedRoundedRectPath(new PolylineSink(points, bezierSamples), inset, w, h, tanX, tanY,
                radTL, radTR, radBR, radBL);
        }

        // Emits the element's rounded-rect outline (inset on all sides) into a sink, shearing every point — a
        // shear is affine, so transforming the bezier control points transforms the curve exactly. tanX =
        // tanY = 0 yields an upright rounded rect. The generic constraint keeps a struct sink (Painter2DSink)
        // boxing-free on the per-frame fill/stroke path.
        internal static void EmitShearedRoundedRectPath<TSink>(TSink sink, float inset,
            float w, float h, float tanX, float tanY,
            float radTL, float radTR, float radBR, float radBL) where TSink : IRoundedRectPathSink
        {
            var x0 = inset;
            var y0 = inset;
            var x1 = w - inset;
            var y1 = h - inset;
            var maxR = Mathf.Min(x1 - x0, y1 - y0) * 0.5f;
            var tl = Mathf.Clamp(radTL - inset, 0f, maxR);
            var tr = Mathf.Clamp(radTR - inset, 0f, maxR);
            var br = Mathf.Clamp(radBR - inset, 0f, maxR);
            var bl = Mathf.Clamp(radBL - inset, 0f, maxR);

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

    // The captured native-face colors of one element, with the sentinel-suppression machinery that hides the
    // native rectangular background/border so a silhouette layer can repaint them. The inline bg/border slots
    // are shared with the arbitrary-value resolver, so the stash must re-sync on every patch: a fresh resolver
    // value re-stashes directly; a USS-DRIVEN stash releases the sentinel on a class change and re-stashes on
    // the next style resolution, while an INLINE-driven stash is kept (an inline color only changes through a
    // resolver write, which the re-stash branch already catches). Owned by a SkewBinding / DropShadowBinding /
    // BorderStyleBinding; its lifecycle (TryStash on attach, SyncOnPatch on patch, Release on detach) is driven
    // by that binding.
    //
    // includeBackground gates every background touch: the skew / shadow layers own the whole face (fill +
    // border), so they suppress and repaint both (default true); the border-dashed layer restyles only the
    // border, so it constructs with includeBackground:false and never suppresses or re-captures the fill.
    internal sealed class SilhouetteFaceStash
    {
        // The captured face colors (valid while HasStash). Border uses the LEFT side: a single uniform stroke,
        // so per-side border colors/widths collapse to one.
        public Color BgColor;
        public Color BorderColor;
        public bool HasStash;

        // Whether the captured background came from an INLINE value (the resolver's bg-[…] write) rather than a
        // USS rule. An inline color is authoritative and only ever changes via another resolver write — which
        // overwrites the sentinel and is caught by the "resolver overwrote us" branch — so an inline-driven stash
        // must NOT be released just because some unrelated class changed (doing so dropped the fill and brought
        // the native-rectangle double image back). Only a USS-driven stash can change beneath the sentinel.
        public bool BgFromInline;

        // The border equivalent of BgFromInline: whether the captured border came from an inline border-[…]
        // write rather than a USS rule. Same authority rule — an inline-driven border stash is kept across an
        // unrelated class change (it only changes via a resolver write, caught by the re-stash branch).
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

            // Release-and-re-stash ONLY when NEITHER owned face is inline-driven: releasing nulls the shared
            // inline slots, so for an inline-driven face that would drop the authored color (the double-image
            // regression). Documented limitation of the shared-slot scheme: a MIXED element (one inline face +
            // one USS face) whose USS face color changes beneath the sentinel is not picked up until the next
            // resolver write — a full per-face release would need separable suppression. (For a border-only
            // stash BgFromInline stays false, so this reduces to the border authority test.)
            if (classesChanged && !BgFromInline && !BorderFromInline)
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
            CaptureFace(element.style.borderLeftColor.value, element.resolvedStyle.borderLeftColor,
                ref BorderColor, ref BorderFromInline);
            HasStash = true;
        }

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
            BorderFromInline = source.BorderFromInline;
            HasStash = true;
        }
    }
}
