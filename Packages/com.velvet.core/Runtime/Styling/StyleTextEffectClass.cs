using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Velvet
{
    // The text-transform an element requests. None is the EXPLICIT reset (normal-case) — distinct from "unset",
    // which is represented by a null TextTransformKind? in TextEffect so the cascade can tell an explicit
    // normal-case (stop inheriting) apart from no token at all (inherit an ancestor's transform).
    internal enum TextTransformKind
    {
        None,        // normal-case
        Upper,       // uppercase
        Lower,       // lowercase
        Capitalize,  // capitalize (title-case: first letter of each word)
    }

    // The text-decoration an element requests. None is the EXPLICIT reset (no-underline); unset is a null
    // TextDecorationKind? in TextEffect. UI Toolkit text has no overline rich-text tag, so overline is omitted.
    internal enum TextDecorationKind
    {
        None,        // no-underline
        Underline,   // underline   -> <u>
        LineThrough, // line-through -> <s>
    }

    // The whitespace-collapse an element requests. None is the EXPLICIT reset: every direct, literal
    // whitespace-{normal,nowrap,pre,pre-wrap} class resolves here, not merely to "no collapse" but to a real
    // value that BLOCKS a farther ancestor's whitespace-pre-line from reaching this element — mirroring how
    // normal-case / no-underline block Transform / Decoration. Unset is a null WhitespaceCollapseKind? in
    // TextEffect (inherit an ancestor's collapse, if any).
    internal enum WhitespaceCollapseKind
    {
        None,    // whitespace-normal / whitespace-nowrap / whitespace-pre / whitespace-pre-wrap
        PreLine, // whitespace-pre-line
    }

    // Which unit a resolved Leading value carries: Em multiplies whatever font-size is in effect at the
    // point the rich-text tag is generated (every named leading-* preset); Pixel is an absolute length
    // (leading-[Npx]). Unlike TextTransformKind/TextDecorationKind/WhitespaceCollapseKind, this axis has
    // deliberately no explicit-reset member: Tailwind defines no leading-auto utility below leading-none, and
    // every named preset — including leading-none's own multiplier of 1 — is already a real, meaningful
    // value rather than a sentinel standing in for "reset to nothing" the way normal-case / no-underline /
    // an explicit whitespace-* class are. A None member here would have no Parse case that ever produces it
    // and no caller that would ever need it, so it is left out rather than added purely for symmetry with
    // the other three axes.
    internal enum LeadingUnit
    {
        Em,
        Pixel,
    }

    // A resolved leading-* value: the numeric multiplier/length plus which unit it is in. Nullable
    // (LeadingValue?) in TextEffect exactly like the other three axes' own kinds — null means unset (inherit
    // an ancestor's leading, if any); non-null is a real value that cascades and can be overridden by a
    // nearer ancestor's own leading-* (the ??= resolution in StyleTextEffectResolver.ResolveEffective gives
    // this for free, identical to Transform/Decoration/Whitespace).
    internal readonly struct LeadingValue : IEquatable<LeadingValue>
    {
        public readonly LeadingUnit Unit;
        public readonly float Value;

        public LeadingValue(LeadingUnit unit, float value)
        {
            Unit = unit;
            Value = value;
        }

        public bool Equals(LeadingValue other) => Unit == other.Unit && Value == other.Value;
        public override bool Equals(object obj) => obj is LeadingValue o && Equals(o);
        public override int GetHashCode() => unchecked((Unit.GetHashCode() * 397) ^ Value.GetHashCode());
    }

    // An element's OWN text-transform / text-decoration / whitespace-collapse / leading intent, each axis
    // independent and nullable: null means the axis carries no token on this element (inherit), a non-null
    // value (incl. the explicit None reset the first three axes have) wins over an ancestor. CSS
    // text-transform / text-decoration / white-space / line-height all inherit; Unity UI Toolkit only
    // natively inherits white-space (it lives in inheritedData) — text-transform / text-decoration /
    // line-height have no UITK property at all — so Velvet realises all four the same way regardless, by
    // mutating the displayed text: uppercasing/title-casing the string, wrapping it in the rich-text
    // <u>/<s>/<line-height=X> tags UI Toolkit renders (enableRichText is on by default), and/or collapsing
    // space/tab runs.
    //
    // Whitespace still needs the same manual cascade walk as Transform/Decoration despite white-space
    // natively inheriting: no UITK enum member expresses CSS pre-line's collapse, so a C# string mutation is
    // the only way to realise it, and that mutation cannot itself propagate through the visual tree the way
    // a real inherited USS property does — it must reach every leaf whose EFFECTIVE (cascade-resolved) value
    // is PreLine, exactly like Transform/Decoration (see StyleTextEffectResolver.ResolveEffective). Leading
    // has no USS property to inherit from at all (see LeadingUnit), so — like Transform/Decoration, and for
    // the same underlying reason — it needs that identical manual walk too.
    internal readonly struct TextEffect : IEquatable<TextEffect>
    {
        public readonly TextTransformKind? Transform;
        public readonly TextDecorationKind? Decoration;
        public readonly WhitespaceCollapseKind? Whitespace;
        public readonly LeadingValue? Leading;

        public TextEffect(TextTransformKind? transform, TextDecorationKind? decoration, WhitespaceCollapseKind? whitespace, LeadingValue? leading)
        {
            Transform = transform;
            Decoration = decoration;
            Whitespace = whitespace;
            Leading = leading;
        }

        // True when no axis carries a token (nothing to track for this element).
        public bool IsEmpty => Transform == null && Decoration == null && Whitespace == null && Leading == null;

        public bool Equals(TextEffect other) =>
            Transform == other.Transform && Decoration == other.Decoration && Whitespace == other.Whitespace
            && Leading.Equals(other.Leading);
        public override bool Equals(object obj) => obj is TextEffect o && Equals(o);
        public override int GetHashCode() =>
            unchecked(((((Transform?.GetHashCode() ?? -1) * 397) ^ (Decoration?.GetHashCode() ?? -1)) * 397 ^ (Whitespace?.GetHashCode() ?? -1)) * 397 ^ (Leading?.GetHashCode() ?? -1));
    }

    // Parses the text-transform / text-decoration / whitespace-pre-line / leading-* utilities into a
    // TextEffect, and applies a resolved effect to a raw string. Pure and allocation-light; the reconciler
    // owns the per-element side-tables and the cascade (walking ancestors for the nearest non-null axis).
    internal static class StyleTextEffectClass
    {
        // leading-* named presets -> em multiplier (Tailwind's own scale). Applied verbatim as the rich-text
        // tag's <line-height=Nem> value — the ENGINE resolves the em against whatever font-size is in effect
        // at that point in the string, so this table needs no font-size-aware pre-baking the way
        // tracking-*'s em scale does in _typography.uss (see fonts.md).
        private static readonly Dictionary<string, float> s_leadingPresets = new(StringComparer.Ordinal)
        {
            ["leading-none"] = 1f,
            ["leading-tight"] = 1.25f,
            ["leading-snug"] = 1.375f,
            ["leading-normal"] = 1.5f,
            ["leading-relaxed"] = 1.625f,
            ["leading-loose"] = 2f,
        };

        // Resolves an element's OWN effect from its class list (last token wins per axis, except Whitespace —
        // see below). Returns an empty TextEffect (every axis null) when no recognised token is present.
        // Leading follows the same last-token-wins rule as Transform/Decoration: it has no reset form (see
        // LeadingUnit), so there is nothing analogous to Whitespace's cross-family override to special-case.
        public static TextEffect Parse(string[] classNames)
        {
            TextTransformKind? transform = null;
            TextDecorationKind? decoration = null;
            WhitespaceCollapseKind? whitespace = null;
            LeadingValue? leading = null;
            if (classNames == null)
            {
                return default;
            }
            var sawExplicitWhitespaceClass = false;
            foreach (var cls in classNames)
            {
                switch (cls)
                {
                    case "uppercase": transform = TextTransformKind.Upper; break;
                    case "lowercase": transform = TextTransformKind.Lower; break;
                    case "capitalize": transform = TextTransformKind.Capitalize; break;
                    case "normal-case": transform = TextTransformKind.None; break;
                    case "underline": decoration = TextDecorationKind.Underline; break;
                    case "line-through": decoration = TextDecorationKind.LineThrough; break;
                    case "no-underline": decoration = TextDecorationKind.None; break;
                    case "whitespace-pre-line": whitespace = WhitespaceCollapseKind.PreLine; break;
                    case "whitespace-normal":
                    case "whitespace-nowrap":
                    case "whitespace-pre":
                    case "whitespace-pre-wrap":
                        sawExplicitWhitespaceClass = true;
                        break;
                    default:
                        if (s_leadingPresets.TryGetValue(cls, out var em))
                        {
                            leading = new LeadingValue(LeadingUnit.Em, em);
                        }
                        else if (TryParseLeadingBracket(cls, out var px))
                        {
                            leading = new LeadingValue(LeadingUnit.Pixel, px);
                        }
                        break;
                }
            }
            // An explicit whitespace-{normal,nowrap,pre,pre-wrap} class on the SAME element always wins over
            // that element's own whitespace-pre-line token, regardless of which appears earlier/later in the
            // class list — order-dependent "last wins" (the rule every other case above uses) is not a
            // meaningful concept across two independently-authored utility families, so the choice is made
            // unconditionally instead. This is the least-surprising option: pre-line mutates the displayed
            // string, so a reader who also reached for a direct, single-purpose whitespace-* class most
            // likely wants its literal CSS-standard behavior, not a silently-collapsed one. Resolving to the
            // explicit None reset (not back to unset) settles the conflict on THIS element AND stops a
            // pre-line request from a FARTHER ancestor from still reaching this element through the normal
            // cascade — the same explicit-reset semantics normal-case / no-underline already give
            // Transform/Decoration.
            if (sawExplicitWhitespaceClass)
            {
                whitespace = WhitespaceCollapseKind.None;
            }
            return new TextEffect(transform, decoration, whitespace, leading);
        }

        // True when cls is the leading-[...] arbitrary bracket form, regardless of whether its value parses.
        // Mirrors StyleFontClass.IsArbitraryFontClass: the reconciler must keep this token out of the USS
        // class list at the same sites (FiberElementFactory.ApplyClassNames, FiberNodePatcher.AddClass /
        // RemoveClass) — a malformed leading-[...] must vanish exactly like a malformed font-[...] does,
        // never sitting in the class list as a dead token. The named presets (leading-none, leading-tight,
        // ...) need no such guard: an inert token in the class list is the established, harmless pattern
        // every other axis's own real-value classes already use (uppercase, whitespace-pre-line, ...).
        // Routed through StripImportant first so an important-modifier bang (!leading-[...] / leading-[...]!)
        // is tolerated: the 3 call sites above run THIS check before their own StripImportant call, so a
        // bang'd token that only matched the un-prefixed form would fall through, have its bang stripped
        // downstream, and leak the bare bracket core into the class list as a dead token. The modifier
        // itself stays a no-op for this family either way (see StripImportant's own Scope comment) — this
        // only makes the classlist guard agree with the stripper on what counts as "this family".
        public static bool IsArbitraryLeadingClass(string cls)
        {
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
            return core.StartsWith("leading-[", StringComparison.Ordinal);
        }

        // Parses leading-[Npx]. Only the px unit is accepted inside the bracket for v1 — an explicit "px"
        // suffix is REQUIRED (a bare unitless number, or any other unit such as em/%/rem, is rejected)
        // because the feature is deliberately scoped down to px for its first iteration; widening to other
        // units is left for later. A malformed or unsupported-unit value returns false and Leading is simply
        // left unset for this token — silently, no Debug.LogWarning — mirroring
        // StyleArbitraryValueResolver.TryParse's own established convention for a bracket value that fails to
        // parse (e.g. w-[abc]): there, an unparsed token falls back to the USS class list, where it matches
        // no selector and is inert; here, IsArbitraryLeadingClass's unconditional prefix check keeps it out
        // of the class list too, so the result is the same inertness by a different, axis-appropriate route.
        private static bool TryParseLeadingBracket(string cls, out float px)
        {
            px = 0f;
            const string prefix = "leading-[";
            if (!cls.StartsWith(prefix, StringComparison.Ordinal) || cls[cls.Length - 1] != ']')
            {
                return false;
            }
            var inner = cls.Substring(prefix.Length, cls.Length - prefix.Length - 1);
            if (!inner.EndsWith("px", StringComparison.Ordinal))
            {
                return false;
            }
            var numeric = inner.Substring(0, inner.Length - 2);
            return float.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out px)
                && !float.IsNaN(px) && !float.IsInfinity(px) && px >= 0f;
        }

        // Applies a resolved whitespace-pre-line collapse, then transform, then decoration, then leading to a
        // raw string, in that order — CSS resolves white-space processing before text-transform, so
        // collapsing first means Capitalize's word-boundary scan and the decoration wrap both see the
        // already-normalized text. Leading wraps OUTERMOST, after decoration: line-height is a layout
        // property with no bearing on the string's own content, so it never needs to observe (or interact
        // with) what Transform/Decoration did to it — wrapping outside <u>/<s> leaves those tags' own nesting
        // untouched and simply adds one more independent rich-text span around the whole result. A null/None
        // axis on any parameter is a no-op. Empty text is returned unchanged (no empty <u></u>, no empty
        // <line-height>...</line-height>) — checked both before AND after the collapse, since an
        // all-whitespace input can collapse all the way down to empty too (every run in it touches a line
        // edge); Transform/Decoration/Leading never turn a non-empty string empty, so that one guard pair
        // upfront covers the whole pipeline. The transform assumes plain text; pre-existing rich-text markup
        // in the raw string is the caller's concern.
        public static string Apply(
            string raw,
            TextTransformKind? transform,
            TextDecorationKind? decoration,
            WhitespaceCollapseKind? whitespace = null,
            LeadingValue? leading = null)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return raw;
            }
            var text = whitespace == WhitespaceCollapseKind.PreLine ? CollapseForPreLine(raw) : raw;
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            text = ApplyTransform(text, transform);
            text = ApplyDecoration(text, decoration);
            return ApplyLeading(text, leading);
        }

        private static string ApplyTransform(string text, TextTransformKind? transform)
        {
            switch (transform)
            {
                case TextTransformKind.Upper: return text.ToUpperInvariant();
                case TextTransformKind.Lower: return text.ToLowerInvariant();
                case TextTransformKind.Capitalize: return Capitalize(text);
                default: return text; // None / null
            }
        }

        private static string ApplyDecoration(string text, TextDecorationKind? decoration)
        {
            switch (decoration)
            {
                case TextDecorationKind.Underline: return "<u>" + text + "</u>";
                case TextDecorationKind.LineThrough: return "<s>" + text + "</s>";
                default: return text; // None / null
            }
        }

        // Wraps text in the <line-height=X> rich-text tag both the standard and Advanced Text Generators
        // implement (px / em / % forms; only em — the named presets — and px — the bracket form — are ever
        // produced here). InvariantCulture on the number is required, not cosmetic: this is the first spot in
        // this file that stringifies a float into markup the ENGINE itself re-parses, and a comma decimal
        // separator (e.g. "1,625em" under a comma-decimal thread culture) does not match the tag's numeric
        // grammar, so the tag would silently fail to apply — a new bug class for this axis, since
        // Transform/Decoration never stringify a number at all.
        private static string ApplyLeading(string text, LeadingValue? leading)
        {
            if (leading == null)
            {
                return text;
            }
            var value = leading.Value;
            var unit = value.Unit == LeadingUnit.Pixel ? "px" : "em";
            var amount = value.Value.ToString(CultureInfo.InvariantCulture);
            return "<line-height=" + amount + unit + ">" + text + "</line-height>";
        }

        // CSS pre-line's own text mutation: runs of spaces/tabs fold to a single space and newlines are kept
        // as forced breaks, but a run touching a line edge — the very start of a line, the very end of a
        // line, or the start/end of the whole string — collapses away entirely rather than leaving a stray
        // space there, mirroring how CSS drops the whitespace immediately around a preserved segment break.
        // CSS Text Level 3 also normalizes segment breaks: a '\r\n' pair and a lone '\r' are both treated as
        // a single break equivalent to '\n', so both are folded to '\n' before the line-edge logic above
        // sees them (a '\r\n' pair also consumes its own trailing '\n' so it still yields exactly one break,
        // not two). Manual single pass over the string (no Regex) to stay allocation-light, matching
        // Capitalize below; the sentinel iteration (i == text.Length) closes out a trailing run exactly like
        // a real line break would, without appending anything for it.
        private static string CollapseForPreLine(string text)
        {
            var sb = new StringBuilder(text.Length);
            var inRun = false;
            var atLineStart = true; // true until a non-run character is written on the current line
            for (var i = 0; i <= text.Length; i++)
            {
                var atEnd = i == text.Length;
                var ch = atEnd ? '\n' : text[i];
                if (!atEnd && ch == '\r')
                {
                    // Normalize onto the '\n' sentinel the rest of this walk already keys line edges off
                    // of; a '\r\n' pair swallows its own trailing '\n' here so it is not also counted as a
                    // second, separate break.
                    ch = '\n';
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                }
                if (!atEnd && (ch == ' ' || ch == '\t'))
                {
                    inRun = true;
                    continue;
                }
                if (inRun)
                {
                    // A run survives as a single space only strictly inside a line: not immediately after the
                    // previous line break (or the string's start) and not immediately before this one.
                    if (!atLineStart && ch != '\n')
                    {
                        sb.Append(' ');
                    }
                    inRun = false;
                }
                if (atEnd)
                {
                    break;
                }
                sb.Append(ch);
                atLineStart = ch == '\n';
            }
            return sb.ToString();
        }

        // Title-cases each whitespace-separated word (the first letter of every word uppercased, the rest left
        // as-is) — matching CSS text-transform: capitalize, which only touches the first letter of each word.
        private static string Capitalize(string text)
        {
            var sb = new StringBuilder(text.Length);
            var atWordStart = true;
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    atWordStart = true;
                    sb.Append(ch);
                    continue;
                }
                sb.Append(atWordStart ? char.ToUpper(ch, CultureInfo.InvariantCulture) : ch);
                atWordStart = false;
            }
            return sb.ToString();
        }
    }
}
