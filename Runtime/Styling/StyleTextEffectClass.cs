using System;
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

    // An element's OWN text-transform / text-decoration / whitespace-collapse intent, each axis independent
    // and nullable: null means the axis carries no token on this element (inherit), a non-null value (incl.
    // the explicit None reset every axis has) wins over an ancestor. CSS text-transform / text-decoration /
    // white-space all inherit; Unity UI Toolkit only natively inherits white-space (it lives in
    // inheritedData) — text-transform / text-decoration have no UITK property at all — so Velvet realises
    // all three the same way regardless, by mutating the displayed text: uppercasing/title-casing the
    // string, wrapping it in the rich-text <u>/<s> tags UI Toolkit renders (enableRichText is on by
    // default), and/or collapsing space/tab runs.
    //
    // Whitespace still needs the same manual cascade walk as Transform/Decoration despite white-space
    // natively inheriting: no UITK enum member expresses CSS pre-line's collapse, so a C# string mutation is
    // the only way to realise it, and that mutation cannot itself propagate through the visual tree the way
    // a real inherited USS property does — it must reach every leaf whose EFFECTIVE (cascade-resolved) value
    // is PreLine, exactly like Transform/Decoration (see StyleTextEffectResolver.ResolveEffective).
    internal readonly struct TextEffect : IEquatable<TextEffect>
    {
        public readonly TextTransformKind? Transform;
        public readonly TextDecorationKind? Decoration;
        public readonly WhitespaceCollapseKind? Whitespace;

        public TextEffect(TextTransformKind? transform, TextDecorationKind? decoration, WhitespaceCollapseKind? whitespace)
        {
            Transform = transform;
            Decoration = decoration;
            Whitespace = whitespace;
        }

        // True when no axis carries a token (nothing to track for this element).
        public bool IsEmpty => Transform == null && Decoration == null && Whitespace == null;

        public bool Equals(TextEffect other) =>
            Transform == other.Transform && Decoration == other.Decoration && Whitespace == other.Whitespace;
        public override bool Equals(object obj) => obj is TextEffect o && Equals(o);
        public override int GetHashCode() =>
            unchecked((((Transform?.GetHashCode() ?? -1) * 397) ^ (Decoration?.GetHashCode() ?? -1)) * 397 ^ (Whitespace?.GetHashCode() ?? -1));
    }

    // Parses the text-transform / text-decoration / whitespace-pre-line utilities into a TextEffect, and
    // applies a resolved effect to a raw string. Pure and allocation-light; the reconciler owns the
    // per-element side-tables and the cascade (walking ancestors for the nearest non-null axis).
    internal static class StyleTextEffectClass
    {
        // Resolves an element's OWN effect from its class list (last token wins per axis, except Whitespace —
        // see below). Returns an empty TextEffect (every axis null) when no recognised token is present.
        public static TextEffect Parse(string[] classNames)
        {
            TextTransformKind? transform = null;
            TextDecorationKind? decoration = null;
            WhitespaceCollapseKind? whitespace = null;
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
            return new TextEffect(transform, decoration, whitespace);
        }

        // Applies a resolved whitespace-pre-line collapse, then transform, then decoration to a raw string, in
        // that order — CSS resolves white-space processing before text-transform, so collapsing first means
        // Capitalize's word-boundary scan and the decoration wrap both see the already-normalized text. A
        // null/None axis on any parameter is a no-op. Empty text is returned unchanged (no empty <u></u>) —
        // checked both before AND after the collapse, since an all-whitespace input can collapse all the way
        // down to empty too (every run in it touches a line edge). The transform assumes plain text;
        // pre-existing rich-text markup in the raw string is the caller's concern.
        public static string Apply(string raw, TextTransformKind? transform, TextDecorationKind? decoration, WhitespaceCollapseKind? whitespace = null)
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
            return ApplyDecoration(text, decoration);
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
