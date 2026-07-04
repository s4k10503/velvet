using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Realises text-transform (uppercase / lowercase / capitalize / normal-case) and text-decoration
    // (underline / line-through) by mutating the displayed text — Unity UI Toolkit has no property for either,
    // so the string is upper/lower/title-cased and wrapped in the <u>/<s> rich-text tags UITK renders. CSS
    // inherits both; UITK does not, so the effect cascades by walking ancestors (StyleTextEffectClass holds the
    // pure parse + string ops; this owns the reconciler-side cascade and the per-element side-tables).
    //
    // Two per-element side-tables (both pure, on ReconcilerContext): TextEffects = an element's OWN parsed
    // effect (each axis nullable so an explicit normal-case / no-underline reset is distinct from "inherit");
    // TextRawText = the untransformed text last seen for a text-bearing element, captured at the text-set seams
    // so the effect can be re-applied idempotently (the element's live .text may already be transformed).
    //
    // KNOWN LIMITATION: toggling a text-transform / -decoration class on an ANCESTOR re-cascades to descendants
    // only when that ancestor is re-rendered (its post-children pass walks them). A descendant whose own text is
    // unchanged and whose ancestor's class changed without the ancestor re-patching keeps its prior effect until
    // its next text update — the common static case (effect set at mount) is fully correct.
    internal static class StyleTextEffectResolver
    {
        // Captures the untransformed text for a text-bearing element at a text-set seam (TextNode create/patch,
        // or an element's Text prop). Stored so a later re-apply (this element's or an ancestor's effect pass)
        // transforms the RAW text, not an already-transformed value.
        public static void CaptureRaw(ReconcilerContext ctx, VisualElement element, string raw)
        {
            ctx.TextRawText[element] = raw ?? string.Empty;
        }

        // Captures the raw text AND immediately applies the cascade-resolved effect to this element. Used at the
        // text-set seams (TextNode create / patch) so a leaf that re-renders in ISOLATION — its own text changed
        // via an inner component's state while the effect-bearing ANCESTOR did not re-render — still shows the
        // inherited effect, rather than waiting for the ancestor's next pass. Idempotent (resolves from the raw).
        public static void OnTextSet(ReconcilerContext ctx, VisualElement element, string raw)
        {
            CaptureRaw(ctx, element, raw);
            ApplyToElement(ctx, element);
        }

        // Syncs an element's OWN effect from its class list and applies the (cascade-resolved) effect to its own
        // text and — when it carries an effect — its descendant text. Called from the post-children hook on
        // create and patch, so it runs after the element's text, its classes, and its children are all in place.
        public static void Apply(ReconcilerContext ctx, VisualElement element, string[] classNames)
        {
            var own = StyleTextEffectClass.Parse(classNames);
            if (own.IsEmpty)
            {
                ctx.TextEffects.Remove(element);
            }
            else
            {
                ctx.TextEffects[element] = own;
            }

            // The element's own text (Label / Button carrying a Text prop) reflects its cascade-resolved effect.
            ApplyToElement(ctx, element);

            // An element carrying an effect cascades it to descendant text (the V.Div("uppercase", V.Text(...))
            // shape, where the text leaf has no class of its own). Gated on a non-empty own effect so the common
            // no-effect element pays nothing. The reset forms (normal-case / no-underline) also cascade — they
            // re-resolve descendants against the nearest surviving ancestor effect.
            if (!own.IsEmpty)
            {
                ApplyToDescendants(ctx, element);
            }
        }

        // Re-applies the cascade-resolved effect to a single element if it is text-bearing and a raw text is
        // tracked for it. Idempotent: always transforms from the stored raw, never from the live (maybe already
        // transformed) .text.
        private static void ApplyToElement(ReconcilerContext ctx, VisualElement element)
        {
            if (element is TextElement te && ctx.TextRawText.TryGetValue(te, out var raw))
            {
                var (transform, decoration) = ResolveEffective(ctx, te);
                te.text = StyleTextEffectClass.Apply(raw, transform, decoration);
            }
        }

        private static void ApplyToDescendants(ReconcilerContext ctx, VisualElement root)
        {
            // hierarchy walk (not the content Query) so wrapper-free structure is traversed exactly; only the
            // text-bearing descendants with a tracked raw text are touched.
            var count = root.hierarchy.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = root.hierarchy[i];
                ApplyToElement(ctx, child);
                ApplyToDescendants(ctx, child);
            }
        }

        // The cascade: the nearest ancestor-or-self that sets each axis wins (each axis resolved independently),
        // mirroring CSS inheritance of text-transform / text-decoration. A null axis on every ancestor means no
        // effect on that axis.
        private static (TextTransformKind? transform, TextDecorationKind? decoration) ResolveEffective(
            ReconcilerContext ctx, VisualElement element)
        {
            TextTransformKind? transform = null;
            TextDecorationKind? decoration = null;
            for (var e = element; e != null; e = e.hierarchy.parent)
            {
                if (!ctx.TextEffects.TryGetValue(e, out var eff))
                {
                    continue;
                }
                transform ??= eff.Transform;
                decoration ??= eff.Decoration;
                if (transform != null && decoration != null)
                {
                    break;
                }
            }
            return (transform, decoration);
        }
    }
}
