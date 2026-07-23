using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Pure geometry for the `overline` text-decoration's painted rule (see TextOverlineBinding for the
    // generateVisualContent attach/detach that consumes it). Painter-free so ComputeGeometry is
    // unit-testable headless — a Painter2D cannot be read back — mirroring DashedBorderPainter's own split
    // between pure math (ComputeSegments) and the live-Painter2D stroke call (StrokeDashed).
    internal static class TextOverlinePainter
    {
        // The rule sits this far below the FIRST line's top edge, as a fraction of font size. UI Toolkit
        // exposes no public, synchronously-reachable ascent metric for an arbitrary Font/FontAsset (the
        // per-glyph vertex hook, PostProcessTextVertices, runs job-dependent and out of sync with
        // generateVisualContent), so this is a documented approximation rather than a measured ascent line:
        // most fonts carry some internal leading above the ascender within their em-square, and CSS specifies
        // overline at the ascent line, not the very top of the line box — nudging down by 15% of the font
        // size lands near that ascent line for typical font metrics without needing per-font data.
        private const float TopOffsetFraction = 0.15f;

        // Typical browsers stroke an underline/overline at roughly 1/16th of the font size; floored at 1px
        // so a small font size never thins the rule to an invisible sub-pixel stroke.
        private const float ThicknessDivisor = 16f;
        private const float MinThicknessPx = 1f;

        // Exposed separately from TryComputeGeometry so a caller (or a test) can pin the thickness rule on
        // its own, independent of a content rect.
        public static float ResolveThickness(float fontSize) => Mathf.Max(MinThicknessPx, fontSize / ThicknessDivisor);

        // Computes the rule's endpoints and stroke width, in the element-local space generateVisualContent
        // paints in (the same space contentRect itself is expressed in). Width is the text's natural
        // single-line-equivalent width clamped to the content box (v1 scope: one rule above the FIRST line
        // only — per-line metrics of wrapped text are not reachable here, see the type remarks on
        // TextOverlineBinding). textBlockHeight is the whole (possibly wrapped, multi-line) text block's
        // measured height — needed by ResolveFirstLineTop to find where a Middle/Lower-anchored block (and so
        // its first line) sits, exactly the way measuredWidth feeds ResolveStartX for the horizontal anchor.
        // Both measurements are supplied by the caller (Draw), never taken here, so this stays a pure
        // function — a Painter2D cannot be read back, and MeasureTextSize needs a live TextElement, so neither
        // belongs in a function a headless unit test must be able to call directly. Returns false (nothing to
        // draw) for a degenerate content box; the caller skips painting in that case.
        public static bool TryComputeGeometry(Rect contentRect, float measuredWidth, float textBlockHeight,
            float fontSize, TextAnchor align, out Vector2 from, out Vector2 to, out float lineWidth)
        {
            from = default;
            to = default;
            lineWidth = 0f;
            if (contentRect.width <= 0f || contentRect.height <= 0f
                || float.IsNaN(contentRect.width) || float.IsNaN(contentRect.height))
            {
                return false;
            }

            var width = Mathf.Clamp(measuredWidth, 0f, contentRect.width);
            var x0 = ResolveStartX(contentRect, width, align);
            var firstLineTop = ResolveFirstLineTop(contentRect, textBlockHeight, align);
            var y = firstLineTop + (fontSize * TopOffsetFraction);
            lineWidth = ResolveThickness(fontSize);
            from = new Vector2(x0, y);
            to = new Vector2(x0 + width, y);
            return true;
        }

        // Honors -unity-text-align for the run's horizontal start exactly like the engine's own text run:
        // left starts at the content box's own left edge, center splits the leftover space evenly, right
        // ends flush with the content box's right edge.
        private static float ResolveStartX(Rect contentRect, float width, TextAnchor align)
        {
            switch (align)
            {
                case TextAnchor.UpperCenter:
                case TextAnchor.MiddleCenter:
                case TextAnchor.LowerCenter:
                    return contentRect.xMin + ((contentRect.width - width) * 0.5f);
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    return contentRect.xMax - width;
                default: // UpperLeft / MiddleLeft / LowerLeft
                    return contentRect.xMin;
            }
        }

        // Honors -unity-text-align's VERTICAL component for where the text BLOCK sits in the content box, then
        // returns the FIRST line's top edge within it (v1 scope: the rule sits above the first line only, see
        // the type remarks on TextOverlineBinding). Velvet's own text-center/-left/-right/-start/-end utilities
        // all resolve to a middle-* anchor (see _typography.uss), which is also UI Toolkit's own unstyled
        // default, so the Middle branch is the common case, not an edge case: an Upper* anchor top-aligns the
        // block to the content box (first line's top == contentRect.yMin, the only case v0 ever computed); a
        // Middle* anchor centers the whole block, so it spans from contentRect.center.y - textBlockHeight / 2
        // to + textBlockHeight / 2 — the first of those is the first line's top; a Lower* anchor bottom-aligns
        // the block (block bottom == contentRect.yMax), so the first line's top is textBlockHeight above that.
        // Lines stack downward within the block regardless of how the block itself is anchored, so "first
        // line's top" is always the block's own top edge, one formula per anchor group.
        private static float ResolveFirstLineTop(Rect contentRect, float textBlockHeight, TextAnchor align)
        {
            switch (align)
            {
                case TextAnchor.MiddleLeft:
                case TextAnchor.MiddleCenter:
                case TextAnchor.MiddleRight:
                    return contentRect.center.y - (textBlockHeight * 0.5f);
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerCenter:
                case TextAnchor.LowerRight:
                    return contentRect.yMax - textBlockHeight;
                default: // UpperLeft / UpperCenter / UpperRight
                    return contentRect.yMin;
            }
        }

        // Strokes the already-computed rule onto a live Painter2D. A no-op for a transparent color or a
        // degenerate width, mirroring DashedBorderPainter.StrokeDashed's own guards. Butt caps (not round)
        // so the painted length matches the computed width exactly — a round cap would bleed half the line
        // width past each end.
        public static void Stroke(Painter2D painter, Vector2 from, Vector2 to, float lineWidth, Color color)
        {
            if (color.a <= 0.004f || lineWidth <= 0.01f)
            {
                return;
            }
            painter.strokeColor = color;
            painter.lineWidth = lineWidth;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }
    }
}
