using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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

        // BuildQuery(ISearchParams) groups the values of one key; a pair list keeps the order it was given.
        internal static string BuildQuery(IReadOnlyList<KeyValuePair<string, string>> pairs)
        {
            if (pairs.Count == 0)
            {
                return string.Empty;
            }

            var parts = new List<string>(pairs.Count);
            foreach (var pair in pairs)
            {
                parts.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
            }
            return "?" + string.Join("&", parts);
        }
    }
}
