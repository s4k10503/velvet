using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine.UIElements;

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

    // The text-decoration an element requests. None is the EXPLICIT reset (no-underline, which clears
    // Overline too — mirroring CSS's text-decoration-line: none clearing every line); unset is a null
    // TextDecorationKind? in TextEffect. UI Toolkit rich text has no overline tag (only <u>/<s>), so unlike
    // Underline/LineThrough, Overline is never realised as a string rewrite — StyleTextEffectClass.Apply
    // passes the string through unchanged for it, and the leaf paints a rule instead (see
    // StyleTextEffectResolver.ApplyToElement / TextOverlineBinding). It still shares this single axis with
    // Underline/LineThrough/None (last-token-wins on one element, cascades/resets the same way) rather than
    // getting its own axis, even though CSS lets text-decoration-line combine multiple lines — see the Why
    // comment in Parse.
    internal enum TextDecorationKind
    {
        None,        // no-underline
        Underline,   // underline    -> <u>
        LineThrough, // line-through -> <s>
        Overline,    // overline     -> painted, no tag
    }

    // The whitespace-collapse an element requests. None is the EXPLICIT reset: every direct, literal
    // whitespace-{normal,nowrap,pre,pre-wrap} class resolves here, not merely to "no collapse" but to a real
    // value that BLOCKS a farther ancestor's whitespace-pre-line from reaching this element — mirroring how
    // normal-case / no-underline block Transform / Decoration. Unset is a null WhitespaceCollapseKind? in
    // TextEffect (inherit an ancestor's collapse, if any).
    internal enum WhitespaceCollapseKind
    {
        None,    // whitespace-normal / whitespace-nowrap / whitespace-pre / whitespace-pre-wrap / truncate
        PreLine, // whitespace-pre-line
    }

    // The line-breaking of CSS's text-wrap-style that Velvet realises. None is auto, the engine's own greedy
    // breaking, and also what text-wrap and text-nowrap reset to.
    internal enum TextWrapStyle
    {
        None,
        Balance,
        Pretty,
    }

    // The two things text-wrap, text-nowrap, text-balance and text-pretty set: the wrap mode and the style.
    internal readonly struct TextWrapSetting
    {
        public readonly bool? Wraps;
        public readonly TextWrapStyle? Style;

        public TextWrapSetting(bool? wraps, TextWrapStyle? style)
        {
            Wraps = wraps;
            Style = style;
        }
    }

    // What a text leaf asks of the line breaking: the breaker that holds its style and measurements, and
    // whether the white-space it inherits keeps the spaces and newlines it was written with.
    internal readonly struct TextBreakRequest
    {
        public readonly StyleTextBalanceManipulator Breaker;
        public readonly bool Preserves;

        public TextBreakRequest(StyleTextBalanceManipulator breaker, bool preserves)
        {
            Breaker = breaker;
            Preserves = preserves;
        }
    }

    // Which unit a resolved Leading value carries, following CSS line-height's two kinds of value. Em is a
    // number (every named leading-* preset and a unitless bracket value): CSS inherits the number itself,
    // so it multiplies whatever font-size is in effect where the rich-text tag is generated. EmLength is an
    // em or percentage bracket value: CSS computes it to a length on the element that declares it, and
    // StyleTextEffectResolver.ResolveEmLength turns it into Pixel where it can. Pixel is an absolute
    // length (a bracket px or rem value, or a leading-<n> spacing step). Unlike TextTransformKind/TextDecorationKind/WhitespaceCollapseKind,
    // this axis has deliberately no explicit-reset member: the leading-* utility scale defines no reset value below
    // leading-none, and every named preset — including leading-none's own multiplier of 1 — is already a
    // real, meaningful value rather than a sentinel standing in for "reset to nothing" the way normal-case /
    // no-underline / an explicit whitespace-* class are. A None member here would have no Parse case that
    // ever produces it and no caller that would ever need it, so it is left out rather than added purely
    // for symmetry with the other three axes.
    internal enum LeadingUnit
    {
        Em,
        EmLength,
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
    // space/tab runs. Decoration's Overline value is the one exception to the string-mutation story: UI
    // Toolkit rich text has no overline tag to wrap with, so it cascades through this SAME struct/axis (it
    // is still just a TextDecorationKind value) but is realised as a PAINTED rule on the leaf instead — see
    // StyleTextEffectResolver.ApplyToElement and TextOverlineBinding.
    //
    // Whitespace still needs the same manual cascade walk as Transform/Decoration despite white-space
    // natively inheriting: no UITK enum member expresses CSS pre-line's collapse, so a C# string mutation is
    // the only way to realise it, and that mutation cannot itself propagate through the visual tree the way
    // a real inherited USS property does — it must reach every leaf whose EFFECTIVE (cascade-resolved) value
    // is PreLine, exactly like Transform/Decoration (see StyleTextEffectResolver.ResolveEffective). Leading
    // has no USS property to inherit from at all (see LeadingUnit), so — like Transform/Decoration, and for
    // the same underlying reason — it needs that identical manual walk too.
    internal readonly struct TextEffect
    {
        public readonly TextTransformKind? Transform;
        public readonly TextDecorationKind? Decoration;
        public readonly WhitespaceCollapseKind? Whitespace;
        public readonly LeadingValue? Leading;
        // The white-space this element's own white-space-writing classes give it, or null when it carries
        // none. text-wrap, text-nowrap, text-balance and text-pretty set CSS's wrap mode and leave the
        // collapse alone, which UI Toolkit's single white-space cannot hold apart, so StyleTextEffectResolver
        // reads the collapse from here.
        public readonly WhiteSpace? WhiteSpaceClass;
        // The wrap mode those four set: true to wrap, false for text-nowrap.
        public readonly bool? Wraps;
        // The line-breaking those four set: balance, pretty, or None for text-wrap and text-nowrap, which
        // reset it. CSS inherits text-wrap-style as it does the wrap mode, so the nearest element that sets
        // it decides for the text under it.
        public readonly TextWrapStyle? WrapStyle;

        public TextEffect(TextTransformKind? transform, TextDecorationKind? decoration, WhitespaceCollapseKind? whitespace, LeadingValue? leading,
            WhiteSpace? whiteSpaceClass, TextWrapSetting wrap)
        {
            Transform = transform;
            Decoration = decoration;
            Whitespace = whitespace;
            Leading = leading;
            WhiteSpaceClass = whiteSpaceClass;
            Wraps = wrap.Wraps;
            WrapStyle = wrap.Style;
        }

        // True when no axis carries a token (nothing to track for this element).
        // A white-space class always sets Whitespace too (Parse), so it needs no term of its own.
        public bool IsEmpty => Transform == null && Decoration == null && Whitespace == null && Leading == null
            && Wraps == null;
    }

    // Parses the text-transform / text-decoration / white-space / leading-* utilities and text-balance /
    // text-pretty into a TextEffect, and applies a resolved effect to a raw string. Pure and
    // allocation-light; the reconciler owns the per-element side-tables and the cascade (walking ancestors for the nearest non-null axis).
    internal static class StyleTextEffectClass
    {
        // leading-* named presets -> em multiplier. Applied verbatim as the rich-text
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

        // Resolves an element's OWN effect from its class list (last token wins per axis, an important one over
        // every plain one; within one importance, Whitespace is the exception —
        // see below). Returns an empty TextEffect (every axis null) when no recognised token is present.
        // Leading follows the same last-token-wins rule as Transform/Decoration: it has no reset form (see
        // LeadingUnit), so there is nothing analogous to Whitespace's cross-family override to special-case.
        // Decoration's last-token-wins rule also covers Overline: CSS's text-decoration-line can combine
        // multiple lines in one declaration (underline overline both render), but this axis resolves to
        // exactly ONE TextDecorationKind value per element by pre-existing design (predating Overline) — so
        // "underline overline" here picks the LAST token (Overline), it does not compose both onto the
        // element the way CSS would. Documented as a deviation in fonts.md; not re-litigated by adding
        // Overline.
        public static TextEffect Parse(string[] classNames)
        {
            if (classNames == null)
            {
                return default;
            }
            var facets = new TextEffectFacets();
            WhitespaceCollapseKind? whitespace = null;
            WhiteSpace? whiteSpaceClass = null;
            // Same two-pass importance rule as StyleFontClass.TryExtract. The white-space axis is settled within
            // each pass and the important pass's answer, when it has one, replaces the plain pass's: an important
            // whitespace-pre-line beats a plain whitespace-nowrap, and an important whitespace-nowrap beats a plain
            // whitespace-pre-line.
            for (var pass = 0; pass < 2; pass++)
            {
                facets.Whitespace = null;
                facets.WhiteSpaceClass = null;
                facets.WhiteSpaceRank = -1;
                foreach (var cls in classNames)
                {
                    var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
                    if (important == (pass == 1))
                    {
                        ParseToken(core, ref facets);
                    }
                }
                // An explicit whitespace-{normal,nowrap,pre,pre-wrap} or truncate class on the SAME element wins
                // over that element's own whitespace-pre-line token of the same importance, whichever appears
                // earlier/later in the class list — order-dependent "last wins" (the rule every other case in
                // ParseToken uses) is not a meaningful concept across two independently-authored utility families,
                // so the choice is made unconditionally instead. This is the least-surprising option: pre-line
                // mutates the displayed string, so a reader who also reached for a direct, single-purpose
                // whitespace-* class most likely wants its literal CSS-standard behavior, not a silently-collapsed
                // one. Resolving to the explicit None reset (not back to unset) settles the conflict on THIS element
                // AND stops a pre-line request from a FARTHER ancestor from still reaching this element through the
                // normal cascade — the same explicit-reset semantics normal-case / no-underline already give
                // Transform/Decoration.
                if (facets.WhiteSpaceClass != null)
                {
                    whitespace = WhitespaceCollapseKind.None;
                    whiteSpaceClass = facets.WhiteSpaceClass;
                }
                else if (facets.Whitespace != null)
                {
                    whitespace = facets.Whitespace;
                    whiteSpaceClass = null;
                }
            }
            return new TextEffect(facets.Transform, facets.Decoration, whitespace, facets.Leading,
                whiteSpaceClass, new TextWrapSetting(facets.Wraps, facets.WrapStyle));
        }

        // The axes Parse folds a class array into, bundled so the
        // per-token step can be shared with IsTextEffectToken. Mirrors StyleFontClass.FontFacets.
        private struct TextEffectFacets
        {
            public TextTransformKind? Transform;
            public TextDecorationKind? Decoration;
            public WhitespaceCollapseKind? Whitespace;
            public LeadingValue? Leading;
            // The latest-declared white-space class seen, and its index in WhiteSpaceClassesInSheetOrder.
            public WhiteSpace? WhiteSpaceClass;
            public int WhiteSpaceRank;
            public bool? Wraps;
            public TextWrapStyle? WrapStyle;
        }

        // The utilities that write white-space, in the order _typography.uss declares them, since among
        // several on one element the later rule is the one UI Toolkit applies. whitespace-pre-line has no
        // rule and is left to the Whitespace axis. WhiteSpaceSheetOrderTests reads the sheet to pin the order.
        internal static readonly KeyValuePair<string, WhiteSpace>[] WhiteSpaceClassesInSheetOrder =
        {
            new("whitespace-normal", WhiteSpace.Normal),
            new("whitespace-nowrap", WhiteSpace.NoWrap),
            new("whitespace-pre", WhiteSpace.Pre),
            new("whitespace-pre-wrap", WhiteSpace.PreWrap),
            new("truncate", WhiteSpace.NoWrap),
        };

        // Returns true when cls was recognised as a text-effect utility (and folded into facets).
        private static bool ParseToken(string cls, ref TextEffectFacets facets)
        {
            switch (cls)
            {
                case null:
                case "": return false;
                case "uppercase": facets.Transform = TextTransformKind.Upper; return true;
                case "lowercase": facets.Transform = TextTransformKind.Lower; return true;
                case "capitalize": facets.Transform = TextTransformKind.Capitalize; return true;
                case "normal-case": facets.Transform = TextTransformKind.None; return true;
                case "underline": facets.Decoration = TextDecorationKind.Underline; return true;
                case "line-through": facets.Decoration = TextDecorationKind.LineThrough; return true;
                case "overline": facets.Decoration = TextDecorationKind.Overline; return true;
                case "no-underline": facets.Decoration = TextDecorationKind.None; return true;
            }
            var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
            switch (core)
            {
                case "whitespace-pre-line": facets.Whitespace = WhitespaceCollapseKind.PreLine; return true;
                case "text-wrap":
                    facets.Wraps = true;
                    facets.WrapStyle = TextWrapStyle.None;
                    return true;
                case "text-balance":
                    facets.Wraps = true;
                    facets.WrapStyle = TextWrapStyle.Balance;
                    return true;
                case "text-pretty":
                    facets.Wraps = true;
                    facets.WrapStyle = TextWrapStyle.Pretty;
                    return true;
                case "text-nowrap":
                    facets.Wraps = false;
                    facets.WrapStyle = TextWrapStyle.None;
                    return true;
            }
            for (var rank = 0; rank < WhiteSpaceClassesInSheetOrder.Length; rank++)
            {
                if (core != WhiteSpaceClassesInSheetOrder[rank].Key)
                {
                    continue;
                }
                // MUTANT_SURVIVES(equivalent, boundary): at an equal rank the class is the one already held.
                if (rank >= facets.WhiteSpaceRank)
                {
                    facets.WhiteSpaceRank = rank;
                    facets.WhiteSpaceClass = WhiteSpaceClassesInSheetOrder[rank].Value;
                }
                return true;
            }
            if (TryParseLeading(cls, out var leading))
            {
                facets.Leading = leading;
                return true;
            }
            return false;
        }

        private static bool TryParseLeading(string cls, out LeadingValue leading)
        {
            if (s_leadingPresets.TryGetValue(cls, out var em))
            {
                leading = new LeadingValue(LeadingUnit.Em, em);
                return true;
            }
            if (TryParseLeadingBracket(cls, out leading))
            {
                return true;
            }
            if (TryParseLeadingSpacing(cls, out var spacingPx))
            {
                leading = new LeadingValue(LeadingUnit.Pixel, spacingPx);
                return true;
            }
            return false;
        }

        // Whether a variant payload spelling cls can change what Parse builds, which is what puts the token
        // in the class source this family resolves from (StyleVariantPayload.IsVariantGateToken). Shares
        // ParseToken with Parse itself so the two cannot answer differently about one token.
        public static bool IsTextEffectToken(string cls)
        {
            var facets = new TextEffectFacets { WhiteSpaceRank = -1 };
            return ParseToken(cls, ref facets);
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
        // downstream, and leak the bare bracket core into the class list as a dead token.
        public static bool IsArbitraryLeadingClass(string cls)
        {
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
            return core.StartsWith("leading-[", StringComparison.Ordinal);
        }

        // Parses leading-[<value>] with CSS line-height's grammar: a unitless number is Em, an em or
        // percentage length is EmLength (a percentage taken as its hundredth), and px and rem are Pixel, rem
        // at the fixed 16px the other bracket utilities use (StyleArbitraryValueResolver.TryParseValue). A
        // negative value, any other unit or a malformed value leaves Leading unset — silently, the way an
        // unparsed w-[abc] is inert; IsArbitraryLeadingClass keeps the token out of the class list either
        // way.
        private static bool TryParseLeadingBracket(string cls, out LeadingValue leading)
        {
            leading = default;
            const string prefix = "leading-[";
            if (!cls.StartsWith(prefix, StringComparison.Ordinal) || cls[cls.Length - 1] != ']')
            {
                return false;
            }
            var inner = cls.AsSpan(prefix.Length, cls.Length - prefix.Length - 1);
            if (!TryParseLineHeight(inner, out var amount, out var unit))
            {
                return false;
            }
            if (amount < 0f)
            {
                return false;
            }
            leading = new LeadingValue(unit, amount);
            return true;
        }

        // leading-<n> reads the --space-* scale, as Tailwind's bare-number line height is n spacing units.
        private static bool TryParseLeadingSpacing(string cls, out float px)
        {
            px = 0f;
            const string prefix = "leading-";
            if (!cls.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
            return StyleArbitraryValueResolver.TryGetSpacingPx(cls.Substring(prefix.Length), out px);
        }

        private static bool TryParseLineHeight(ReadOnlySpan<char> value, out float amount, out LeadingUnit unit)
        {
            unit = LeadingUnit.Em;
            if (StyleArbitraryValueResolver.TryParseFloat(value, out amount))
            {
                return true;
            }
            unit = LeadingUnit.EmLength;
            if (value.EndsWith("em".AsSpan(), StringComparison.Ordinal)
                && !value.EndsWith("rem".AsSpan(), StringComparison.Ordinal))
            {
                return StyleArbitraryValueResolver.TryParseFloat(value.Slice(0, value.Length - 2), out amount);
            }
            if (!StyleArbitraryValueResolver.TryParseValue(value, out amount, out var lengthUnit))
            {
                return false;
            }
            if (lengthUnit == LengthUnit.Percent)
            {
                amount /= 100f;
                return true;
            }
            unit = LeadingUnit.Pixel;
            return true;
        }

        // Applies a resolved whitespace-pre-line collapse, then transform, then the line breaks a request
        // asks for, then decoration, then leading to a raw string, in that order — CSS resolves white-space processing before text-transform, so
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
            LeadingValue? leading = null,
            TextBreakRequest? breaks = null)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return raw;
            }
            var text = CollapseWhitespace(raw, whitespace, breaks);
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            text = ApplyTransform(text, transform);
            if (breaks != null)
            {
                text = breaks.Value.Breaker.Break(text);
            }
            text = ApplyDecoration(text, decoration);
            return ApplyLeading(text, leading);
        }

        // A line break request collapses a default white-space here: the breaks it writes are newlines, and
        // the resolver writes the leaf pre-wrap, which collapses nothing itself. A request whose breaker
        // writes none is applied again without the request, so the leaf keeps the white-space as authored.
        private static string CollapseWhitespace(string raw, WhitespaceCollapseKind? whitespace, TextBreakRequest? breaks)
        {
            if (whitespace == WhitespaceCollapseKind.PreLine)
            {
                return CollapseForPreLine(raw);
            }
            return breaks != null && !breaks.Value.Preserves ? CollapseForNormal(raw) : raw;
        }

        // CSS white-space: normal. Every run of spaces, tabs and line breaks folds to one space and none
        // survives at either end of the string.
        internal static string CollapseForNormal(string text)
        {
            var sb = new StringBuilder(text.Length);
            var pendingSpace = false;
            foreach (var ch in text)
            {
                if (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r')
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }
                sb.Append(ch);
            }
            return sb.ToString();
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
                // Overline has no rich-text tag to wrap with (UI Toolkit's markup vocabulary is <u>/<s>
                // only) — spelled out rather than left to fall through to default, so a reader does not
                // mistake the no-op for an oversight. The leaf paints a rule instead; see
                // StyleTextEffectResolver.ApplyToElement.
                case TextDecorationKind.Overline: return text;
                default: return text; // None / null
            }
        }

        // Wraps text in the <line-height=X> rich-text tag both the standard and Advanced Text Generators
        // implement (px / em / % forms; only em and px are ever produced here). InvariantCulture on the
        // number is required, not cosmetic: this is the first spot in this file that stringifies a float into markup the ENGINE itself re-parses, and a comma decimal
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
