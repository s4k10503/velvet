using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Velvet.StyleTable
{
    /// <summary>What a single simple selector means to the utility-property table.</summary>
    internal enum UssSelectorKind
    {
        /// <summary>A shape the table cannot model. Reported, never silently dropped.</summary>
        Unsupported,

        /// <summary>A class selector, optionally gated on a pseudo-class or a state marker class.</summary>
        UtilityClass,

        /// <summary>
        /// A <c>:root</c> block. Excluded from the table: it is keyed on no class, and it declares custom
        /// properties, which are values other declarations read through <c>var()</c> rather than properties an
        /// element holds.
        /// </summary>
        RootBlock,

        /// <summary>
        /// A selector keyed on an element type. Excluded from the table: adding or removing a class never
        /// changes whether such a rule matches, so it sits outside what a class projection can affect.
        /// </summary>
        TypeKeyed,
    }

    /// <summary>
    /// The extra condition a class-keyed rule carries beyond the class being present.
    /// </summary>
    /// <remarks>
    /// A gated rule sits in its own cascade layer: its selector is one simple selector longer than a bare
    /// utility's, so it outranks every plain single-class rule and only applies while the gate holds. Two
    /// classes therefore contend for a property only when they share a gate, which is why the gate travels
    /// with the property set instead of being flattened away.
    /// </remarks>
    internal enum UssGate
    {
        None,
        Hover,
        Active,
        Inactive,
        Focus,
        Disabled,
        Enabled,
        Checked,

        /// <summary>Gated on the <c>is-selected</c> marker class being present alongside the utility.</summary>
        Selected,
    }

    /// <summary>One simple selector, classified.</summary>
    internal readonly struct UssSelectorTarget
    {
        private UssSelectorTarget(UssSelectorKind kind, string className, UssGate gate)
        {
            Kind = kind;
            ClassName = className;
            Gate = gate;
        }

        public UssSelectorKind Kind { get; }

        public string ClassName { get; }

        public UssGate Gate { get; }

        public static UssSelectorTarget Unsupported() =>
            new UssSelectorTarget(UssSelectorKind.Unsupported, string.Empty, UssGate.None);

        public static UssSelectorTarget Utility(string className, UssGate gate) =>
            new UssSelectorTarget(UssSelectorKind.UtilityClass, className, gate);

        public static UssSelectorTarget Root() =>
            new UssSelectorTarget(UssSelectorKind.RootBlock, string.Empty, UssGate.None);

        public static UssSelectorTarget TypeKeyed() =>
            new UssSelectorTarget(UssSelectorKind.TypeKeyed, string.Empty, UssGate.None);
    }

    /// <summary>Classifies USS selectors into the shapes the utility-property table knows how to model.</summary>
    internal static class UssSelector
    {
        // Unity's pseudo-class set is fixed by PseudoStates plus the two inverse forms; a name outside it is a
        // typo or a CSS-ism USS does not implement, and either way the rule it gates would never fire.
        private static readonly Dictionary<string, UssGate> PseudoClasses =
            new Dictionary<string, UssGate>(StringComparer.Ordinal)
            {
                ["hover"] = UssGate.Hover,
                ["active"] = UssGate.Active,
                ["inactive"] = UssGate.Inactive,
                ["focus"] = UssGate.Focus,
                ["disabled"] = UssGate.Disabled,
                ["enabled"] = UssGate.Enabled,
                ["checked"] = UssGate.Checked,
            };

        // A compound `.a.b` is modelled only when `b` is a state marker: a marker carries no rules of its own,
        // so `a` is unambiguously the utility whose property set the rule describes. Two utilities compounded
        // would have no such answer, so that shape is reported instead.
        private static readonly Dictionary<string, UssGate> StateMarkerClasses =
            new Dictionary<string, UssGate>(StringComparer.Ordinal)
            {
                ["is-selected"] = UssGate.Selected,
            };

        /// <summary>
        /// Splits a selector list and classifies each part. A comma-separated list is defined as the block
        /// repeated once per selector, so every part independently receives the rule's declarations.
        /// </summary>
        public static ImmutableArray<UssSelectorTarget> Classify(string selector)
        {
            var targets = ImmutableArray.CreateBuilder<UssSelectorTarget>();
            foreach (var part in selector.Split(','))
            {
                targets.Add(ClassifySimple(Normalize(part)));
            }
            return targets.ToImmutable();
        }

        /// <summary>Collapses the interior whitespace a multi-line selector list carries.</summary>
        private static string Normalize(string part)
        {
            var trimmed = part.Trim();
            var builder = new System.Text.StringBuilder(trimmed.Length);
            var lastWasSpace = false;
            foreach (var c in trimmed)
            {
                if (char.IsWhiteSpace(c))
                {
                    lastWasSpace = true;
                    continue;
                }
                if (lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }
                lastWasSpace = false;
                builder.Append(c);
            }
            return builder.ToString();
        }

        private static UssSelectorTarget ClassifySimple(string selector)
        {
            if (selector.Length == 0)
            {
                return UssSelectorTarget.Unsupported();
            }
            if (string.Equals(selector, ":root", StringComparison.Ordinal))
            {
                return UssSelectorTarget.Root();
            }
            if (selector[0] == '.')
            {
                return ClassifyClassSelector(selector);
            }
            return IsTypeKeyed(selector) ? UssSelectorTarget.TypeKeyed() : UssSelectorTarget.Unsupported();
        }

        private static UssSelectorTarget ClassifyClassSelector(string selector)
        {
            var pseudo = selector.IndexOf(':');
            if (pseudo >= 0)
            {
                var gated = ParseClassName(selector.Substring(1, pseudo - 1));
                var pseudoName = selector.Substring(pseudo + 1);
                if (gated == null || !PseudoClasses.TryGetValue(pseudoName, out var pseudoGate))
                {
                    return UssSelectorTarget.Unsupported();
                }
                return UssSelectorTarget.Utility(gated, pseudoGate);
            }

            var second = selector.IndexOf('.', 1);
            if (second >= 0)
            {
                var utility = ParseClassName(selector.Substring(1, second - 1));
                var marker = ParseClassName(selector.Substring(second + 1));
                if (utility == null || marker == null || !StateMarkerClasses.TryGetValue(marker, out var classGate))
                {
                    return UssSelectorTarget.Unsupported();
                }
                return UssSelectorTarget.Utility(utility, classGate);
            }

            var plain = ParseClassName(selector.Substring(1));
            return plain == null ? UssSelectorTarget.Unsupported() : UssSelectorTarget.Utility(plain, UssGate.None);
        }

        // Unity's USS class grammar is `\.[\w-]+`, so a name outside that character set is not a class the
        // importer would match either.
        private static string? ParseClassName(string candidate)
        {
            if (candidate.Length == 0)
            {
                return null;
            }
            foreach (var c in candidate)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                {
                    return null;
                }
            }
            return candidate;
        }

        private static bool IsTypeKeyed(string selector)
        {
            var pseudo = selector.IndexOf(':');
            var typeName = pseudo < 0 ? selector : selector.Substring(0, pseudo);
            if (pseudo >= 0 && !PseudoClasses.ContainsKey(selector.Substring(pseudo + 1)))
            {
                return false;
            }
            if (typeName.Length == 0 || !char.IsLetter(typeName[0]))
            {
                return false;
            }
            foreach (var c in typeName)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
            }
            return true;
        }
    }
}
