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
            if (string.IsNullOrEmpty(path))
            {
                return result;
            }

            var qIndex = path.IndexOf('?');
            if (qIndex < 0 || qIndex == path.Length - 1)
            {
                return result;
            }

            var query = path.Substring(qIndex + 1);
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0)
                {
                    result.Append(DecodeQueryComponent(pair), string.Empty);
                }
                else
                {
                    var key = DecodeQueryComponent(pair.Substring(0, eq));
                    var value = DecodeQueryComponent(pair.Substring(eq + 1));
                    result.Append(key, value);
                }
            }
            return result;
        }

        // A get submission's body as React Router's normalizeNavigateOptions encodes it: null is empty, an
        // ISearchParams is itself, a string parses as new URLSearchParams(string) does, and name/value pairs
        // (a dictionary, say) stand for a plain object. Anything else is a body it cannot encode.
        internal static bool TryEncodeBody(object? body, out ISearchParams query)
        {
            switch (body)
            {
                case null:
                    query = new SearchParams();
                    return true;
                case ISearchParams searchParams:
                    query = searchParams;
                    return true;
                case string text:
                    query = ParseQuery(text.StartsWith('?') ? text : "?" + text);
                    return true;
                case IEnumerable<KeyValuePair<string, string>> pairs:
                    var built = new SearchParams();
                    foreach (var pair in pairs)
                    {
                        built.Append(pair.Key, pair.Value ?? string.Empty);
                    }
                    query = built;
                    return true;
                default:
                    query = new SearchParams();
                    return false;
            }
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
    }
}
