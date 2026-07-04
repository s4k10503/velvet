#nullable enable
using System;

namespace Velvet
{
    /// <summary>
    /// Parses the feature-query variant <c>supports-[&lt;property&gt;:&lt;value&gt;]:</c> (the
    /// <c>@supports (property: value)</c> wrapper). The bracket holds a CSS-style declaration whose own
    /// <c>:</c> separates property from value, so — like <see cref="StyleHasVariantClass"/> and
    /// <see cref="StyleAttributeVariantClass"/> — the variant separator is the <c>:</c> that immediately
    /// follows the <c>]</c>.
    /// <para/>
    /// STATIC IN UI TOOLKIT. A feature query asks "does the engine support this declaration?". In a browser
    /// that answer varies across engines, so the variant gates at runtime. Velvet targets a single, fixed
    /// engine (Unity UI Toolkit on a pinned editor), so there is no runtime feature variation: the supported
    /// property set is constant for a given build. There is no cheap programmatic supported-property set to
    /// test against, and by construction the author only writes <c>supports-[prop:val]</c> for a declaration
    /// they intend to use, so a well-formed token is treated as ALWAYS-APPLIED (a malformed one never
    /// applies). The reconciler therefore evaluates this once at config time, with no reactive signal —
    /// unlike the event-driven / position / attribute variants there is nothing to re-derive.
    /// <para/>
    /// USS class selectors cannot encode a feature query, so these tokens never enter the class list; the
    /// reconciler routes them to a per-element side-table that records the applied payload only so a later
    /// class-list change can clear it.
    /// <para/>
    /// Limitations: the property/value pair is validated for WELL-FORMEDNESS only (non-empty property,
    /// non-empty value) — it is NOT checked against an actual UITK property whitelist, so an
    /// <c>@supports</c> negation or an unsupported-on-purpose query cannot be expressed; both forms simply
    /// apply. The payload must be a plain utility — a nested variant (<c>supports-[..]:hover:..</c>) has no
    /// gating owner on the side-table path and is dropped, mirroring the <c>has-[.class]:</c> /
    /// <c>data-[..]:</c> side-tables.
    /// </summary>
    internal static class StyleSupportsVariantClass
    {
        private const string Prefix = "supports-[";

        /// <summary>True if <paramref name="token"/> is a recognized <c>supports-[...]</c> variant token.</summary>
        public static bool IsSupports(string? token) => TryParse(token, out _, out _, out _);

        /// <summary>
        /// Splits a <c>supports-[&lt;property&gt;:&lt;value&gt;]:</c> token into its declared property, value,
        /// and payload. Returns false for any non-<c>supports-</c> token, a bracket missing the
        /// <c>property:value</c> colon, an empty property/value, or an empty payload.
        /// </summary>
        public static bool TryParse(string? token, out string? property, out string? value, out string? payload)
        {
            property = null;
            value = null;
            payload = null;
            if (string.IsNullOrEmpty(token) || !token.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            if (!StyleBracketVariant.TrySplitBracket(token, Prefix.Length, out var declaration, out payload))
            {
                payload = null;
                return false;
            }

            // A feature query is always a property:value declaration; the first ':' splits them so a value
            // may itself contain ':' verbatim. A missing ':' or an empty property/value is malformed.
            var colon = declaration.IndexOf(':');
            if (colon <= 0 || colon == declaration.Length - 1)
            {
                payload = null;
                return false;
            }

            property = declaration.Substring(0, colon);
            value = declaration.Substring(colon + 1);
            return true;
        }
    }
}
