using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The axis a divide border runs along. divide-x draws a LEFT border between columns,
    // divide-y draws a TOP border between rows (mirrors GapAxis, minus Auto — the divide
    // utilities are always explicitly x or y).
    internal enum DivideAxis
    {
        Horizontal,
        Vertical,
    }

    // A resolved divide-* utility: the axis + width of the inter-child border, and an optional color.
    // A divide is only active when an axis class (divide-x / divide-y / divide-x-N / divide-x-[..]) is
    // present — a lone divide-{color} does nothing (the color needs a width to show).
    internal readonly struct DivideSpec
    {
        public readonly DivideAxis Axis;
        public readonly float Width;
        public readonly bool HasColor;
        public readonly Color Color;

        public DivideSpec(DivideAxis axis, float width, bool hasColor, Color color)
        {
            Axis = axis;
            Width = width;
            HasColor = hasColor;
            Color = color;
        }
    }

    // Parses Velvet's divide-x / divide-y (and divide-x-{0,2,4,8} widths, the
    // divide-x-[Npx] JIT arbitrary form, and divide-{color}) into a DivideSpec for
    // StyleDivideManipulator, which writes the inter-child leading border (border-left for x,
    // border-top for y) on every child except the first — the `> * + *` divider rule, which
    // no USS selector can express (UI Toolkit has no :first-child and no `> *` child combinator).
    //
    // Deviations (UI Toolkit constraints):
    //   - divide-{style}: divide-solid is the only style UI Toolkit can render (USS has no
    //     border-style), so divide-dashed / divide-dotted / divide-double are unsupported and ignored.
    //   - divide-*-reverse is unsupported.
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
                if (!string.IsNullOrEmpty(cls) && cls.StartsWith("divide-", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // Scans classNames for the divide utilities and accumulates the axis + width (last axis class
        // wins) and color (last color class wins). Returns false when no axis class is present — a lone
        // divide-{color} is inert.
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

            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls) || !cls.StartsWith("divide-", StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryParseAxisWidth(cls, out var a, out var w))
                {
                    foundAxis = true;
                    axis = a;
                    width = w;
                }
                else if (TryParseColor(cls, out var c))
                {
                    hasColor = true;
                    color = c;
                }
                // Otherwise an unsupported divide-* (divide-dashed, divide-solid, divide-*-reverse, …):
                // skip it without disturbing the accumulated spec.
            }

            if (!foundAxis)
            {
                return false;
            }
            spec = new DivideSpec(axis, width, hasColor, color);
            return true;
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
            // is rejected (only px / unitless is meaningful).
            if (suffix.Length >= 2 && suffix[0] == '[')
            {
                return StyleArbitraryValueResolver.TryParseArbitraryPixels(suffix.AsSpan(), out width);
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
