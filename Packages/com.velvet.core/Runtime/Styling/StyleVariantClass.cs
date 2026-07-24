#nullable enable
namespace Velvet
{
    /// <summary>
    /// State-variant kinds for utility classes — the <c>hover:</c> / <c>focus:</c> /
    /// <c>active:</c> prefixes.
    /// </summary>
    /// <remarks>
    /// <c>disabled:</c> is intentionally absent: UI Toolkit has no reliable "enabled changed" event to
    /// drive a manipulator, so disabled-state styling stays on the USS <c>:disabled</c> pseudo-class
    /// (the curated <c>disabled-*</c> utilities). Responsive (<c>sm:</c>/<c>md:</c>), <c>dark:</c>, and
    /// <c>group</c>/<c>peer</c> are tracked separately (they need a breakpoint / theme / structural
    /// signal source).
    /// </remarks>
    public enum StyleVariantKind
    {
        // Element-local state variants (driven by pointer/focus events).
        Hover,
        Focus,
        FocusVisible,
        Active,

        // Element-local checked state (driven by the target's ChangeEvent<bool>, e.g. a Toggle).
        Checked,

        // Responsive min-width variants (driven by the panel root width).
        Sm,
        Md,
        Lg,
        Xl,
        Xxl,

        // Ambient theme variant (driven by VelvetTheme.IsDark).
        Dark,

        // Relational variants: parent marked `group` (group-*) / previous sibling marked `peer` (peer-*).
        GroupHover,
        GroupFocus,
        GroupFocusWithin,
        GroupActive,
        PeerHover,
        PeerFocus,
        PeerFocusWithin,
        PeerActive,
        PeerChecked,
    }

    /// <summary>
    /// Parses a state-variant utility token of the form <c>&lt;variant&gt;:&lt;payload&gt;</c>
    /// (e.g. <c>hover:bg-blue-500</c>, <c>focus:border-accent</c>, <c>active:w-[200px]</c>).
    /// <para/>
    /// USS class selectors cannot contain <c>:</c>, so these tokens are never added to the class list;
    /// the reconciler routes them to a <see cref="StyleVariantManipulator"/> that toggles the payload
    /// when the matching pointer/focus state is active. The payload itself is an ordinary utility — a
    /// USS class (<c>bg-blue-500</c>) or an arbitrary value (<c>w-[200px]</c>).
    /// </summary>
    public static class StyleVariantClass
    {
        /// <summary>Returns true if <paramref name="token"/> is a recognized state-variant token.</summary>
        public static bool IsVariant(string? token) => TryParse(token, out _, out _, out _);

        /// <summary>
        /// Splits <paramref name="token"/> into its variant kind and payload. Returns false for a null/empty
        /// token, an unknown variant prefix, an empty payload, or when the first <c>:</c> belongs to an
        /// arbitrary value (i.e. occurs inside <c>[...]</c>, as in <c>bg-[addr:key]</c>). A named relational
        /// token (<c>group-hover/sidebar:</c>) parses to its kind with the name discarded; use the
        /// <see cref="TryParse(string, out StyleVariantKind, out string, out string)"/> overload to recover it.
        /// </summary>
        public static bool TryParse(string? token, out StyleVariantKind kind, out string? payload)
            => TryParse(token, out kind, out _, out payload);

