using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;

namespace Velvet
{
    internal static partial class StyleCustomProperties
    {
        // What an unresolvable var() computes to, as a utility, for the painted utilities where that is not
        // nothing. A gradient stop's colour and position, a box shadow, a ring's shadow and its offset are custom
        // properties Tailwind registers with an initial value: transparent; 0%, 50% and 100%; one that draws none;
        // the same; 0px. A ring's colour is one it registers without one, which its shadow reads with a
        // currentcolor fallback; an outline colour's initial value is currentcolor and its width's is medium (3px).
        // Gap's initial value is no gap. A font's weight and family and a line height inherit. A skew axis is a
        // custom property Tailwind registers without one, which the transform reads with an empty fallback: no
        // shear on that axis, as a skew of 0 draws.
        private static string? PaintedUnset(string prefix, string? hint)
        {
            if (!s_paintedUnset.TryGetValue(prefix, out var unset))
            {
                return null;
            }
            return hint == null ? unset.Untyped
                : hint == "color" ? unset.Colour
                : hint is "family-name" or "generic-name" ? unset.Family
                : unset.Typed;
        }

        // Per painted utility: what an unresolvable var() stands for when the value is untyped, a colour, a family
        // name, and any other type. An untyped shadow is a box shadow, as Tailwind writes one it does not test.
        private static readonly Dictionary<string, (string? Untyped, string? Colour, string? Family, string? Typed)> s_paintedUnset =
            new(StringComparer.Ordinal)
            {
                ["from-"] = ("from-transparent", "from-transparent", "from-transparent", "from-0%"),
                ["via-"] = ("via-transparent", "via-transparent", "via-transparent", "via-50%"),
                ["to-"] = ("to-transparent", "to-transparent", "to-transparent", "to-100%"),
                ["shadow-"] = ("shadow-none", null, "shadow-none", "shadow-none"),
                ["ring-"] = ("ring-current", "ring-current", "ring-current", "ring-0"),
                ["ring-offset-"] = (null, null, null, "ring-offset-0"),
                ["outline-"] = ("outline-current", "outline-current", "outline-current", "outline-[length:3px]"),
                ["gap-"] = ("gap-0", "gap-0", "gap-0", "gap-0"),
                ["gap-x-"] = ("gap-x-0", "gap-x-0", "gap-x-0", "gap-x-0"),
                ["gap-y-"] = ("gap-y-0", "gap-y-0", "gap-y-0", "gap-y-0"),
                ["font-"] = ("font-[weight:inherit]", "font-[weight:inherit]", "font-[family-name:inherit]", "font-[weight:inherit]"),
                ["leading-"] = ("leading-[inherit]", "leading-[inherit]", "leading-[inherit]", "leading-[inherit]"),
                ["skew-x-"] = ("skew-x-0", "skew-x-0", "skew-x-0", "skew-x-0"),
                ["skew-y-"] = ("skew-y-0", "skew-y-0", "skew-y-0", "skew-y-0"),
                ["skew-"] = ("skew-0", "skew-0", "skew-0", "skew-0"),
            };

        private static bool Inherits(ArbitraryProperty property)
            => property is ArbitraryProperty.TextColor or ArbitraryProperty.FontSize or ArbitraryProperty.LetterSpacing;

        // CSS's unset: a property that inherits takes its parent's value, and every other property its initial
        // value. The parent's is written as no inline value until the element's own stylesheets are seen to give
        // it another (TrackInherited).
        private static ArbitraryStyle UnsetOf(Consumer consumer)
        {
            var property = consumer.Property!.Value;
            return !Inherits(property) ? new ArbitraryStyle(property, StyleKeyword.Initial)
                : consumer.Inherited ?? new ArbitraryStyle(property, StyleKeyword.Null);
        }

        // An inherited unset left to the engine shows the parent's value unless a rule of the element's own sets
        // the property, which the browser's unset does not let through: the parent's value is then written, and
        // followed as it changes.
        private static void TrackInherited(VisualElement target, Consumer consumer)
        {
            var parent = target.hierarchy.parent;
            if (parent == null || target.panel == null)
            {
                return;
            }
            var property = consumer.Property!.Value;
            var inherited = ResolvedOf(parent, property);
            var shown = consumer.Inherited ?? ResolvedOf(target, property);
            if (SameValue(shown, inherited))
            {
                return;
            }
            consumer.Inherited = inherited;
            StyleArbitraryValueResolver.Apply(target, inherited, consumer.Key);
            SettleWritten(target, consumer, new List<ArbitraryProperty> { property });
        }

