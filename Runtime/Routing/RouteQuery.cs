using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Velvet
{
    /// <summary>
    /// Pure query-string utilities shared by the routing hooks and the search-params setter: parsing a query
    /// into <see cref="ISearchParams"/>, stripping the query from a path, and building a query string back.
    /// These have no fiber or hook state.
    /// </summary>
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
                    // Repeated keys are preserved (getAll parity); Get returns the first value.
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

        // Decodes one application/x-www-form-urlencoded query component: a literal '+' denotes a space
        // and is converted before percent-escapes are resolved, so an encoded
        // plus ('%2B') round-trips back to '+' rather than collapsing to a space.
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
                // Emit one key=value pair per value so multi-value keys round-trip.
                foreach (var value in values.GetAll(key))
                {
                    parts.Add($"{escapedKey}={Uri.EscapeDataString(value ?? string.Empty)}");
                }
            }
            return "?" + string.Join("&", parts);
        }
    }
}
