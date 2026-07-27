using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The axis a divide border runs along. divide-x divides columns, divide-y divides rows (mirrors GapAxis,
    // minus Auto — the divide utilities are always explicitly x or y). Which PHYSICAL edge of that axis
    // carries the border is resolved separately, from the container's direction and the reverse marker — see
    // StyleDivideManipulator.ResolveEdge.
    internal enum DivideAxis
    {
        Horizontal,
        Vertical,
    }

    // The physical edge of a divided child that carries the divider border. Shared with DivideDashPainter,
    // which needs the edge rather than the axis to place its stroke: a trailing divider's dashes run down the
    // child's right / bottom edge, where the solid border of the same divider would paint.
    internal enum DivideEdge
    {
        Left,
        Top,
        Right,
        Bottom,
    }

    // A resolved divide-* utility: the axis + width of the inter-child border, an optional color, the line
    // style, and whether the divider is reversed onto the axis's trailing edge. A divide is only active when
    // an axis class (divide-x / divide-y / divide-x-N / divide-x-[..]) is present — a lone divide-{color},
    // and a lone divide-x-reverse, do nothing (both need a width to show).
    internal readonly struct DivideSpec
    {
        public readonly DivideAxis Axis;
        public readonly float Width;
        public readonly bool HasColor;
        public readonly Color Color;
        public readonly BorderLineStyle Style;

        // The divide-x-reverse / divide-y-reverse marker FOR THE AXIS THIS SPEC RESOLVED TO. Unlike gap's
        // Auto axis, a divide always names its axis in the class itself, so the cross-axis marker can never
        // become relevant later and is dropped at parse time rather than carried per axis: divide-y with a
        // divide-x-reverse is simply not reversed.
        public readonly bool Reverse;

        public DivideSpec(DivideAxis axis, float width, bool hasColor, Color color, BorderLineStyle style, bool reverse)
        {
            Axis = axis;
            Width = width;
            HasColor = hasColor;
            Color = color;
            Style = style;
            Reverse = reverse;
        }
    }

    // Parses Velvet's divide-x / divide-y (and divide-x-{0,2,4,8} widths, the
    // divide-x-[Npx] JIT arbitrary form, divide-{color}, and the divide-x-reverse / divide-y-reverse
    // markers) into a DivideSpec for StyleDivideManipulator, which writes the inter-child border on
    // every child except the first — the `> * + *` divider rule, which no USS selector can express
    // (UI Toolkit has no :first-child and no `> *` child combinator).
    //
    // Deviations (UI Toolkit constraints):
    //   - divide-dashed / divide-dotted have no UI Toolkit border-style, so they are painted by
    //     DivideDashPainter on each divided child's own generateVisualContent (the manipulator still writes
    //     the real gutter width and masks the color with the sentinel). divide-double is still unsupported.
    //   - A single element resolves ONE axis (last axis class wins, CSS-cascade order); divide-x and
    //     divide-y are not combined onto the same element.
    internal static class StyleDivideClass
    {
        // Divide width scale: divide-x-0/2/4/8; the bare divide-x is 1px.
        private static readonly Dictionary<string, float> WidthScale = new()
        {
            ["0"] = 0f,
            ["2"] = 2f,
            ["4"] = 4f,
            ["8"] = 8f,
        };

        // Single-token half of HasDivideClass. Its own predicate so the prefix has ONE definition — both
        // the array scan below and the variant-payload gate (StyleVariantPayload) resolve the family here.
        public static bool IsDivideToken(string cls)
            => !string.IsNullOrEmpty(cls) && cls.StartsWith("divide-", StringComparison.Ordinal);

        // Cheap early-out gate: true when ANY class begins with the divide- prefix. No allocation —
        // used to skip the full TryExtract scan on the ~99% of elements with no divide class.
        public static bool HasDivideClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (IsDivideToken(cls))
                {
                    return true;
                }
            }
            return false;
        }

        // Scans classNames for the divide utilities and accumulates the axis + width (last axis class
        // wins), color (last color class wins) and the per-axis reverse markers. Returns false when no axis
        // class is present — a lone divide-{color}, or a lone divide-x-reverse, is inert.
        public static bool TryExtract(string[] classNames, out DivideSpec spec)
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }

            var foundAxis = false;
            var axis = DivideAxis.Horizontal;
            var width = 0f;
            var hasColor = false;
            var color = default(Color);
            var style = BorderLineStyle.Solid;
            var xReverse = false;
            var yReverse = false;

            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls) || !cls.StartsWith("divide-", StringComparison.Ordinal))
                {
                    continue;
                }

                // Checked before the axis/width parse: divide-x-reverse shares the divide-x prefix, and
                // reading it as a width would depend on "reverse" failing every width parse rather than on
                // this being a marker.
                if (TryParseReverse(cls, out var reverseAxis))
                {
                    if (reverseAxis == DivideAxis.Horizontal)
                    {
                        xReverse = true;
                    }
                    else
                    {
                        yReverse = true;
                    }
                }
                else if (TryParseAxisWidth(cls, out var a, out var w))
                {
                    foundAxis = true;
                    axis = a;
                    width = w;
                }
                else if (TryParseStyle(cls, out var st))
                {
                    style = st;
                }
                else if (TryParseColor(cls, out var c))
                {
                    hasColor = true;
                    color = c;
                }
                // Otherwise an unsupported divide-* (divide-double, …): skip it without disturbing the
                // accumulated spec.
            }

            if (!foundAxis)
            {
                return false;
            }
            // The marker on the OTHER axis is discarded here: the axis is final by now, and a marker that
            // does not name it can never apply.
            var reverse = axis == DivideAxis.Horizontal ? xReverse : yReverse;
            spec = new DivideSpec(axis, width, hasColor, color, style, reverse);
            return true;
        }

        // divide-x-reverse / divide-y-reverse. Each is an ABSOLUTE per-axis instruction — "put the divider on
        // the trailing physical edge" — that Tailwind never conditions on flex-direction, so
        // StyleDivideManipulator.ResolveEdge OR's it with a detected row-reverse / column-reverse rather than
        // XOR'ing: the idiomatic flex-row-reverse divide-x divide-x-reverse still lands trailing instead of
        // cancelling back to leading. Matched exactly, so a divide-x-reverse-something is left to the parses
        // below rather than swallowed here.
        private static bool TryParseReverse(string cls, out DivideAxis axis)
        {
            switch (cls)
            {
                case "divide-x-reverse":
                    axis = DivideAxis.Horizontal;
                    return true;
                case "divide-y-reverse":
                    axis = DivideAxis.Vertical;
                    return true;
                default:
                    axis = DivideAxis.Horizontal;
                    return false;
            }
        }

        // divide-solid / divide-dashed / divide-dotted (the last one wins in the cascade). divide-solid is the
        // default and a recognized reset (it overrides an earlier divide-dashed). Returns false for anything
        // else so the caller falls through to the color / axis parse.
        private static bool TryParseStyle(string cls, out BorderLineStyle style)
        {
            switch (cls)
            {
                case "divide-dashed":
                    style = BorderLineStyle.Dashed;
                    return true;
                case "divide-dotted":
                    style = BorderLineStyle.Dotted;
                    return true;
                case "divide-solid":
                    style = BorderLineStyle.Solid;
                    return true;
                default:
                    style = BorderLineStyle.Solid;
                    return false;
            }
        }

        // divide-x / divide-y, divide-x-{0,2,4,8}, divide-x-[Npx] (and the y forms).
        private static bool TryParseAxisWidth(string cls, out DivideAxis axis, out float width)
        {
            axis = DivideAxis.Horizontal;
            width = 0f;

            string suffix;
            if (cls.StartsWith("divide-x", StringComparison.Ordinal))
            {
                axis = DivideAxis.Horizontal;
                suffix = cls.Substring("divide-x".Length);
            }
            else if (cls.StartsWith("divide-y", StringComparison.Ordinal))
            {
                axis = DivideAxis.Vertical;
                suffix = cls.Substring("divide-y".Length);
            }
            else
            {
                return false;
            }

            // Bare divide-x / divide-y → 1px (the default width).
            if (suffix.Length == 0)
            {
                width = 1f;
                return true;
            }

            // The remainder must be "-<scale>" or "-[<value>]"; anything else (e.g. "-reverse") is not a width.
            if (suffix[0] != '-')
            {
                return false;
            }
            suffix = suffix.Substring(1);

            // Arbitrary width: divide-x-[2px] (JIT arbitrary value). Realized as a pixel border, so a percentage
            // is rejected (only px / unitless is meaningful). TryParseArbitraryPixels already verifies the
            // bracket shape itself, so a non-bracket suffix falls straight through to the preset scale below
            // without needing its own duplicate guard here.
            if (StyleArbitraryValueResolver.TryParseArbitraryPixels(suffix.AsSpan(), out width))
            {
                return true;
            }

            return WidthScale.TryGetValue(suffix, out width);
        }

        // divide-{color}: a named palette color (divide-gray-200, divide-white, …) or the
        // arbitrary form divide-[#e5e7eb] / divide-[rgb(...)]. Returns false for a non-color suffix so
        // the caller leaves the accumulated spec untouched (e.g. divide-dashed, divide-solid).
        private static bool TryParseColor(string cls, out Color color) =>
            VelvetPalette.TryResolveColorToken(cls.Substring("divide-".Length), out color);
    }
}
