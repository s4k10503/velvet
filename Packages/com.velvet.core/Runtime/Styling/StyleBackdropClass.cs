#nullable enable
using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Velvet
{
    // The functions of a backdrop-filter, in the order Tailwind composes them into one declaration.
    internal enum BackdropFunction
    {
        Blur,
        Brightness,
        Contrast,
        Grayscale,
        HueRotate,
        Invert,
        Opacity,
        Saturate,
        Sepia,
    }

    // The backdrop-filter an element's backdrop-* utilities compose. A function no class sets is absent, which
    // the shader runs as its identity.
    internal sealed class BackdropSpec
    {
        internal const int FunctionCount = 9;

        // Each function's amount: blur's standard deviation in px, hue-rotate's angle in degrees, the rest a factor.
        private readonly float?[] _amounts;

        // A blur the element measures (an em, a viewport unit), which _amounts leaves unset.
        internal readonly StyleLengthExpression? MeasuredBlur;

        internal BackdropSpec(float?[] amounts, StyleLengthExpression? measuredBlur)
        {
            _amounts = amounts;
            MeasuredBlur = measuredBlur;
        }

        internal float AmountOr(BackdropFunction function, float identity) => _amounts[(int)function] ?? identity;

        // The declaration in CSS's spelling, the measured blur as "blur(measured)".
        public override string ToString()
        {
            var text = new StringBuilder();
            for (var i = 0; i < FunctionCount; i++)
            {
                var function = (BackdropFunction)i;
                string? argument = function == BackdropFunction.Blur && MeasuredBlur != null
                    ? "measured"
                    : _amounts[i] is { } amount
                        ? amount.ToString("0.###", CultureInfo.InvariantCulture)
                          + (function == BackdropFunction.Blur ? "px" : function == BackdropFunction.HueRotate ? "deg" : "")
                        : null;
                if (argument == null)
                {
                    continue;
                }
                if (text.Length > 0)
                {
                    text.Append(' ');
                }
                text.Append(StyleBackdropClass.NameOf(function)).Append('(').Append(argument).Append(')');
            }
            return text.ToString();
        }
    }

    // Parses Tailwind v4's backdrop-* utilities into a BackdropSpec. Each function is a slot of its own, as Tailwind
    // writes each into a custom property of its own: the class holding a slot is the last important one, or else the
    // last one, the composed class source having put a family's tokens in Tailwind's order (StyleFamilyOrder).
    internal static class StyleBackdropClass
    {
        private const string Prefix = "backdrop-";

        private static readonly string[] s_names =
        {
            "blur", "brightness", "contrast", "grayscale", "hue-rotate", "invert", "opacity", "saturate", "sepia",
        };

        internal static string NameOf(BackdropFunction function) => s_names[(int)function];

        // Loose, like StyleRingClass.IsRingClass: a token of the family's shape that names no value adds nothing.
        public static bool IsBackdropClass(string? cls)
            => !string.IsNullOrEmpty(cls)
                && (cls!.StartsWith(Prefix, StringComparison.Ordinal) || cls.StartsWith("-" + Prefix, StringComparison.Ordinal));

        public static bool HasBackdropClass(string[]? classNames)
            => classNames != null && Array.Exists(classNames,
                cls => IsBackdropClass(StyleArbitraryValueResolver.StripImportant(cls ?? string.Empty, out _)));

        private struct Slot
        {
            public bool Set;
            public bool Important;
            public bool Rejected;
            public float Amount;
            public StyleLengthExpression? Measured;
        }

        // False where no class sets a function, and where the class holding one carries a bracket the browser
        // rejects: that makes the one declaration Tailwind composes invalid, so the element draws no backdrop.
        public static bool TryExtract(string[]? classNames, out BackdropSpec? spec)
        {
            spec = null;
            var slots = new Slot[BackdropSpec.FunctionCount];
            var any = false;
            foreach (var cls in classNames ?? Array.Empty<string>())
            {
                var core = StyleArbitraryValueResolver.StripImportant(cls ?? string.Empty, out var important);
                if (!IsBackdropClass(core) || !TryRead(core, out var function, out var amount, out var measured, out var rejected))
                {
                    continue;
                }
                ref var slot = ref slots[(int)function];
                if (slot.Set && slot.Important && !important)
                {
                    continue;
                }
                slot = new Slot { Set = true, Important = important, Rejected = rejected, Amount = amount, Measured = measured };
                any = true;
            }
            if (!any)
            {
                return false;
            }
            var amounts = new float?[BackdropSpec.FunctionCount];
            StyleLengthExpression? measuredBlur = null;
            for (var i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Set)
                {
                    continue;
                }
                if (slots[i].Rejected)
                {
                    return false;
                }
                if (slots[i].Measured != null)
                {
                    measuredBlur = slots[i].Measured;
                }
                else
                {
                    amounts[i] = slots[i].Amount;
                }
            }
            spec = new BackdropSpec(amounts, measuredBlur);
            return true;
        }

        // Whether core, a utility without its important modifier, is a backdrop-* utility, and what it sets. A bracket
        // the browser rejects is still one (rejected), since it holds its function's slot.
        private static bool TryRead(string core, out BackdropFunction function, out float amount,
            out StyleLengthExpression? measured, out bool rejected)
        {
            function = default;
            amount = 0f;
            measured = null;
            rejected = false;
            var negate = core.Length > 0 && core[0] == '-';
            var body = core.AsSpan((negate ? 1 : 0) + Prefix.Length);
            for (var i = 0; i < s_names.Length; i++)
            {
                if (!body.StartsWith(s_names[i].AsSpan(), StringComparison.Ordinal))
                {
                    continue;
                }
                function = (BackdropFunction)i;
                var rest = body.Slice(s_names[i].Length);
                if (negate && function != BackdropFunction.HueRotate)
                {
                    return false;
                }
                // The bare grayscale, invert and sepia are 100%.
                if (rest.Length == 0)
                {
                    amount = 1f;
                    return !negate && function is BackdropFunction.Grayscale or BackdropFunction.Invert
                        or BackdropFunction.Sepia;
                }
                if (rest.Length < 2 || rest[0] != '-')
                {
                    return false;
                }
                var suffix = rest.Slice(1);
                if (suffix[0] == '[')
                {
                    if (suffix.Length < 3 || suffix[suffix.Length - 1] != ']'
                        || !StyleArbitraryValueResolver.TrySplitTypeHint(suffix.Slice(1, suffix.Length - 2), out _,
                            out var value))
                    {
                        return false;
                    }
                    rejected = !TryReadBracket(function, value, negate, out amount, out measured);
                    return true;
                }
                return TryReadNamed(function, suffix, negate, out amount);
            }
            return false;
        }

        // The bracket grammar of the filter utility of the same name (StyleFilterValueParser), and opacity()'s, a
        // number or percentage clamped to 1.
        private static bool TryReadBracket(BackdropFunction function, ReadOnlySpan<char> value, bool negate,
            out float amount, out StyleLengthExpression? measured)
        {
            amount = 0f;
            measured = null;
            if (function == BackdropFunction.Opacity)
            {
                if (!StyleFilterValueParser.TryParseAmount(value, out amount) || amount < 0f)
                {
                    return false;
                }
                amount = Mathf.Min(amount, 1f);
                return true;
            }
            if (StyleFilterValueParser.TryParseFilterValue(NameOf(function) + "-", value, negate, out var style) != true)
            {
                return false;
            }
            amount = style.Value;
            measured = style.Expression;
            return true;
        }

        // A theme blur, or Tailwind v4's bare integer: a percentage for the factors, clamped to 1 where CSS clamps the
        // function, and degrees for hue-rotate.
        private static bool TryReadNamed(BackdropFunction function, ReadOnlySpan<char> suffix, bool negate, out float amount)
        {
            amount = 0f;
            if (function == BackdropFunction.Blur)
            {
                return StyleFilterValueParser.TryGetBlurPreset(suffix.ToString(), out amount);
            }
            if (suffix.Length > 6 || !int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
            {
                return false;
            }
            if (function == BackdropFunction.HueRotate)
            {
                amount = negate ? -whole : whole;
                return true;
            }
            amount = whole / 100f;
            if (function is BackdropFunction.Grayscale or BackdropFunction.Invert or BackdropFunction.Opacity
                or BackdropFunction.Sepia)
            {
                amount = Mathf.Min(amount, 1f);
            }
            return true;
        }
    }
}
