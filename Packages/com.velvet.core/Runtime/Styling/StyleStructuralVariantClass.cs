#nullable enable
using System;

namespace Velvet
{
    /// <summary>
    /// Position among siblings, for the structural variants. Mirrors the CSS child-position pseudo
    /// classes: <c>first:</c> / <c>last:</c> / <c>only:</c> / <c>odd:</c> / <c>even:</c> and the arbitrary
    /// selector form <c>[&amp;:nth-child(N)]:</c> / <c>[&amp;:nth-last-child(N)]:</c> (+ the named
    /// <c>[&amp;:first-child]</c> etc. aliases).
    /// </summary>
    internal enum StyleStructuralKind
    {
        First,
        Last,
        Only,
        Odd,
        Even,
        NthChild,      // 1-based: nth-child(N) → index == N-1
        NthLastChild,  // 1-based from the end: nth-last-child(N) → index == count-N
    }

    /// <summary>
    /// Parses and evaluates the structural (child-position) variants. Unlike the event-driven
    /// variants, these depend on the element's position among its siblings, so they are NOT owned by a
    /// per-element manipulator: the reconciler re-evaluates them from a container pass that runs after the
    /// container's children are reconciled (the same hook gap/divide use), so a child added / removed /
    /// reordered re-derives every sibling's position. Evaluation is stateless and idempotent.
    /// <para/>
    /// USS class selectors cannot encode position, so these tokens never enter the class list; the
    /// reconciler routes them to the structural pass via <see cref="ReconcilerContext"/>.
    /// <para/>
    /// Limitations: the payload must be a plain utility — composing with a nested variant
    /// (<c>first:hover:…</c>) is unsupported (the structural pass has no gating owner, so such a token is
    /// dropped). Items virtualized by <c>V.VirtualList</c> are not evaluated by the structural pass (the
    /// controller mounts rows on its own path), mirroring the gap manipulator's VirtualList exclusion.
    /// </summary>
    internal static class StyleStructuralVariantClass
    {
        private const string ArbitraryPrefix = "[&:";

        /// <summary>True if <paramref name="token"/> is a recognized structural variant token.</summary>
        public static bool IsStructural(string? token) => TryParse(token, out _, out _, out _);

        /// <summary>
        /// Splits a structural variant token into its kind (+ N for the nth forms) and payload. Returns
        /// false for any non-structural token. Accepts the named forms (<c>first:</c>…<c>even:</c>) and the
        /// arbitrary selector forms (<c>[&amp;:nth-child(3)]:</c>, <c>[&amp;:first-child]:</c>, …).
        /// </summary>
        public static bool TryParse(string? token, out StyleStructuralKind kind, out int n, out string? payload)
        {
            kind = default;
            n = 0;
            payload = null;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            if (token.StartsWith(ArbitraryPrefix, StringComparison.Ordinal))
            {
                return TryParseArbitrary(token, out kind, out n, out payload);
            }

            var colon = token.IndexOf(':');
            if (colon <= 0 || colon == token.Length - 1)
            {
                return false;
            }

            switch (token.Substring(0, colon))
            {
                case "first": kind = StyleStructuralKind.First; break;
                case "last": kind = StyleStructuralKind.Last; break;
                case "only": kind = StyleStructuralKind.Only; break;
                case "odd": kind = StyleStructuralKind.Odd; break;
                case "even": kind = StyleStructuralKind.Even; break;
                default: return false;
            }

            payload = token.Substring(colon + 1);
            return payload.Length > 0;
        }

        // Parses the arbitrary selector form [&:<selector>]:<payload>. The selector's own ':' / '(' live
        // inside the brackets, so the variant separator is the ':' that immediately follows the ']'.
        private static bool TryParseArbitrary(string? token, out StyleStructuralKind kind, out int n, out string? payload)
        {
            kind = default;
            n = 0;
            payload = null;

            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            // An empty selector ([&:]:…) is rejected by the shared split; it would match no case below anyway.
            if (!StyleBracketVariant.TrySplitBracket(token, ArbitraryPrefix.Length, out var selector, out payload))
            {
                return false;
            }

            switch (selector)
            {
                case "first-child": kind = StyleStructuralKind.First; return true;
                case "last-child": kind = StyleStructuralKind.Last; return true;
                case "only-child": kind = StyleStructuralKind.Only; return true;
                case "nth-child(odd)": kind = StyleStructuralKind.Odd; return true;
                case "nth-child(even)": kind = StyleStructuralKind.Even; return true;
            }

            if (TryParseNthArgument(selector, "nth-child(", out n))
            {
                kind = StyleStructuralKind.NthChild;
                return true;
            }
            if (TryParseNthArgument(selector, "nth-last-child(", out n))
            {
                kind = StyleStructuralKind.NthLastChild;
                return true;
            }

            payload = null;
            return false;
        }

        // Parses the positive integer N out of "<fn>N)" (e.g. "nth-child(3)" → 3). Only a bare 1-based
        // integer is supported; the general An+B microsyntax is intentionally out of scope.
        private static bool TryParseNthArgument(string? selector, string fn, out int n)
        {
            n = 0;
            if (string.IsNullOrEmpty(selector)
                || !selector.StartsWith(fn, StringComparison.Ordinal)
                || selector[selector.Length - 1] != ')')
            {
                return false;
            }

            var inner = selector.Substring(fn.Length, selector.Length - fn.Length - 1);
            return int.TryParse(inner, out n) && n >= 1;
        }

        /// <summary>
        /// Whether a structural rule matches an element at <paramref name="index"/> (0-based) among
        /// <paramref name="count"/> siblings.
        /// </summary>
        public static bool Matches(StyleStructuralKind kind, int n, int index, int count) => kind switch
        {
            StyleStructuralKind.First => index == 0,
            StyleStructuralKind.Last => index == count - 1,
            StyleStructuralKind.Only => count == 1,
            StyleStructuralKind.Odd => index % 2 == 0,          // 1st, 3rd, … (1-based odd)
            StyleStructuralKind.Even => index % 2 == 1,         // 2nd, 4th, …
            StyleStructuralKind.NthChild => n >= 1 && index == n - 1,
            StyleStructuralKind.NthLastChild => n >= 1 && index == count - n,
            _ => false,
        };
    }
}
