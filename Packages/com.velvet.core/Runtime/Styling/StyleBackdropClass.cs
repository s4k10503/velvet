#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Velvet
{
    // The functions a backdrop-filter holds. The first nine are in the order Tailwind composes its backdrop-*
    // utilities into one declaration.
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
        DropShadow,
    }

    // One function of a backdrop-filter: blur's standard deviation in px, hue-rotate's angle in degrees, the other
    // amounts a factor, or a drop shadow.
    internal readonly struct BackdropOp
    {
        public readonly BackdropFunction Function;
        public readonly float Amount;
        // A blur the element measures (an em, a viewport unit), which Amount leaves at 0.
        public readonly StyleLengthExpression? Measured;
        public readonly DropShadowLayer Shadow;

        public BackdropOp(BackdropFunction function, float amount, StyleLengthExpression? measured = null)
        {
            Function = function;
            Amount = amount;
            Measured = measured;
            Shadow = default;
        }

        public BackdropOp(DropShadowLayer shadow)
        {
            Function = BackdropFunction.DropShadow;
            Amount = 0f;
            Measured = null;
            Shadow = shadow;
        }
    }

    // The backdrop-filter an element's backdrop-* utilities declare. No function is `none`.
    internal sealed class BackdropSpec
    {
        internal static readonly BackdropSpec None = new(Array.Empty<BackdropOp>());

        internal readonly BackdropOp[] Ops;

        internal BackdropSpec(BackdropOp[] ops) => Ops = ops;

        // The declaration in CSS's spelling, a measured length as "measured".
        public override string ToString()
        {
            if (Ops.Length == 0)
            {
                return "none";
            }
            var text = new StringBuilder();
            foreach (var op in Ops)
            {
                text.Append(text.Length > 0 ? " " : string.Empty).Append(StyleBackdropClass.NameOf(op.Function)).Append('(')
                    .Append(Argument(op)).Append(')');
            }
            return text.ToString();
        }

        private static string Argument(in BackdropOp op)
        {
            if (op.Function == BackdropFunction.DropShadow)
            {
                var shadow = op.Shadow;
                var color = float.IsNaN(shadow.Color.r) ? "currentcolor" : "#" + ColorUtility.ToHtmlStringRGBA(shadow.Color);
                return shadow.Measured != null ? "measured " + color
                    : $"{Number(shadow.X)}px {Number(shadow.Y)}px {Number(shadow.Deviation)}px {color}";
            }
            if (op.Measured != null)
            {
                return "measured";
            }
            var unit = op.Function == BackdropFunction.Blur ? "px" : op.Function == BackdropFunction.HueRotate ? "deg" : "";
            return Number(op.Amount) + unit;
        }

        private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    // Parses Tailwind v4's backdrop-* utilities into the backdrop-filter they declare. Every one of them writes the
    // backdrop-filter declaration, and the class holding it is the last important one, or else the last one, the
    // composed class source having put the family's tokens in Tailwind's order (StyleFamilyOrder). A function
    // utility also sets its function's own slot, as Tailwind writes each into a custom property of its own, and the
    // declaration it writes composes every slot set; backdrop-filter-none and backdrop-filter-[…] write theirs
    // literally.
    internal static class StyleBackdropClass
    {
        private const string Prefix = "backdrop-";
        private const string DeclarationPrefix = "backdrop-filter-";
        private const int SlotCount = 9;

        private static readonly string[] s_names =
        {
            "blur", "brightness", "contrast", "grayscale", "hue-rotate", "invert", "opacity", "saturate", "sepia",
            "drop-shadow",
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
            public BackdropOp Op;
        }

        private struct Declarer
        {
            public bool Set;
            public bool Important;
            // Null where the class composing the slots holds the declaration.
            public BackdropOp[]? Literal;
        }

        // False where no class declares a backdrop-filter. A composed declaration over a slot whose bracket the
        // browser rejects is invalid when its value is computed, which leaves backdrop-filter at none.
        public static bool TryExtract(string[]? classNames, out BackdropSpec? spec)
        {
            spec = null;
            var slots = new Slot[SlotCount];
            var declarer = default(Declarer);
            foreach (var cls in classNames ?? Array.Empty<string>())
            {
                var core = StyleArbitraryValueResolver.StripImportant(cls ?? string.Empty, out var important);
                if (!IsBackdropClass(core) || StyleCustomPropertyClass.IsCustomPropertyToken(core))
                {
                    continue;
                }
                Offer(core, important, slots, ref declarer);
            }
            if (!declarer.Set)
            {
                return false;
            }
            spec = declarer.Literal != null ? new BackdropSpec(declarer.Literal) : Composed(slots);
            return true;
        }

        private static void Offer(string core, bool important, Slot[] slots, ref Declarer declarer)
        {
            BackdropOp[]? literal = null;
            if (TryReadSlot(core, out var op, out var rejected))
            {
                ref var slot = ref slots[(int)op.Function];
                if (!slot.Set || !slot.Important || important)
                {
                    slot = new Slot { Set = true, Important = important, Rejected = rejected, Op = op };
                }
            }
            else if (!TryReadDeclaration(core, out literal))
            {
                return;
            }
            if (!declarer.Set || !declarer.Important || important)
            {
                declarer = new Declarer { Set = true, Important = important, Literal = literal };
            }
        }

        private static BackdropSpec Composed(Slot[] slots)
        {
            var ops = new List<BackdropOp>(SlotCount);
            foreach (var slot in slots)
            {
                if (slot.Rejected)
                {
                    return BackdropSpec.None;
                }
                if (slot.Set)
                {
                    ops.Add(slot.Op);
                }
            }
            return new BackdropSpec(ops.ToArray());
        }

        // backdrop-filter-none, or backdrop-filter-[…] holding a value the browser takes. One it rejects is dropped
        // when the stylesheet is parsed, so it declares nothing.
        private static bool TryReadDeclaration(string core, out BackdropOp[]? literal)
        {
            literal = null;
            if (core == DeclarationPrefix + "none")
            {
                literal = Array.Empty<BackdropOp>();
                return true;
            }
            var body = core.AsSpan();
            if (!body.StartsWith(DeclarationPrefix.AsSpan(), StringComparison.Ordinal)
                || !StyleArbitraryValueResolver.TryStripBrackets(body, DeclarationPrefix.Length, out var inner)
                || !StyleArbitraryValueResolver.TrySplitTypeHint(inner, out _, out var value))
            {
                return false;
            }
            return BackdropFilterList.TryParse(value, out literal);
        }

        // A function utility: the op it sets its slot to, and whether its bracket is one the browser rejects, which
        // still sets the slot.
        private static bool TryReadSlot(string core, out BackdropOp op, out bool rejected)
        {
            op = default;
            rejected = false;
            var negate = core[0] == '-';
            var body = core.AsSpan((negate ? 1 : 0) + Prefix.Length);
            for (var i = 0; i < SlotCount; i++)
            {
                if (!body.StartsWith(s_names[i].AsSpan(), StringComparison.Ordinal))
                {
                    continue;
                }
                var function = (BackdropFunction)i;
                if (negate && function != BackdropFunction.HueRotate)
                {
                    return false;
                }
                return TryReadSuffix(function, body.Slice(s_names[i].Length), negate, out op, out rejected);
            }
            return false;
        }

        private static bool TryReadSuffix(BackdropFunction function, ReadOnlySpan<char> rest, bool negate,
            out BackdropOp op, out bool rejected)
        {
            op = default;
            rejected = false;
            // The bare grayscale, invert and sepia are 100%.
            if (rest.Length == 0)
            {
                op = new BackdropOp(function, 1f);
                return !negate && function is BackdropFunction.Grayscale or BackdropFunction.Invert or BackdropFunction.Sepia;
            }
            if (rest.Length < 2 || rest[0] != '-')
            {
                return false;
            }
            var suffix = rest.Slice(1);
            if (StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner))
            {
                if (!StyleArbitraryValueResolver.TrySplitTypeHint(inner, out _, out var value))
                {
                    return false;
                }
                rejected = !BackdropFilterList.TryReadArgument(function, value, negate, out op);
                op = rejected ? new BackdropOp(function, 0f) : op;
                return true;
            }
            return TryReadNamed(function, suffix, negate, out op);
        }

        // A theme blur, or Tailwind v4's bare whole number: a percentage for the amounts, clamped to 1 where CSS
        // clamps the function, and degrees for hue-rotate.
        private static bool TryReadNamed(BackdropFunction function, ReadOnlySpan<char> suffix, bool negate, out BackdropOp op)
        {
            op = default;
            if (function == BackdropFunction.Blur)
            {
                var found = StyleFilterValueParser.TryGetBlurPreset(suffix.ToString(), out var px);
                op = new BackdropOp(function, px);
                return found;
            }
            if (!StyleFilterValueParser.TryParseWholeNumber(suffix, out var whole))
            {
                return false;
            }
            op = new BackdropOp(function, function == BackdropFunction.HueRotate ? (negate ? -whole : whole)
                : Mathf.Min(whole / 100f, BackdropFilterList.CeilingOf(function)));
            return true;
        }
    }

    // CSS's <filter-value-list> as backdrop-filter-[…] writes it, with `_` for a space, and the argument of one
    // function, which the backdrop-* brackets share.
    internal static class BackdropFilterList
    {
        // grayscale(), invert(), sepia() and opacity() clamp their amount to 1.
        internal static float CeilingOf(BackdropFunction function)
            => function is BackdropFunction.Grayscale or BackdropFunction.Invert or BackdropFunction.Sepia
                or BackdropFunction.Opacity ? 1f : float.PositiveInfinity;

        // False for a list the browser rejects. A url() names an SVG filter, which nothing here can resolve, and a
        // reference that does not resolve runs as no function, the rest of the list running as written
        // (styling-filters.md).
        public static bool TryParse(ReadOnlySpan<char> value, out BackdropOp[]? ops)
        {
            ops = null;
            value = value.Trim("_ ".AsSpan());
            if (value.SequenceEqual("none".AsSpan()))
            {
                ops = Array.Empty<BackdropOp>();
                return true;
            }
            var list = new List<BackdropOp>();
            var references = 0;
            while (value.Length > 0)
            {
                if (!TryNextFunction(ref value, out var name, out var argument))
                {
                    return false;
                }
                if (name.SequenceEqual("url".AsSpan()))
                {
                    references++;
                    continue;
                }
                if (!TryFunction(name, out var function) || !TryReadArgument(function, argument, false, out var op))
                {
                    return false;
                }
                list.Add(op);
            }
            ops = list.ToArray();
            return list.Count + references > 0;
        }

        // `name(argument)` at the head of value, which is left after it and the separators following it.
        private static bool TryNextFunction(ref ReadOnlySpan<char> value, out ReadOnlySpan<char> name,
            out ReadOnlySpan<char> argument)
        {
            name = argument = default;
            var open = value.IndexOf('(');
            if (open <= 0)
            {
                return false;
            }
            var close = StyleCustomProperties.MatchingClose(value.ToString(), open, value.Length);
            if (close < 0)
            {
                return false;
            }
            name = value.Slice(0, open);
            argument = value.Slice(open + 1, close - open - 1).Trim("_ ".AsSpan());
            var after = value.Slice(close + 1);
            var trimmed = after.TrimStart("_ ".AsSpan());
            value = trimmed;
            return after.Length == 0 || trimmed.Length < after.Length;
        }

        private static bool TryFunction(ReadOnlySpan<char> name, out BackdropFunction function)
        {
            for (var i = 0; i <= (int)BackdropFunction.DropShadow; i++)
            {
                if (name.SequenceEqual(StyleBackdropClass.NameOf((BackdropFunction)i).AsSpan()))
                {
                    function = (BackdropFunction)i;
                    return true;
                }
            }
            function = default;
            return false;
        }

        // The argument of one function, in the grammar of the filter utility of the same name
        // (StyleFilterValueParser); an empty one is the function's default: blur and hue-rotate 0, the amounts 1.
        internal static bool TryReadArgument(BackdropFunction function, ReadOnlySpan<char> value, bool negate, out BackdropOp op)
        {
            op = default;
            if (function == BackdropFunction.DropShadow)
            {
                var parsed = StyleFilterValueParser.TryParseShadow(value, out var shadow);
                op = new BackdropOp(shadow);
                return parsed;
            }
            if (value.Length == 0)
            {
                op = new BackdropOp(function, function is BackdropFunction.Blur or BackdropFunction.HueRotate ? 0f : 1f);
                return true;
            }
            if (function == BackdropFunction.Opacity)
            {
                var parsed = StyleFilterValueParser.TryParseAmount(value, out var amount) && amount >= 0f;
                op = new BackdropOp(function, Mathf.Min(amount, 1f));
                return parsed;
            }
            if (StyleFilterValueParser.TryParseFilterValue(StyleBackdropClass.NameOf(function) + "-", value, negate,
                    out var style) != true)
            {
                return false;
            }
            op = new BackdropOp(function, style.Value, style.Expression);
            return true;
        }
    }
}
