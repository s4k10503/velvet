using System;
using System.Collections.Generic;

namespace Velvet
{
    // Tailwind v4's data-type tests (utils/infer-data-type.ts and utils/is-color.ts), which decide which branch a
    // utility that reads a bracket more than one way writes it into. Each takes the bracket's text with `_` still
    // standing for a space, and decodes it as Tailwind does first. Where a utility reads a bracket as either a
    // length or a colour, a value these call no length goes to the colour branch, where a browser discards it
    // unless it is a colour.
    internal static class TailwindDataTypes
    {
        internal static readonly string[] s_lengthUnits =
        {
            "cm", "mm", "Q", "in", "pc", "pt", "px", "em", "ex", "ch", "cap", "ic", "rem", "rex", "rch", "rcap",
            "ric", "lh", "rlh", "vw", "vh", "vmin", "vmax", "vb", "vi", "svw", "svh", "svi", "svb", "svmin",
            "svmax", "lvw", "lvh", "lvi", "lvb", "lvmin", "lvmax", "dvw", "dvh", "dvi", "dvb", "dvmin", "dvmax",
            "cqw", "cqh", "cqi", "cqb", "cqmin", "cqmax",
        };

        public static bool IsLength(ReadOnlySpan<char> bracket)
        {
            var value = Decode(bracket);
            if (TailwindMathSpacing.HasMathFunction(value)
                || value.StartsWith("--spacing(", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            var end = NumberEnd(value, withExponent: true);
            if (end > 0 && IsUnit(value, end))
            {
                return true;
            }
            end = NumberEnd(value, withExponent: false);
            return end > 0 && IsUnit(value, end);
        }

        public static bool IsNumber(ReadOnlySpan<char> bracket)
        {
            var value = Decode(bracket);
            return NumberEnd(value, withExponent: true) == value.Length
                || NumberEnd(value, withExponent: false) == value.Length
                || TailwindMathSpacing.HasMathFunction(value);
        }

        public static bool IsPercentage(ReadOnlySpan<char> bracket)
        {
            var value = Decode(bracket);
            if (TailwindMathSpacing.HasMathFunction(value))
            {
                return true;
            }
            if (value.Length < 2 || value[value.Length - 1] != '%')
            {
                return false;
            }
            var number = value.Substring(0, value.Length - 1);
            return NumberEnd(number, withExponent: true) == number.Length
                || NumberEnd(number, withExponent: false) == number.Length;
        }

        // Tailwind's colour test (utils/is-color.ts): a leading '#', a colour function, or a named colour.
        public static bool IsColor(ReadOnlySpan<char> bracket)
        {
            var value = Decode(bracket);
            if (value.Length > 0 && value[0] == '#')
            {
                return true;
            }
            foreach (var function in s_colorFunctions)
            {
                if (value.StartsWith(function, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return s_namedColors.Contains(value);
        }

        private static readonly string[] s_colorFunctions =
        {
            "rgb(", "rgba(", "hsl(", "hsla(", "hwb(", "color(", "lab(", "lch(", "oklab(", "oklch(", "light-dark(",
            "color-mix(", "--alpha(",
        };

        private static readonly HashSet<string> s_namedColors = new(StringComparer.OrdinalIgnoreCase)
        {
            "black", "silver", "gray", "white", "maroon", "red", "purple", "fuchsia", "green", "lime", "olive",
            "yellow", "navy", "blue", "teal", "aqua", "aliceblue", "antiquewhite", "aquamarine", "azure", "beige",
            "bisque", "blanchedalmond", "blueviolet", "brown", "burlywood", "cadetblue", "chartreuse", "chocolate",
            "coral", "cornflowerblue", "cornsilk", "crimson", "cyan", "darkblue", "darkcyan", "darkgoldenrod",
            "darkgray", "darkgreen", "darkgrey", "darkkhaki", "darkmagenta", "darkolivegreen", "darkorange",
            "darkorchid", "darkred", "darksalmon", "darkseagreen", "darkslateblue", "darkslategray", "darkslategrey",
            "darkturquoise", "darkviolet", "deeppink", "deepskyblue", "dimgray", "dimgrey", "dodgerblue",
            "firebrick", "floralwhite", "forestgreen", "gainsboro", "ghostwhite", "gold", "goldenrod", "greenyellow",
            "grey", "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki", "lavender", "lavenderblush",
            "lawngreen", "lemonchiffon", "lightblue", "lightcoral", "lightcyan", "lightgoldenrodyellow", "lightgray",
            "lightgreen", "lightgrey", "lightpink", "lightsalmon", "lightseagreen", "lightskyblue", "lightslategray",
            "lightslategrey", "lightsteelblue", "lightyellow", "limegreen", "linen", "magenta", "mediumaquamarine",
            "mediumblue", "mediumorchid", "mediumpurple", "mediumseagreen", "mediumslateblue", "mediumspringgreen",
            "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream", "mistyrose", "moccasin",
            "navajowhite", "oldlace", "olivedrab", "orange", "orangered", "orchid", "palegoldenrod", "palegreen",
            "paleturquoise", "palevioletred", "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue",
            "rebeccapurple", "rosybrown", "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell",
            "sienna", "skyblue", "slateblue", "slategray", "slategrey", "snow", "springgreen", "steelblue", "tan",
            "thistle", "tomato", "turquoise", "violet", "wheat", "whitesmoke", "yellowgreen", "transparent",
            "currentcolor", "canvas", "canvastext", "linktext", "visitedtext", "activetext", "buttonface",
            "buttontext", "buttonborder", "field", "fieldtext", "highlight", "highlighttext", "selecteditem",
            "selecteditemtext", "mark", "marktext", "graytext", "accentcolor", "accentcolortext",
        };

        // A single <line-width> as Tailwind's line-width test takes one: a length, a number, or a keyword.
        public static bool IsLineWidthKeyword(ReadOnlySpan<char> bracket)
        {
            var value = Decode(bracket);
            return value == "thin" || value == "medium" || value == "thick";
        }

        // Tailwind's absolute-size and relative-size tests, which text-[…] reads as a font size.
        public static bool IsFontSizeKeyword(ReadOnlySpan<char> bracket)
            => Array.IndexOf(s_fontSizeKeywords, Decode(bracket)) >= 0;

        private static readonly string[] s_fontSizeKeywords =
        {
            "xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large", "larger", "smaller",
        };

        private static bool IsUnit(string value, int at)
        {
            var unit = value.Substring(at);
            return Array.IndexOf(s_lengthUnits, unit) >= 0;
        }

        // Where HAS_NUMBER, [+-]?\d*\.?\d+(?:[eE][+-]?\d+)?, ends when it matches from the start, or -1.
        internal static int NumberEnd(string value, bool withExponent)
        {
            var at = 0;
            if (at < value.Length && (value[at] == '+' || value[at] == '-'))
            {
                at++;
            }
            var digits = Digits(value, at);
            var end = digits;
            if (end < value.Length && value[end] == '.')
            {
                var fraction = Digits(value, end + 1);
                if (fraction == end + 1)
                {
                    end = digits;
                }
                else
                {
                    end = fraction;
                }
            }
            if (end == at)
            {
                return -1;
            }
            if (withExponent && end < value.Length && (value[end] == 'e' || value[end] == 'E'))
            {
                var exponent = end + 1;
                if (exponent < value.Length && (value[exponent] == '+' || value[exponent] == '-'))
                {
                    exponent++;
                }
                var exponentEnd = Digits(value, exponent);
                if (exponentEnd > exponent)
                {
                    end = exponentEnd;
                }
            }
            return end;
        }

        // A for over the string's length, so no edit to the loop body can leave it spinning.
        private static int Digits(string value, int at)
        {
            for (var i = at; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                {
                    return i;
                }
            }
            return Math.Max(at, value.Length);
        }

        private static string Decode(ReadOnlySpan<char> bracket) => bracket.ToString().Replace('_', ' ');
    }
}
