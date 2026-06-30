using System;

namespace Velvet
{
    /// <summary>
    /// The descendant condition a <c>has-[...]</c> variant tests for. Mirrors the CSS
    /// <c>:has()</c> relational pseudo-class subset Velvet supports: <c>has-[:checked]:</c> (any
    /// descendant control is checked), <c>has-[:focus]:</c> (a descendant holds focus — focus-within),
    /// and <c>has-[.class]:</c> (a descendant carries the given USS class).
    /// </summary>
    internal enum StyleHasKind
    {
        Checked, // has-[:checked]: — any descendant INotifyValueChanged<bool> is on
        Focus,   // has-[:focus]:   — a descendant has focus (focus-within)
        Class,   // has-[.foo]:     — a descendant carries the class "foo"
    }

    /// <summary>
    /// Parses the <c>has-[...]</c> variant (a parent styled by a DESCENDANT condition). The form is
    /// <c>has-[&lt;inner&gt;]:&lt;payload&gt;</c> where <c>&lt;inner&gt;</c> is <c>:checked</c>, <c>:focus</c>,
    /// or a class selector <c>.foo</c>. Mirrors <see cref="StyleStructuralVariantClass"/>'s bracket parse: the
    /// inner selector's own <c>:</c> / <c>.</c> live inside the brackets, so the variant separator is the
    /// <c>:</c> that immediately follows the <c>]</c>.
    /// <para/>
    /// USS class selectors cannot encode a relational condition, so these tokens never enter the class list;
    /// the reconciler routes them to a <see cref="StyleHasVariantManipulator"/> (the <c>:checked</c> /
    /// <c>:focus</c> forms, driven by bubbling descendant events) and to a side-table re-scanned by the
    /// container's post-children pass (the <c>.class</c> form, re-evaluated when the element's own children
    /// reconcile). On top of those per-element passes, after each settled flush the reconciler re-derives the
    /// has- elements that flush could have affected (<see cref="FiberWorkLoop"/> drives
    /// FiberNodePatcher.RefreshHasVariants): it walks UP the ancestor chain from the flushed region's root (plus
    /// each active Portal target) and re-derives the registered has- elements found there — the only ones whose
    /// match a flush confined to that region can change. So a has- condition stays reactive to an INDEPENDENT
    /// nested re-render that never re-renders the has- element itself — a child component's own state toggling a
    /// descendant's class, or applying a controlled <c>:checked</c> value (which fires no event the manipulator
    /// could catch). The reconciler is the one mutating the descendant, so it drives the re-derivation; no
    /// UI-Toolkit descendant-mutation signal is needed.
    /// <para/>
    /// Limitations: <c>:hover</c> is intentionally NOT supported — a reliable descendant-hover signal would
    /// need per-frame pointer-bounds hit-testing of the whole subtree, which is fragile, so
    /// <c>has-[:hover]:</c> simply does not parse. Items virtualized by <c>V.VirtualList</c> mount their rows on
    /// the controller's own path, so a has- element inside a virtualized row follows that path's timing rather
    /// than the enclosing post-children pass, mirroring the structural variants' VirtualList exclusion. A has-
    /// payload that is itself a structural or has- variant has no gating owner and is dropped; a payload that is
    /// a plain state variant (<c>has-[:checked]:hover:…</c>) composes via the stacked-variant path, like the
    /// other manipulators.
    /// </summary>
    internal static class StyleHasVariantClass
    {
        private const string Prefix = "has-[";

        /// <summary>True if <paramref name="token"/> is a recognized <c>has-[...]</c> variant token.</summary>
        public static bool IsHas(string token) => TryParse(token, out _, out _, out _);

        /// <summary>
        /// Splits a <c>has-[...]</c> token into its kind, the target class name (only for
        /// <see cref="StyleHasKind.Class"/>; null otherwise), and the payload. Returns false for any
        /// non-<c>has-</c> token, an empty selector, an unrecognized inner selector, or an empty payload.
        /// </summary>
        public static bool TryParse(string token, out StyleHasKind kind, out string className, out string payload)
        {
            kind = default;
            className = null;
            payload = null;
            if (string.IsNullOrEmpty(token) || !token.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            if (!StyleBracketVariant.TrySplitBracket(token, Prefix.Length, out var selector, out payload))
            {
                return false;
            }

            switch (selector)
            {
                case ":checked":
                    kind = StyleHasKind.Checked;
                    return true;
                case ":focus":
                    kind = StyleHasKind.Focus;
                    return true;
            }

            // A class selector ".foo" — the only other supported inner form. ":hover" and any other pseudo
            // fall through to false (unsupported), so the token is not claimed and stays inert.
            if (selector[0] == '.' && selector.Length > 1)
            {
                kind = StyleHasKind.Class;
                className = selector.Substring(1);
                return true;
            }

            payload = null;
            return false;
        }
    }
}
