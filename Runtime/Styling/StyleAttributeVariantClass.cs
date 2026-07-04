#nullable enable
using System;

namespace Velvet
{
    /// <summary>
    /// Which attribute namespace an attribute variant tests. Mirrors the two HTML attribute
    /// families targeted with <c>data-[...]</c> and <c>aria-[...]</c>. UI Toolkit has no HTML
    /// attributes, so the element carries the values explicitly via
    /// <see cref="FiberElementProps.Data"/> / <see cref="FiberElementProps.Aria"/>, applied into the
    /// reconciler's per-element attribute side-table; the variant matches against that store.
    /// </summary>
    internal enum StyleAttributeNamespace
    {
        Data, // data-[key=value]: / data-[key]: — matches the element's Data attribute map
        Aria, // aria-[key=value]: / aria-[key]: — matches the element's Aria attribute map
    }

    /// <summary>
    /// Parses the attribute variants <c>data-[key=value]:</c> / <c>data-[key]:</c> and
    /// <c>aria-[key=value]:</c> / <c>aria-[key]:</c>. The bracket holds either a bare key (presence test) or
    /// a <c>key=value</c> pair (equality test); the value is the literal characters between <c>=</c> and
    /// <c>]</c> (no quoting). Mirrors <see cref="StyleHasVariantClass"/>'s bracket parse: the variant
    /// separator is the <c>:</c> that immediately follows the <c>]</c>.
    /// <para/>
    /// HTML attributes do not exist in UI Toolkit, so these tokens never enter the USS class list; the
    /// reconciler routes them to a per-element side-table re-evaluated against the element's carried
    /// attribute values (set via the <c>Data</c> / <c>Aria</c> props), like the <c>has-[.class]:</c> form.
    /// <para/>
    /// Limitations: only the presence and exact-equality matchers are supported — the substring / prefix /
    /// suffix operators (<c>*=</c>, <c>^=</c>, <c>$=</c>) and the general bracketed attribute selector
    /// <c>[aria-checked=true]</c> are intentionally out of scope. The payload must be a plain utility — a
    /// nested variant (<c>data-[..]:hover:..</c>, <c>data-[..]:first:..</c>) has no gating owner on the
    /// side-table path (the table is re-evaluated as a whole, not by a per-payload manipulator) and is
    /// dropped, mirroring the <c>has-[.class]:</c> side-table. The attribute store comes only from the
    /// <c>Data</c> / <c>Aria</c> props; there
    /// is no UI-Toolkit attribute-changed event, so reactivity is driven by the props patch path (a changed
    /// <c>Data</c> / <c>Aria</c> re-derives the payload) and by the class-change config pass — mirroring the
    /// <c>checked:</c> manipulator's documented inability to observe a purely-programmatic change with no
    /// signal. A <c>data-</c> / <c>aria-</c> variant on a <c>V.VirtualList</c> container is not evaluated
    /// (its items mount on the controller's own path, which does not build the attribute store), mirroring
    /// the structural / has-class variants' VirtualList exclusion.
    /// </summary>
    internal static class StyleAttributeVariantClass
    {
        private const string DataPrefix = "data-[";
        private const string AriaPrefix = "aria-[";

        /// <summary>True if <paramref name="token"/> is a recognized attribute variant token.</summary>
        public static bool IsAttribute(string? token) => TryParse(token, out _, out _, out _, out _);

        /// <summary>
        /// Splits an attribute variant token into its namespace, the attribute key, the expected value
        /// (null for the bare-key presence form), and the payload. Returns false for any non-attribute token,
        /// an empty key, or an empty payload.
        /// </summary>
        public static bool TryParse(
            string? token, out StyleAttributeNamespace ns, out string? key, out string? value, out string? payload)
        {
            ns = default;
            key = null;
            value = null;
            payload = null;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            int prefixLength;
            if (token.StartsWith(DataPrefix, StringComparison.Ordinal))
            {
                ns = StyleAttributeNamespace.Data;
                prefixLength = DataPrefix.Length;
            }
            else if (token.StartsWith(AriaPrefix, StringComparison.Ordinal))
            {
                ns = StyleAttributeNamespace.Aria;
                prefixLength = AriaPrefix.Length;
            }
            else
            {
                return false;
            }

            if (!StyleBracketVariant.TrySplitBracket(token, prefixLength, out var inner, out payload))
            {
                payload = null;
                return false;
            }

            // key=value (equality) vs bare key (presence). Only the first '=' splits, so a value may itself
            // contain '=' verbatim. An empty key (e.g. "=open") is rejected; an empty value ("state=") is a
            // valid equality test against the empty string.
            var eq = inner.IndexOf('=');
            if (eq < 0)
            {
                key = inner;
                return true;
            }
            if (eq == 0)
            {
                payload = null;
                return false;
            }

            key = inner.Substring(0, eq);
            value = inner.Substring(eq + 1);
            return true;
        }

        /// <summary>
        /// Whether a parsed rule matches the attribute value resolved from the element's store.
        /// <paramref name="present"/> is whether the key exists; <paramref name="actual"/> its value. A
        /// presence rule (<paramref name="expected"/> null) matches on existence; an equality rule matches
        /// when the key exists and its value equals <paramref name="expected"/> (ordinal).
        /// <para/>
        /// A present attribute with no stored value (<paramref name="actual"/> null) resolves to the empty
        /// string, mirroring HTML's valueless / boolean-attribute semantics where <c>data-state</c> carries
        /// the value <c>""</c>. So the empty-value equality rule <c>data-[state=]:</c> (<paramref name="expected"/>
        /// <c>""</c>) matches a present key whether its stored value is <c>""</c> or null — the two ways the
        /// <c>Data</c> / <c>Aria</c> props express a valueless attribute. For any non-empty
        /// <paramref name="expected"/> the <c>?? string.Empty</c> changes nothing (a null value already fails
        /// the exact match), so this only resolves the empty-value edge.
        /// </summary>
        public static bool Matches(string? expected, bool present, string? actual)
            => expected == null ? present : present && string.Equals(actual ?? string.Empty, expected, StringComparison.Ordinal);
    }
}