        private static ArbitraryStyle ResolvedOf(VisualElement element, ArbitraryProperty property)
        {
            var style = element.resolvedStyle;
            return property == ArbitraryProperty.TextColor ? new ArbitraryStyle(property, style.color)
                : new ArbitraryStyle(property, property == ArbitraryProperty.FontSize ? style.fontSize : style.letterSpacing,
                    LengthUnit.Pixel);
        }

        private static bool SameValue(ArbitraryStyle a, ArbitraryStyle b)
            => a.Keyword == b.Keyword && a.Value == b.Value && a.Color == b.Color;

        private static bool Matches(StringBuilder built, string applied) => built.Equals(applied.AsSpan());

        // The property an inline utility writes, read by parsing it with a value of the type Tailwind gives it in
        // place of its own: a colour where DefaultHint adds one or the bracket's hint names one, else the bracket
        // with each var() read as 0, else a length, a number or an angle; a var() in a modifier is read as 50%.
        // Null when none parses to a layer the unset value can be written to, or where keywordOnly is false, to any
        // layer at all. probe is the utility that parsed.
        private static ArbitraryProperty? WrittenProperty(string utility, UtilityShape shape, string? addedHint,
            out string? probe, bool keywordOnly = true)
        {
            probe = null;
            string[] candidates;
            if (shape.MainOpen < 0)
            {
                candidates = new[] { WithVarsAs(utility, "50%") };
            }
            else
            {
                var head = utility.Substring(0, shape.MainOpen + 1);
                var hint = shape.Hint == null ? string.Empty : shape.Hint + ":";
                var tail = "]" + WithVarsAs(utility.Substring(shape.MainClose + 1), "50%");
                var body = utility.Substring(shape.BodyStart, shape.MainClose - shape.BodyStart);
                // A 0 or a length under a colour hint is no colour, so a written colour hint is probed with one too.
                var typed = addedHint ?? (shape.Hint == "color" ? shape.Hint : null);
                candidates = typed != null
                    ? new[] { head + typed + ":" + (typed == "color" ? "#000" : "400") + tail }
                    : new[]
                    {
                        head + hint + WithVarsAs(body, "0") + tail, head + hint + "0px" + tail, head + hint + "0" + tail,
                        head + hint + "0deg" + tail,
                    };
            }
            foreach (var candidate in candidates)
            {
                if (StyleArbitraryValueResolver.TryParseLayer(candidate, out var parsed)
                    && (!keywordOnly || StyleArbitraryValueResolver.WritesKeyword(parsed.Property)))
                {
                    probe = candidate;
                    return parsed.Property;
                }
            }
            return null;
        }

        private static string WithVarsAs(string text, string value)
        {
            var written = new StringBuilder();
            for (var at = 0; at < text.Length;)
            {
                var call = IndexOfVar(text, at, text.Length);
                written.Append(text, at, call - at);
                if (call == text.Length)
                {
                    break;
                }
                var close = MatchingClose(text, call + 3, text.Length);
                if (close < 0)
                {
                    break;
                }
                written.Append(value);
                at = close + 1;
            }
            return written.ToString();
        }

        // The hint Tailwind's typing gives an untyped value opening with var(), which its type inference declines
        // to test, so the utility writes its default: a colour for the utilities StyleColorValueParser.
        // DefaultsToColour names, and a weight for font-. Null where the value is typed or tested, and where a
        // modifier follows, which already makes it a colour.
        private static string? DefaultHint(string utility, in UtilityShape shape)
        {
            if (shape.MainOpen < 0 || shape.Hint != null || shape.HasModifier
                || string.Compare(utility, shape.BodyStart, "var(", 0, 4, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return null;
            }
            var root = utility.Substring(utility[0] == '-' ? 1 : 0, shape.MainOpen - (utility[0] == '-' ? 2 : 1));
            return StyleColorValueParser.DefaultsToColour(root) ? "color" : root == "font" ? "weight" : null;
        }
    }
}
