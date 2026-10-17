using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    internal static partial class StyleCustomProperties
    {
        private static int[] Repeat(int declaration, int count)
        {
            var declarations = new int[count];
            for (var i = 0; i < count; i++)
            {
                declarations[i] = declaration;
            }
            return declarations;
        }

        // The payloads a resolution applies: the resolved utility, or for a shorthand holding several values its
        // sides, in CSS's order of one to four values (two for gap). Null where the value count is one the
        // shorthand does not take, or where a modifier read from a var() is not a percentage, which is what
        // color-mix() takes it as.
        private static string[]? Split(Consumer consumer, string source)
        {
            if (consumer.ModifierReadsVariable)
            {
                var modifier = source.LastIndexOf("/[", StringComparison.Ordinal);
                if (modifier < 0 || source.Length < modifier + 4 || source[source.Length - 2] != '%')
                {
                    return null;
                }
            }
            if (consumer.Sides is not { } sides)
            {
                return new[] { source };
            }
            var shape = UtilityShape.Of(source);
            var values = SplitValues(source, shape.BodyStart, shape.MainClose);
            if (values.Count <= 1)
            {
                return new[] { source };
            }
            if (values.Count > sides.Length)
            {
                return null;
            }
            var hint = shape.Hint == null ? string.Empty : shape.Hint + ":";
            var payloads = new string[sides.Length];
            for (var side = 0; side < sides.Length; side++)
            {
                // Top, right, bottom, left: a missing right is the top, a missing bottom the top and a missing
                // left the right.
                var index = side < values.Count ? side : side == 3 && values.Count > 1 ? 1 : 0;
                payloads[side] = sides[side] + "[" + hint + values[index] + "]";
            }
            return payloads;
        }

        private static List<string> SplitValues(string text, int start, int end)
        {
            var values = new List<string>();
            var depth = 0;
            var from = start;
            for (var i = start; i <= end; i++)
            {
                var c = i < end ? text[i] : '_';
                depth += c == '(' ? 1 : c == ')' ? -1 : 0;
                if (depth != 0 || c != '_' || (i > start && text[i - 1] == '\\'))
                {
                    continue;
                }
                if (i > from)
                {
                    values.Add(text.Substring(from, i - from));
                }
                from = i + 1;
            }
            return values;
        }

        // Whether every payload parses to an inline layer, of property where one is given.
        private static bool AllWrite(string[] payloads, ArbitraryProperty? property)
            => Array.TrueForAll(payloads, payload => StyleArbitraryValueResolver.TryParseLayer(payload, out var parsed)
                && (property == null || parsed.Property == property));
    }
}
