using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one leaf TextElement's PAINTED overline (the Decoration axis's
    // Overline value — see TextDecorationKind), keyed in ReconcilerContext.TextOverlineBindings by the
    // element itself. Unlike BorderStyleBinding there is no native property to suppress and nothing to
    // stash: the rule is purely ADDITIVE paint above the glyph run, so the binding holds only the
    // registered callback (needed so Detach can unhook it) — every value the paint itself needs (text,
    // color, font size, alignment, layout) is read fresh from the element at each Draw, per
    // StyleTextEffectResolver's own "resolve fresh, no caching" contract for this axis.
    internal sealed class TextOverlineBinding
    {
        public Action<MeshGenerationContext>? OnGenerate;
    }

    /// <summary>
    /// Paints the <c>overline</c> text-decoration — a solid rule stroked above a leaf TextElement's first
    /// line via Painter2D in the element's own <c>generateVisualContent</c>. UI Toolkit's rich text has no
    /// overline tag (only <c>&lt;u&gt;</c> / <c>&lt;s&gt;</c>), so unlike underline / line-through this
    /// cannot be a string rewrite; <see cref="TextOverlinePainter"/> computes the rule's geometry.
    /// </summary>
    /// <remarks>
    /// v1 scope: ONE rule, positioned above the FIRST line and sized to the text's natural
    /// single-line-equivalent width (clamped to the content width). Per-line metrics of wrapped text are not
    /// publicly reachable in a way usable synchronously from <c>generateVisualContent</c> (there is a public
    /// per-glyph vertex hook, <c>TextElement.PostProcessTextVertices</c>, but it runs job-dependent and out
    /// of sync with <c>generateVisualContent</c>), so a multi-line label gets one rule above its top line
    /// only — a documented limitation, not a bug; per-line placement is future work.
    /// </remarks>
    internal static class TextOverlineSilhouette
    {
        // Wires the paint callback onto the element and returns the binding. Prepended (not appended) so the
        // rule paints BEHIND the glyphs: TextElement (Label, Button) registers its own text-rendering
        // callback at construction, before this Attach call ever runs, and prepending ours keeps it from
        // being invoked AFTER (over) the text — the same ordering rule BorderStyleSilhouette keeps for a
        // dashed outline, and it also matches how a browser paints a decoration line behind the glyph ink,
        // so a tall glyph that dips into the rule's band is not visually interrupted by it.
        public static TextOverlineBinding Attach(VisualElement element)
        {
            var binding = new TextOverlineBinding();
            binding.OnGenerate = mgc => Draw(mgc, (TextElement)element);
            element.generateVisualContent = binding.OnGenerate + element.generateVisualContent;
            element.MarkDirtyRepaint();
            return binding;
        }

        // Unregisters the paint callback so a detached or pooled element cannot ghost the rule onto its
        // next consumer.
        public static void Detach(VisualElement element, TextOverlineBinding binding)
        {
            element.generateVisualContent -= binding.OnGenerate;
            element.MarkDirtyRepaint();
        }

        private static void Draw(MeshGenerationContext mgc, TextElement element)
        {
            // Two different measurements for two different axes: an UNCONSTRAINED measure gives the text's
            // natural single-line-equivalent width (what ResolveStartX centers/right-aligns horizontally,
            // unrelated to how the text actually wraps); a measure pinned to the content width via Exactly
            // gives the (possibly wrapped, multi-line) block's real rendered height — what ResolveFirstLineTop
            // needs to place a Middle/Lower-anchored block vertically. Both are read fresh here (not cached)
            // per this file's own contract, and passed in so TryComputeGeometry stays pure and unit-testable.
            var measuredWidth = element.MeasureTextSize(
                element.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
            var textBlockHeight = element.MeasureTextSize(
                element.text, element.contentRect.width, VisualElement.MeasureMode.Exactly,
                0f, VisualElement.MeasureMode.Undefined).y;
            if (!TextOverlinePainter.TryComputeGeometry(
                    element.contentRect, measuredWidth, textBlockHeight, element.resolvedStyle.fontSize,
                    element.resolvedStyle.unityTextAlign, out var from, out var to, out var lineWidth))
            {
                return;
            }
            TextOverlinePainter.Stroke(mgc.painter2D, from, to, lineWidth, element.resolvedStyle.color);
        }
    }
}
