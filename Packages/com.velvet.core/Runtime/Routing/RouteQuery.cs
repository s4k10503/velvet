using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Velvet
{
    internal static class RouteQuery
    {
        internal static ISearchParams ParseQuery(string path)
        {
            var result = new SearchParams();
            foreach (var pair in ParsePairs(path))
            {
                result.Append(pair.Key, pair.Value);
            }
            return result;
        }

        // The pairs of the text after the first '?', in the order written.
        internal static List<KeyValuePair<string, string>> ParsePairs(string path)
        {
            var result = new List<KeyValuePair<string, string>>();
            var qIndex = string.IsNullOrEmpty(path) ? -1 : path.IndexOf('?');
            if (qIndex < 0)
            {
                return result;
            }

            var query = path.Substring(qIndex + 1);
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = DecodeQueryComponent(eq < 0 ? pair : pair.Substring(0, eq));
                var value = eq < 0 ? string.Empty : DecodeQueryComponent(pair.Substring(eq + 1));
                result.Add(new KeyValuePair<string, string>(key, value));
            }
            return result;
        }

        // Replace literal '+' before unescaping so "%2B" remains '+'.
        private static string DecodeQueryComponent(string component) =>
            Uri.UnescapeDataString(component.Replace('+', ' '));

        [return: NotNullIfNotNull(nameof(path))]
        internal static string? StripQuery(string? path)
        {
            if (path == null)
            {
                return null;
            }
            var qIndex = path.IndexOf('?');
            return qIndex < 0 ? path : path.Substring(0, qIndex);
        }

        internal static string BuildQuery(ISearchParams values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            var parts = new List<string>(values.Count);
            foreach (var key in values.Keys)
            {
                var escapedKey = Uri.EscapeDataString(key);
                foreach (var value in values.GetAll(key))
                {
                    parts.Add($"{escapedKey}={Uri.EscapeDataString(value ?? string.Empty)}");
                }
            }
            return "?" + string.Join("&", parts);
        }

        // A submission's query, as React Router writes `?${searchParams}`: the pairs in the order given, unlike
        // BuildQuery(ISearchParams), through the application/x-www-form-urlencoded serializer.
        internal static string BuildQuery(IReadOnlyList<KeyValuePair<string, string>> pairs)
            => pairs.Count == 0 ? string.Empty : "?" + FormUrlEncode(pairs);

        // URLSearchParams' toString: each name and value as UTF-8, a space as '+', the ASCII letters and digits
        // and '*', '-', '.', '_' as they are, and every other byte percent-escaped in upper case.
        internal static string FormUrlEncode(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            var text = new StringBuilder();
            var first = true;
            foreach (var pair in pairs)
            {
                if (!first)
                {
                    text.Append('&');
                }
                first = false;
                AppendFormComponent(text, pair.Key);
                text.Append('=');
                AppendFormComponent(text, pair.Value);
            }
            return text.ToString();
        }

        private static void AppendFormComponent(StringBuilder text, string component)
        {
            foreach (var b in Encoding.UTF8.GetBytes(component))
            {
                var c = (char)b;
                if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '*' or '-' or '.' or '_')
                {
                    text.Append(c);
                }
                else if (c == ' ')
                {
                    text.Append('+');
                }
                else
                {
                    text.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
        }
    }
}
