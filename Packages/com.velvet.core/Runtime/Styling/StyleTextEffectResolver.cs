using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Realises text-transform (uppercase / lowercase / capitalize / normal-case), text-decoration
    // (underline / line-through / overline), whitespace-pre-line, and leading-* (line-height) by mutating
    // the displayed text — Unity UI Toolkit has no property for any of the four axes, so the string is
    // upper/lower/title-cased, wrapped in the <u>/<s>/<line-height=X> rich-text tags UITK renders, and/or has
    // its space/tab runs collapsed. Overline is the one value with no tag to wrap with (UI Toolkit's rich
    // text vocabulary is <u>/<s> only), so instead of a string rewrite it attaches a PAINTED rule via a
    // generateVisualContent binding — see the fourth side-table below and TextOverlineBinding. CSS inherits
    // all four axes; UITK does not (for text-transform / text-decoration / line-height there is no UITK
    // property at all; white-space DOES natively inherit, but no enum value expresses pre-line's collapse,
    // so the STRING rewrite still needs the same treatment — see the Whitespace remarks on TextEffect). So
    // every axis cascades by walking ancestors (StyleTextEffectClass holds the pure parse + string ops; this
    // owns the reconciler-side cascade and the per-element side-tables).
    //
    // Three per-element side-tables are pure (on ReconcilerContext): TextEffects = an element's OWN parsed
    // effect (each axis nullable so an explicit reset — normal-case / no-underline / an explicit
    // whitespace-* class — is distinct from "inherit"; Leading has no reset form, see LeadingUnit, but is
    // still nullable the same way for "inherit" vs. "this element sets a real value"); TextRawText = the
    // untransformed text last seen for a text-bearing element, captured at the text-set seams so the effect
    // can be re-applied idempotently (the element's live .text may already be transformed); TextWhitespaceOwned
    // = a set (Dictionary with a trivial value) of elements whose CURRENT inline `style.whiteSpace` was
    // written by THIS resolver — see the ownership discussion below ApplyToElement. All three ride element
    // cleanup / pool reuse for free via ReconcilerContext.ClearElementSideTables. Leading needs no table of
    // its own: unlike PreLine it drives no separate inline style, only the SAME string rewrite
    // TextEffects/TextRawText already carry, so it rides their existing cleanup for free too. A FOURTH table,
    // TextOverlineBindings, is NOT pure: it owns a live generateVisualContent subscription (one per leaf with
    // decoration == Overline), so FiberElementCleaner detaches it explicitly instead of riding the plain
    // side-table sweep — see the Overline remarks below ApplyToElement and the table's own comment on
    // ReconcilerContext.
    //
    // PreLine ALSO drives an inline `white-space: pre-wrap` write, so the preserved newlines render as
    // breaks and wrapping still works. That write happens in ApplyToElement (below), on EVERY text leaf
    // whose EFFECTIVE (cascade-resolved) axis is PreLine — the same call, off the same resolved value, that
    // already drives the string collapse. An earlier design instead wrote it ONCE, only on the element whose
    // OWN class list carried whitespace-pre-line, and left native UI Toolkit inheritance of `white-space` to
    // carry it to descendants; that cannot work for the primary use case (a class on a container Div, text
    // in a descendant Label) because Label/TextElement carries its own element-level `white-space` rule from
    // the default theme/USS, and an element's OWN matching USS rule always beats an INHERITED value in the
    // cascade regardless of specificity — so the write never reached a descendant Label no matter how it
    // got there. Writing per-leaf off the RESOLVED value sidesteps that: a leaf with its own explicit
    // whitespace-{normal,nowrap,pre,pre-wrap} class resolves its own axis first in ResolveEffective (the
    // walk starts at the leaf itself), so it never receives the write and its own USS class governs
    // untouched — no separate "does this leaf opt itself out" check is needed, ResolveEffective already IS
    // that check, and the write and the collapsed string now always agree for the same leaf by construction.
    //
    // The write's clear-when-not-PreLine (ApplyToElement) is OWNERSHIP-gated via TextWhitespaceOwned, not
    // unconditional: it clears style.whiteSpace back to StyleKeyword.Null only when this element is
    // CURRENTLY marked owned there (and then drops the mark); any other leaf — one this resolver never
    // wrote to at all — is left completely untouched. An earlier design cleared unconditionally on every
    // non-PreLine call, reasoning that no other Velvet system ever writes `style.whiteSpace` inline besides
    // FiberElementPoolReset's pool-RETURN sweep (a real second writer, but one that only ever runs on a
    // pooled element BETWEEN tenants, never on one that is still live) — that reasoning was wrong: a
    // consumer's refCallback can write style.whiteSpace directly (a pattern the framework's own docs treat
    // as legitimate, see ReconcilerContext.RefCallbacks), and the unconditional clear silently clobbered it
    // on this element's very next mount-or-patch text-effect pass, whether or not PreLine was involved
    // anywhere in the tree. Ownership-tracking fixes that: the resolver now only ever undoes its OWN prior
    // write, so an element it never touched keeps whatever any other writer put there.
    //
    // TextWhitespaceOwned rides the same ClearElementSideTables sweep as TextEffects/TextRawText, so a
    // normal unmount or pool return scrubs it for free — FiberElementPoolReset's unconditional
    // `style.whiteSpace = null` on the pool-RETURN path remains the backstop for a freshly-rented element
    // whose new node carries no Text prop at all (CaptureRaw never fires for it, so the TextRawText gate
    // below never runs and never gets a chance to clear anything itself). The one path that does NOT go
    // through that sweep is a still-MOUNTED TextElement whose Text prop patches to null: the element keeps
    // living, so ClearElementSideTables never runs for it, yet ApplyToElement can no longer run for it
    // either (no tracked raw text left to resolve from) — without an explicit clear at that transition, a
    // leaf that carried an owned inline PreWrap would keep it forever. FiberNodePatcher.PatchBaseElement
    // clears TextWhitespaceOwned at exactly that site; see the comment there.
    //
    // Overline is likewise gated in ApplyToElement, off the SAME resolved Decoration value the string
    // rewrite above just used: a leaf that resolves to Overline gets a TextOverlineBinding attached (a
    // no-op if one is already tracked for it) and one that no longer does gets its existing binding
    // detached — so the paint and the (absent) string change can never disagree about which axis is active.
    // TextOverlineBindings is NOT a pure side-table (see its own comment on ReconcilerContext): the same
    // still-MOUNTED-Text-prop-patched-to-null transition above would otherwise leak its generateVisualContent
    // subscription forever (ClearElementSideTables never runs, and ApplyToElement can never run again to
    // detach it either), so FiberNodePatcher.PatchBaseElement detaches it at that exact site too.
    //
    // KNOWN LIMITATION: toggling a text-transform / text-decoration / whitespace-pre-line / leading-* class
    // on an ANCESTOR re-cascades to descendants only when that ancestor is re-rendered (its post-children
    // pass walks them). A descendant whose own text is unchanged and whose ancestor's class changed without
    // the ancestor re-patching keeps its prior effect — string AND, for PreLine, the inline `white-space`
    // write AND, for Overline, the painted binding alike — until its next text update; the common static
    // case (effect set at mount) is fully correct.
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
        // text and — when it carries an effect (now OR before this call) — its descendant text. Called from the
        // post-children hook on create and patch, so it runs after the element's text, its classes, and its
        // children are all in place.
        public static void Apply(ReconcilerContext ctx, VisualElement element, string[] classNames)
        {
            ctx.TextEffects.TryGetValue(element, out var previous);
            var own = StyleTextEffectClass.Parse(classNames);
            if (own.IsEmpty)
            {
                ctx.TextEffects.Remove(element);
            }
            else
            {
                ctx.TextEffects[element] = own;
            }

            // The element's own text (Label / Button carrying a Text prop) reflects its cascade-resolved
            // effect — including the inline pre-wrap write for PreLine (see ApplyToElement). No own-axis
            // shortcut is needed here: ApplyToElement resolves from THIS element via ResolveEffective, which
            // checks the TextEffects entry just written above first, so an own PreLine token (or reset) is
            // already the first thing the walk finds.
            ApplyToElement(ctx, element);

            // An element carrying an effect cascades it to descendant text (the V.Div("uppercase", V.Text(...))
            // shape, where the text leaf has no class of its own). Gated on "own OR previous carrying an
            // effect" — not simply "own effect present" — so a patch that DROPS the last effect-bearing class
            // (own goes from non-empty to empty) still re-walks descendants: nothing else re-resolves a
            // descendant TextNode leaf whose own text prop is unchanged (PatchText only calls OnTextSet when
            // the text prop itself changes), so without this walk it would keep showing the removed effect —
            // string AND, for PreLine, the inline whitespace write alike — forever. The reset forms
            // (normal-case / no-underline / an explicit whitespace-* class) also cascade — they re-resolve
            // descendants against the nearest surviving ancestor effect.
            if (!own.IsEmpty || !previous.IsEmpty)
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
                var (transform, decoration, whitespace, leading) = ResolveEffective(ctx, te);
                te.text = StyleTextEffectClass.Apply(raw, transform, decoration, whitespace, leading);
                // Per-leaf inline pre-wrap write, off the SAME resolved value that just drove the string
                // collapse above, so the two can never disagree for this leaf. Ownership-gated (see the type
                // comment / TextWhitespaceOwned): PreLine writes-and-marks; a non-PreLine resolve clears ONLY
                // when this element is currently marked, so a leaf this resolver never wrote to — including
                // one a consumer's refCallback wrote style.whiteSpace on directly — is never touched either way.
                if (whitespace == WhitespaceCollapseKind.PreLine)
                {
                    te.style.whiteSpace = WhiteSpace.PreWrap;
                    ctx.TextWhitespaceOwned[te] = true;
                }
                else if (ctx.TextWhitespaceOwned.Remove(te))
                {
                    te.style.whiteSpace = StyleKeyword.Null;
                }

                // Overline paint, off the SAME resolved decoration the string rewrite above just used (it
                // left the string itself unchanged — see StyleTextEffectClass.ApplyDecoration): attach a
                // binding the first time this leaf resolves to Overline, detach it the moment it no longer
                // does. Unlike the whitespace write there is nothing to disagree with here (no other system
                // ever attaches this binding), so no ownership gate is needed — presence in
                // TextOverlineBindings IS the ownership.
                if (decoration == TextDecorationKind.Overline)
                {
                    // Repaint belongs on the ATTACH transition only — Attach's own MarkDirtyRepaint already
                    // covers a fresh subscription's first paint — NOT on a steady-state revisit where the
                    // binding already exists, or every patch of every overlined leaf pays a full mesh regen
                    // whether or not anything the paint reads actually changed. This is safe because every
                    // value Draw reads is already dirtied through its OWN native channel by the time it
                    // changes: TextElement's text setter always raises Repaint (SetValueWithoutNotify diffs
                    // the string and calls IncrementVersion(Layout|Repaint) or Repaint); font-size and
                    // -unity-text-align are both explicit Repaint-raising properties in UI Toolkit's own
                    // ComputedStyle diff (ComputedStyle.CompareChanges); and contentRect only ever moves as
                    // the result of a resolved layout change, which the layout pass itself always pairs with
                    // Repaint the moment a box's resolved size actually changes (VisualTreeLayoutUpdater.
                    // UpdateSubTree raises Size and Repaint together, unconditionally, no fast path). The one
                    // real gap found: a resolvedStyle.color-ONLY change raises
                    // VersionChangeType.Color, not Repaint, and an element that opts into the native
                    // UsageHints.DynamicColor perf hint can patch that color in place without a full repaint
                    // (RenderEvents.OnColorChanged's dynamic-color fast path) — Velvet itself never sets that
                    // hint, so closing it here would mean paying an unconditional repaint on every visit to
                    // guard a combination this framework never produces; left as a documented, self-correcting
                    // gap (any OTHER later change on the element still repaints it) rather than gated.
                    if (!ctx.TextOverlineBindings.TryGetValue(te, out var overline))
                    {
                        overline = TextOverlineSilhouette.Attach(te);
                        ctx.TextOverlineBindings[te] = overline;
                    }
                }
                else if (ctx.TextOverlineBindings.TryGetValue(te, out var overline))
                {
                    TextOverlineSilhouette.Detach(te, overline);
                    ctx.TextOverlineBindings.Remove(te);
                }
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

        // The cascade: the nearest ancestor-or-self that sets each axis wins (each axis resolved
        // independently), mirroring CSS inheritance of text-transform / text-decoration / white-space /
        // line-height. A null axis on every ancestor means no effect on that axis. An explicit None on
        // Transform/Decoration/Whitespace (normal-case / no-underline / an explicit whitespace-* class) also
        // wins and stops the walk for THAT axis — it is a real resolved value, not "no token" — so it blocks
        // a farther ancestor's non-None value from reaching this element; Leading has no such explicit-reset
        // value (see LeadingUnit), so its walk only ever stops on a real leading-* token or the tree's root.
        // Feeds BOTH the string rewrite and, for Whitespace, the inline pre-wrap style write in
        // ApplyToElement — one resolve, N writes off the same value, so they can never disagree.
        private static (TextTransformKind? transform, TextDecorationKind? decoration, WhitespaceCollapseKind? whitespace, LeadingValue? leading) ResolveEffective(
            ReconcilerContext ctx, VisualElement element)
        {
            TextTransformKind? transform = null;
            TextDecorationKind? decoration = null;
            WhitespaceCollapseKind? whitespace = null;
            LeadingValue? leading = null;
            for (var e = element; e != null; e = e.hierarchy.parent)
            {
                if (!ctx.TextEffects.TryGetValue(e, out var eff))
                {
                    continue;
                }
                transform ??= eff.Transform;
                decoration ??= eff.Decoration;
                whitespace ??= eff.Whitespace;
                leading ??= eff.Leading;
                if (transform != null && decoration != null && whitespace != null && leading != null)
                {
                    break;
                }
            }
            return (transform, decoration, whitespace, leading);
        }
    }
}