        /// <summary>
        /// Splits <paramref name="token"/> into its variant kind, optional relational NAME, and payload.
        /// <paramref name="name"/> is the part after a <c>/</c> in the variant prefix, used only by the named
        /// <c>group/&lt;name&gt;</c> · <c>peer/&lt;name&gt;</c> named group/peer forms — e.g.
        /// <c>group-hover/sidebar:bg-on</c> yields <c>(GroupHover, "sidebar", "bg-on")</c>. It is null for the
        /// unnamed forms and for every non-relational variant. A <c>/</c> in the variant prefix is rejected
        /// (returns false) when the kind is not relational or when the name is empty (<c>group-hover/:</c>).
        /// The payload's own <c>/</c> (the opacity modifier <c>bg-black/50</c>) is untouched — it lives after
        /// the <c>:</c>, not in the prefix.
        /// <para/>
        /// Internal (not part of the public surface): the public 2-arg <see cref="TryParse(string, out StyleVariantKind, out string)"/>
        /// stays the supported entry point, matching the other (internal) variant parsers; the reconciler reads
        /// the name through this overload.
        /// </summary>
        internal static bool TryParse(string? token, out StyleVariantKind kind, out string? name, out string? payload)
        {
            kind = default;
            name = null;
            payload = null;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            var colon = token.IndexOf(':');
            if (colon <= 0 || colon == token.Length - 1)
            {
                return false;
            }

            // If a '[' precedes the ':', the colon is part of an arbitrary value (e.g. bg-[addr:key]),
            // not a variant separator.
            var bracket = token.IndexOf('[');
            if (bracket >= 0 && bracket < colon)
            {
                return false;
            }

            // The variant prefix may carry a relational name after a '/': group-hover/sidebar. Split it off
            // before matching the keyword. Only group-*/peer- accept a name; anything else with a '/' here is
            // not a valid token.
            var prefix = token.Substring(0, colon);
            var slash = prefix.IndexOf('/');
            if (slash >= 0)
            {
                name = prefix.Substring(slash + 1);
                prefix = prefix.Substring(0, slash);
                if (name.Length == 0)
                {
                    name = null;
                    return false;
                }
            }

            switch (prefix)
            {
                case "hover": kind = StyleVariantKind.Hover; break;
                case "focus": kind = StyleVariantKind.Focus; break;
                case "focus-visible": kind = StyleVariantKind.FocusVisible; break;
                case "active": kind = StyleVariantKind.Active; break;
                case "checked": kind = StyleVariantKind.Checked; break;
                case "sm": kind = StyleVariantKind.Sm; break;
                case "md": kind = StyleVariantKind.Md; break;
                case "lg": kind = StyleVariantKind.Lg; break;
                case "xl": kind = StyleVariantKind.Xl; break;
                case "2xl": kind = StyleVariantKind.Xxl; break;
                case "dark": kind = StyleVariantKind.Dark; break;
                case "group-hover": kind = StyleVariantKind.GroupHover; break;
                case "group-focus": kind = StyleVariantKind.GroupFocus; break;
                case "group-focus-within": kind = StyleVariantKind.GroupFocusWithin; break;
                case "group-active": kind = StyleVariantKind.GroupActive; break;
                case "peer-hover": kind = StyleVariantKind.PeerHover; break;
                case "peer-focus": kind = StyleVariantKind.PeerFocus; break;
                case "peer-focus-within": kind = StyleVariantKind.PeerFocusWithin; break;
                case "peer-active": kind = StyleVariantKind.PeerActive; break;
                case "peer-checked": kind = StyleVariantKind.PeerChecked; break;
                default: name = null; return false;
            }

            // A name is only meaningful for the relational kinds; reject it on any other variant.
            if (name != null && !IsRelational(kind))
            {
                name = null;
                return false;
            }

            payload = token.Substring(colon + 1);
            if (payload.Length == 0)
            {
                name = null;
                return false;
            }
            return true;
        }

        /// <summary>The per-source relational state a kind drives (hover / focus / focus-within / active /
        /// checked), shared by the group and peer families.</summary>
        internal enum RelationalState { Hover, Focus, FocusWithin, Active, Checked }

        /// <summary>True for the relational variant kinds (group-* / peer-*), the only ones that accept a name.</summary>
        internal static bool IsRelational(StyleVariantKind kind)
            => kind is StyleVariantKind.GroupHover or StyleVariantKind.GroupFocus
                or StyleVariantKind.GroupFocusWithin or StyleVariantKind.GroupActive
                or StyleVariantKind.PeerHover or StyleVariantKind.PeerFocus
                or StyleVariantKind.PeerFocusWithin or StyleVariantKind.PeerActive
                or StyleVariantKind.PeerChecked;

        /// <summary>True when a relational kind is a peer-* (previous-sibling source); false for group-*.</summary>
        internal static bool RelationalIsPeer(StyleVariantKind kind)
            => kind is StyleVariantKind.PeerHover or StyleVariantKind.PeerFocus
                or StyleVariantKind.PeerFocusWithin or StyleVariantKind.PeerActive
                or StyleVariantKind.PeerChecked;

        /// <summary>Maps a relational kind to the source state it reacts to.</summary>
        internal static RelationalState RelationalStateOf(StyleVariantKind kind) => kind switch
        {
            StyleVariantKind.GroupHover or StyleVariantKind.PeerHover => RelationalState.Hover,
            StyleVariantKind.GroupFocus or StyleVariantKind.PeerFocus => RelationalState.Focus,
            StyleVariantKind.GroupFocusWithin or StyleVariantKind.PeerFocusWithin => RelationalState.FocusWithin,
            StyleVariantKind.GroupActive or StyleVariantKind.PeerActive => RelationalState.Active,
            _ => RelationalState.Checked, // PeerChecked
        };

        /// <summary>True for the responsive min-width variants (<c>sm:</c>…<c>2xl:</c>).</summary>
        public static bool IsResponsive(StyleVariantKind kind)
            => kind is StyleVariantKind.Sm or StyleVariantKind.Md or StyleVariantKind.Lg
                or StyleVariantKind.Xl or StyleVariantKind.Xxl;

        /// <summary>
        /// Min-width (px) at which a responsive variant activates. The default breakpoints:
        /// sm 640, md 768, lg 1024, xl 1280, 2xl 1536. Returns 0 for non-responsive kinds.
        /// </summary>
        public static float BreakpointPx(StyleVariantKind kind) => kind switch
        {
            StyleVariantKind.Sm => 640f,
            StyleVariantKind.Md => 768f,
            StyleVariantKind.Lg => 1024f,
            StyleVariantKind.Xl => 1280f,
            StyleVariantKind.Xxl => 1536f,
            _ => 0f,
        };
    }
}
