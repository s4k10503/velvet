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
        // The utility with every var() replaced by what it resolves to on element, spaces written as `_` and an
        // underscore as `\_`, and the hint DefaultHint adds. False when a var() resolves to nothing.
        private static bool TryBuild(VisualElement element, Consumer consumer, StringBuilder into)
        {
            var utility = consumer.Utility;
            var from = consumer.Shape.MainOpen >= 0 ? consumer.Shape.MainOpen + 1 : 0;
            into.Append(utility, 0, from);
            if (consumer.AddedHint != null)
            {
                into.Append(consumer.AddedHint).Append(':');
            }
            s_resolving.Clear();
            return TryAppendSubstituted(element, utility, from, utility.Length, encoded: true, into);
        }

        // Appends text[start, end) with each var() in it substituted. encoded text is a class bracket, whose
        // spaces are already `_`; anything else is CSS text.
        private static bool TryAppendSubstituted(VisualElement scope, string text, int start, int end, bool encoded,
            StringBuilder into)
        {
            if (s_depth >= MaxDepth)
            {
                return false;
            }
            s_depth++;
            try
            {
                return TryAppendCalls(scope, text, start, end, encoded, into);
            }
            finally
            {
                s_depth--;
            }
        }

        private static bool TryAppendCalls(VisualElement scope, string text, int start, int end, bool encoded,
            StringBuilder into)
        {
            var at = start;
            while (at < end)
            {
                var call = IndexOfVar(text, at, end);
                AppendText(text, at, call, encoded, into);
                if (call == end)
                {
                    return true;
                }
                var nameStart = call + 4;
                var nameEnd = nameStart;
                while (nameEnd < end && text[nameEnd] != ',' && text[nameEnd] != ')')
                {
                    nameEnd++;
                }
                var close = MatchingClose(text, call + 3, end);
                if (close < 0)
                {
                    return false;
                }
                var name = encoded
                    ? text.Substring(nameStart, nameEnd - nameStart).Trim(' ', '_')
                    : text.Substring(nameStart, nameEnd - nameStart).Trim();
                var mark = into.Length;
                if (!name.StartsWith("--", StringComparison.Ordinal) || !TryAppendVariable(scope, name, into))
                {
                    into.Length = mark;
                    if (nameEnd >= close)
                    {
                        return false;
                    }
                    var (fallbackStart, fallbackEnd) = Trimmed(text, nameEnd + 1, close, encoded);
                    if (!TryAppendSubstituted(scope, text, fallbackStart, fallbackEnd, encoded, into))
                    {
                        return false;
                    }
                }
                at = close + 1;
            }
            return true;
        }

        // Appends the computed value of the custom property name on element: its own declaration, else the
        // nearest ancestor's, with the declaration's var() substituted on the element declaring it.
        private static bool TryAppendVariable(VisualElement element, string name, StringBuilder into)
        {
            for (var scope = element; scope != null;)
            {
                if (!TryOwn(scope, name, out var raw) && !TryStylesheet(scope, name, out raw))
                {
                    scope = scope.hierarchy.parent;
                    continue;
                }
                var value = raw?.Trim();
                if (value == null || value == "initial")
                {
                    return false;
                }
                if (value is "inherit" or "unset")
                {
                    scope = scope.hierarchy.parent;
                    continue;
                }
                if (IndexOfVar(value, 0, value.Length) == value.Length)
                {
                    AppendText(value, 0, value.Length, encoded: false, into);
                    return true;
                }
                foreach (var resolving in s_resolving)
                {
                    if (ReferenceEquals(resolving.Element, scope) && resolving.Name == name)
                    {
                        return false;
                    }
                }
                s_resolving.Add((scope, name));
                var resolved = TryAppendSubstituted(scope, value, 0, value.Length, encoded: false, into);
                s_resolving.RemoveAt(s_resolving.Count - 1);
                return resolved;
            }
            return false;
        }

        private static bool TryOwn(VisualElement element, string name, out string? value)
        {
            value = null;
            if (!s_declared.TryGetValue(element, out var declared) || !declared.TryGetValue(name, out var layers))
            {
                return false;
            }
            value = layers.Values[layers.Count - 1];
            return true;
        }

        private static int IndexOfVar(string text, int from, int end)
        {
            var at = from >= text.Length ? -1 : text.IndexOf("var(", from, end - from, StringComparison.OrdinalIgnoreCase);
            return at < 0 ? end : at;
        }

        // The index of the parenthesis closing the one at open, or -1 when none does before end.
        private static int MatchingClose(string text, int open, int end)
        {
            var depth = 0;
            var at = open;
            for (; at < end; at++)
            {
                depth += text[at] == '(' ? 1 : text[at] == ')' ? -1 : 0;
                if (depth == 0)
                {
                    break;
                }
            }
            return at < end ? at : -1;
        }

        private static (int Start, int End) Trimmed(string text, int start, int end, bool encoded)
        {
            while (start < end && (text[start] == ' ' || (encoded && text[start] == '_')))
            {
                start++;
            }
            while (end > start && (text[end - 1] == ' ' || (encoded && text[end - 1] == '_')))
            {
                end--;
            }
            return (start, end);
        }

        // CSS text is written into a class bracket the way Tailwind reads one back: a space as `_`, an underscore
        // as `\_`.
        private static void AppendText(string text, int start, int end, bool encoded, StringBuilder into)
        {
            for (var i = start; i < end; i++)
            {
                var c = text[i];
                if (encoded)
                {
                    into.Append(c);
                }
                else if (c == '_')
                {
                    into.Append('\\').Append('_');
                }
                else
                {
                    into.Append(char.IsWhiteSpace(c) ? '_' : c);
                }
            }
        }
    }
}
