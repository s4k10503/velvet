#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Velvet
{
    // new URL(href, "http://localhost").toString() for an href that is a path and a query, as memory history's
    // createURL makes React Router's request.url: the WHATWG URL parser's reading of a special scheme's path and
    // query, serialized.
    internal static class RouteUrl
    {
        private const string Origin = "http://localhost";

        private static readonly char[] TrailingTrimmed = BuildTrailingTrimmed();

        private static char[] BuildTrailingTrimmed()
        {
            var chars = new char[0x21];
            for (var index = 0; index < chars.Length; index++)
            {
                chars[index] = (char)index;
            }
            return chars;
        }

        internal static string Of(string href)
        {
            // The parser takes out every tab and newline, and the trailing C0 controls and spaces.
            var pathAndQuery = href.Replace("\t", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty)
                .TrimEnd(TrailingTrimmed);
            var queryAt = pathAndQuery.IndexOf('?');
            var path = queryAt < 0 ? pathAndQuery : pathAndQuery.Substring(0, queryAt);
            var text = new StringBuilder(Origin).Append(Path(path));
            if (queryAt >= 0)
            {
                text.Append('?');
                Encode(text, pathAndQuery.Substring(queryAt + 1), "\"#<>'");
            }
            return text.ToString();
        }

        // The path state: a backslash ends a segment as a slash does, a single-dot segment is dropped and a
        // double-dot one drops the segment before it, either leaving an empty last segment when it is the last.
        private static string Path(string path)
        {
            var segments = path.Replace('\\', '/').Split('/');
            var kept = new List<string>();
            for (var index = path.StartsWith("/") || path.StartsWith("\\") ? 1 : 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                var last = index == segments.Length - 1;
                if (IsDoubleDot(segment))
                {
                    if (kept.Count > 0)
                    {
                        kept.RemoveAt(kept.Count - 1);
                    }
                }
                else if (!IsSingleDot(segment))
                {
                    kept.Add(segment);
                    continue;
                }
                if (last)
                {
                    kept.Add(string.Empty);
                }
            }

            var text = new StringBuilder();
            foreach (var segment in kept)
            {
                text.Append('/');
                Encode(text, segment, "\"#<>?`{}");
            }
            return text.Length == 0 ? "/" : text.ToString();
        }

        private static bool IsSingleDot(string segment)
            => segment == "." || string.Equals(segment, "%2e", System.StringComparison.OrdinalIgnoreCase);

        private static bool IsDoubleDot(string segment)
        {
            var lower = segment.ToLowerInvariant();
            return lower is ".." or ".%2e" or "%2e." or "%2e%2e";
        }

        // UTF-8, with every byte outside printable ASCII and every byte of the set percent-encoded in upper case.
        private static void Encode(StringBuilder text, string part, string set)
        {
            foreach (var b in Encoding.UTF8.GetBytes(part))
            {
                if (b < 0x21 || b > 0x7E || set.IndexOf((char)b) >= 0)
                {
                    text.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
                else
                {
                    text.Append((char)b);
                }
            }
        }
    }
}
