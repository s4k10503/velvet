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

    // An element's OWN text-transform / text-decoration intent, each axis independent and nullable: null means
    // the axis carries no token on this element (inherit), a non-null value (incl. the explicit None reset) wins
    // over an ancestor. CSS text-transform / text-decoration both inherit, which Unity UI Toolkit does NOT do for
    // these (there is no UITK property), so Velvet realises them by mutating the displayed text — uppercasing the
    // string and wrapping it in the rich-text <u>/<s> tags UI Toolkit renders (enableRichText is on by default).
    internal readonly struct TextEffect : IEquatable<TextEffect>
    {
        public readonly TextTransformKind? Transform;
        public readonly TextDecorationKind? Decoration;

        public TextEffect(TextTransformKind? transform, TextDecorationKind? decoration)
        {
            Transform = transform;
            Decoration = decoration;
        }

        // True when neither axis carries a token (nothing to track for this element).
        public bool IsEmpty => Transform == null && Decoration == null;

        public bool Equals(TextEffect other) => Transform == other.Transform && Decoration == other.Decoration;
        public override bool Equals(object obj) => obj is TextEffect o && Equals(o);
        public override int GetHashCode() => unchecked(((Transform?.GetHashCode() ?? -1) * 397) ^ (Decoration?.GetHashCode() ?? -1));
    }

    // Parses the text-transform / text-decoration utilities into a TextEffect, and applies a resolved
    // effect to a raw string. Pure and allocation-light; the reconciler owns the per-element side-tables and the
    // cascade (walking ancestors for the nearest non-null axis).
    internal static class StyleTextEffectClass
    {
        // Resolves an element's OWN effect from its class list (last token wins per axis). Returns an empty
        // TextEffect (both axes null) when no text-transform / text-decoration token is present.
        public static TextEffect Parse(string[] classNames)
        {
            TextTransformKind? transform = null;
            TextDecorationKind? decoration = null;
            if (classNames == null)
            {
                return default;
            }
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
                }
            }
            return new TextEffect(transform, decoration);
        }

        // Applies a resolved transform + decoration to a raw string. Transform runs first (on the plain text),
        // then the decoration wraps the result in its rich-text tag. A null/None axis is a no-op. Empty text is
        // returned unchanged (no empty <u></u>). The transform assumes plain text; pre-existing rich-text markup
        // in the raw string is the caller's concern.
        public static string Apply(string raw, TextTransformKind? transform, TextDecorationKind? decoration)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return raw;
            }
            var text = ApplyTransform(raw, transform);
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
