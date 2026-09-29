#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// State-variant kinds for utility classes — the <c>hover:</c> / <c>focus:</c> /
    /// <c>active:</c> / <c>disabled:</c> prefixes.
    /// </summary>
    /// <remarks>
    /// A classification answering for every kind — which signal source drives one, which layer it occupies — is
    /// a <see cref="VariantKindTable{T}"/> built from one entry per member, and <c>VariantKindTableTests</c>
    /// fails for a table missing a member. A set of independent <c>is X or Y</c> predicates cannot report a
    /// member matching none, and that is what left <c>checked:</c>, the two focus-within relationals and
    /// <c>peer-checked:</c> unclassified and inert as the inner of a stacked variant.
    /// <para/>
    /// The switches over the smaller enums beside this one are written without a discard arm instead, so
    /// adding a member raises CS8509 at each site that has to learn it. Those sites suppress CS8524: it asks
    /// for an arm covering an out-of-range cast, which no named member can supply. That signal is
    /// load-bearing, so <c>Runtime/csc.rsp</c> compiles CS8509 as an error, and answering that error with a
    /// discard arm would put the silence back, so <c>ExhaustiveSwitchSeverityTests</c> holds both halves at
    /// once. This enum cannot take the same form: a switch naming every member costs VEL501 one branching
    /// decision per member, and it has more members than that cap.
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

        // Element-local disabled state: the element or an ancestor has enabledSelf off, which is what UI
        // Toolkit's :disabled pseudo-class matches. Appended rather than grouped with the element-local
        // states above so the members already published keep their values.
        Disabled,

        // Relational disabled states: the group ancestor / peer sibling is disabled, as Disabled reads it.
        GroupDisabled,
        PeerDisabled,
    }

    /// <summary>
    /// A value for every <see cref="StyleVariantKind"/>, looked up by the kind's value — the form each
    /// classification of the kinds takes, for the reason the enum's remarks give.
    /// </summary>
    internal sealed class VariantKindTable<T>
    {
        private static readonly int KindCount = Enum.GetValues(typeof(StyleVariantKind)).Length;

        private readonly T[] _values = new T[KindCount];
        private readonly bool[] _covered = new bool[KindCount];

        public VariantKindTable(params (StyleVariantKind Kind, T Value)[] entries)
        {
            foreach (var (kind, value) in entries)
            {
                _values[(int)kind] = value;
                _covered[(int)kind] = true;
            }
        }

        public bool Covers(StyleVariantKind kind) => (uint)kind < (uint)KindCount && _covered[(int)kind];

        /// <summary>Throws for a value naming no kind and for a kind the table was built without.</summary>
        public T this[StyleVariantKind kind] =>
            Covers(kind) ? _values[(int)kind] : throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
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
            ["disabled"] = StyleVariantKind.Disabled,
            ["group-disabled"] = StyleVariantKind.GroupDisabled,
            ["peer-disabled"] = StyleVariantKind.PeerDisabled,
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
        internal enum RelationalState { Hover, Focus, FocusWithin, Active, Checked, Disabled }

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
            RelationalVariantSignal.Disabled => RelationalState.Disabled,
        };
#pragma warning restore CS8524

        /// <summary>
        /// Which source a relational kind reads (a preceding <c>peer</c> sibling when <c>IsPeer</c>, else the
        /// nearest <c>group</c> ancestor) and which of that source's states it reacts to; null for every
        /// non-relational kind. One table answers all three questions so they cannot answer differently.
        /// </summary>
        internal static (bool IsPeer, RelationalState State)? RelationalOf(StyleVariantKind kind) => s_relational[kind];

        private static readonly VariantKindTable<(bool IsPeer, RelationalState State)?> s_relational = new(
            (StyleVariantKind.GroupHover, (false, RelationalState.Hover)),
            (StyleVariantKind.GroupFocus, (false, RelationalState.Focus)),
            (StyleVariantKind.GroupFocusWithin, (false, RelationalState.FocusWithin)),
            (StyleVariantKind.GroupActive, (false, RelationalState.Active)),
            (StyleVariantKind.PeerHover, (true, RelationalState.Hover)),
            (StyleVariantKind.PeerFocus, (true, RelationalState.Focus)),
            (StyleVariantKind.PeerFocusWithin, (true, RelationalState.FocusWithin)),
            (StyleVariantKind.PeerActive, (true, RelationalState.Active)),
            (StyleVariantKind.PeerChecked, (true, RelationalState.Checked)),
            (StyleVariantKind.GroupDisabled, (false, RelationalState.Disabled)),
            (StyleVariantKind.PeerDisabled, (true, RelationalState.Disabled)),
            (StyleVariantKind.Hover, null),
            (StyleVariantKind.Focus, null),
            (StyleVariantKind.FocusVisible, null),
            (StyleVariantKind.Active, null),
            (StyleVariantKind.Checked, null),
            (StyleVariantKind.Disabled, null),
            (StyleVariantKind.Sm, null),
            (StyleVariantKind.Md, null),
            (StyleVariantKind.Lg, null),
            (StyleVariantKind.Xl, null),
            (StyleVariantKind.Xxl, null),
            (StyleVariantKind.Dark, null));

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
        public static float BreakpointPx(StyleVariantKind kind) => s_breakpointPx[kind];

        private static readonly VariantKindTable<float> s_breakpointPx = new(
            (StyleVariantKind.Sm, 640f),
            (StyleVariantKind.Md, 768f),
            (StyleVariantKind.Lg, 1024f),
            (StyleVariantKind.Xl, 1280f),
            (StyleVariantKind.Xxl, 1536f),
            (StyleVariantKind.Hover, 0f),
            (StyleVariantKind.Focus, 0f),
            (StyleVariantKind.FocusVisible, 0f),
            (StyleVariantKind.Active, 0f),
            (StyleVariantKind.Checked, 0f),
            (StyleVariantKind.Disabled, 0f),
            (StyleVariantKind.Dark, 0f),
            (StyleVariantKind.GroupHover, 0f),
            (StyleVariantKind.GroupFocus, 0f),
            (StyleVariantKind.GroupFocusWithin, 0f),
            (StyleVariantKind.GroupActive, 0f),
            (StyleVariantKind.PeerHover, 0f),
            (StyleVariantKind.PeerFocus, 0f),
            (StyleVariantKind.PeerFocusWithin, 0f),
            (StyleVariantKind.PeerActive, 0f),
            (StyleVariantKind.PeerChecked, 0f),
            (StyleVariantKind.GroupDisabled, 0f),
            (StyleVariantKind.PeerDisabled, 0f));
    }
}
