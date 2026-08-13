#nullable enable
using System.Collections.Generic;

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
    /// <para/>
    /// Every switch that CLASSIFIES these kinds — which signal source drives one, which layer it occupies —
    /// is written without a discard arm, so adding a member here raises CS8509 at each site that has to learn
    /// it. A set of independent <c>is X or Y</c> predicates cannot report a member matching none, and that is
    /// what left <c>checked:</c>, the two focus-within relationals and <c>peer-checked:</c> unclassified and
    /// inert as the inner of a stacked variant. Those sites suppress CS8524 instead: it asks for an arm
    /// covering an out-of-range cast, which no named member can supply.
    /// <para/>
    /// That signal is load-bearing, so <c>Runtime/csc.rsp</c> compiles CS8509 as an error: a member added
    /// without an arm at one of these sites fails this assembly's build rather than warning into a log
    /// nothing gates on. Answering that error with a discard arm would put the silence back, so
    /// <c>ExhaustiveSwitchSeverityTests</c> holds both halves at once.
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

        // The variant keywords. A relational name is split off before the lookup, so no key carries a '/'.
        private static readonly Dictionary<string, StyleVariantKind> s_kinds = new()
        {
            ["hover"] = StyleVariantKind.Hover,
            ["focus"] = StyleVariantKind.Focus,
            ["focus-visible"] = StyleVariantKind.FocusVisible,
            ["active"] = StyleVariantKind.Active,
            ["checked"] = StyleVariantKind.Checked,
            ["sm"] = StyleVariantKind.Sm,
            ["md"] = StyleVariantKind.Md,
            ["lg"] = StyleVariantKind.Lg,
            ["xl"] = StyleVariantKind.Xl,
            ["2xl"] = StyleVariantKind.Xxl,
            ["dark"] = StyleVariantKind.Dark,
            ["group-hover"] = StyleVariantKind.GroupHover,
            ["group-focus"] = StyleVariantKind.GroupFocus,
            ["group-focus-within"] = StyleVariantKind.GroupFocusWithin,
            ["group-active"] = StyleVariantKind.GroupActive,
            ["peer-hover"] = StyleVariantKind.PeerHover,
            ["peer-focus"] = StyleVariantKind.PeerFocus,
            ["peer-focus-within"] = StyleVariantKind.PeerFocusWithin,
            ["peer-active"] = StyleVariantKind.PeerActive,
            ["peer-checked"] = StyleVariantKind.PeerChecked,
        };

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

            if (!s_kinds.TryGetValue(prefix, out kind))
            {
                name = null;
                return false;
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

        /// <summary>
        /// The length a per-state array is allocated at. Derived from the enum rather than written down:
        /// a discard-less switch raises CS8509 for a member added here, but nothing about a switch can size
        /// an array, and a literal length instead overflows in silence. Callers index such an array with
        /// <c>(int)</c> of a <see cref="RelationalState"/>, so the length and the index cannot disagree.
        /// </summary>
        internal static readonly int RelationalStateCount = System.Enum.GetValues(typeof(RelationalState)).Length;

        /// <summary>
        /// The state a detected source signal belongs to. The two enumerations name the same relational
        /// states; this pairing is what says so, rather than a cast that would silently survive either one
        /// being reordered. No discard arm — see the remarks on <see cref="StyleVariantKind"/>.
        /// </summary>
#pragma warning disable CS8524 // no discard arm: an unpaired signal has to warn
        internal static RelationalState StateOf(RelationalVariantSignal signal) => signal switch
        {
            RelationalVariantSignal.Hover => RelationalState.Hover,
            RelationalVariantSignal.Focus => RelationalState.Focus,
            RelationalVariantSignal.FocusWithin => RelationalState.FocusWithin,
            RelationalVariantSignal.Active => RelationalState.Active,
            RelationalVariantSignal.Checked => RelationalState.Checked,
        };
#pragma warning restore CS8524

        /// <summary>
        /// Which source a relational kind reads (a preceding <c>peer</c> sibling when <c>IsPeer</c>, else the
        /// nearest <c>group</c> ancestor) and which of that source's states it reacts to; null for every
        /// non-relational kind. One switch answers all three questions so they cannot answer differently, and
        /// it carries no discard arm — see the remarks on <see cref="StyleVariantKind"/>.
        /// </summary>
#pragma warning disable CS8524 // no discard arm — see the remarks on StyleVariantKind
        internal static (bool IsPeer, RelationalState State)? RelationalOf(StyleVariantKind kind) => kind switch
        {
            StyleVariantKind.GroupHover => (false, RelationalState.Hover),
            StyleVariantKind.GroupFocus => (false, RelationalState.Focus),
            StyleVariantKind.GroupFocusWithin => (false, RelationalState.FocusWithin),
            StyleVariantKind.GroupActive => (false, RelationalState.Active),
            StyleVariantKind.PeerHover => (true, RelationalState.Hover),
            StyleVariantKind.PeerFocus => (true, RelationalState.Focus),
            StyleVariantKind.PeerFocusWithin => (true, RelationalState.FocusWithin),
            StyleVariantKind.PeerActive => (true, RelationalState.Active),
            StyleVariantKind.PeerChecked => (true, RelationalState.Checked),
            StyleVariantKind.Hover or StyleVariantKind.Focus or StyleVariantKind.FocusVisible
                or StyleVariantKind.Active or StyleVariantKind.Checked
                or StyleVariantKind.Sm or StyleVariantKind.Md or StyleVariantKind.Lg
                or StyleVariantKind.Xl or StyleVariantKind.Xxl
                or StyleVariantKind.Dark => null,
        };
#pragma warning restore CS8524

        /// <summary>True for the relational variant kinds (group-* / peer-*), the only ones that accept a name.</summary>
        internal static bool IsRelational(StyleVariantKind kind) => RelationalOf(kind).HasValue;

        /// <summary>
        /// True for the responsive min-width variants (<c>sm:</c>…<c>2xl:</c>) — read off
        /// <see cref="BreakpointPx"/> rather than listing them again, so the two cannot disagree.
        /// A value that names no kind throws, for the reason given there.
        /// </summary>
        public static bool IsResponsive(StyleVariantKind kind) => BreakpointPx(kind) > 0f;

        /// <summary>
        /// Min-width (px) at which a responsive variant activates. The default breakpoints:
        /// sm 640, md 768, lg 1024, xl 1280, 2xl 1536. Returns 0 for every other named kind, and throws
        /// for a value that names no kind — a cast outside the enum's range is a caller error, and a
        /// silent 0 is how one survives to produce a wrong layout instead of a stack trace.
        /// </summary>
#pragma warning disable CS8524 // no discard arm — see the remarks on StyleVariantKind
        public static float BreakpointPx(StyleVariantKind kind) => kind switch
        {
            StyleVariantKind.Sm => 640f,
            StyleVariantKind.Md => 768f,
            StyleVariantKind.Lg => 1024f,
            StyleVariantKind.Xl => 1280f,
            StyleVariantKind.Xxl => 1536f,
            StyleVariantKind.Hover or StyleVariantKind.Focus or StyleVariantKind.FocusVisible
                or StyleVariantKind.Active or StyleVariantKind.Checked or StyleVariantKind.Dark
                or StyleVariantKind.GroupHover or StyleVariantKind.GroupFocus
                or StyleVariantKind.GroupFocusWithin or StyleVariantKind.GroupActive
                or StyleVariantKind.PeerHover or StyleVariantKind.PeerFocus
                or StyleVariantKind.PeerFocusWithin or StyleVariantKind.PeerActive
                or StyleVariantKind.PeerChecked => 0f,
        };
#pragma warning restore CS8524
    }
}
