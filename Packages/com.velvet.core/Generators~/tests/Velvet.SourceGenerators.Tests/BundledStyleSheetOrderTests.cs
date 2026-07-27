using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Velvet.StyleTable;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Enforces the declaration-order rule the bundled stylesheets depend on: when one utility's property
    /// set contains another's, the narrower utility must be declared later.
    /// </summary>
    /// <remarks>
    /// Cascade order is the @import order of StyleUtilities.uss followed by line order within each partial,
    /// which is what the importer flattens the sheet to.
    /// </remarks>
    public sealed class BundledStyleSheetOrderTests
    {
        [Fact]
        public void Given_TwoUtilitiesWhoseSetsNest_When_ComparedInCascadeOrder_Then_TheNarrowerIsDeclaredLater()
        {
            // Arrange
            var utilities = UtilitiesInCascadeOrder();
            Assume.NotEmpty(utilities, "the bundled stylesheets declare utilities with property sets");

            // Act
            var misordered = Misordered(utilities);

            // Assert
            Assert.Empty(misordered);
        }

        [Fact]
        public void Given_TheExemptFamilies_When_ComparedInCascadeOrder_Then_EachStillBreaksTheRuleItIsExemptedFor()
        {
            // Arrange — an exemption that stopped applying would silently widen the guard's blind spot.
            var utilities = UtilitiesInCascadeOrder();
            Assume.NotEmpty(utilities, "the bundled stylesheets declare utilities with property sets");

            // Act
            var exercised = Exemptions
                .Where(exemption => Misordered(utilities, skip: other => other != exemption).Count > 0)
                .Select(exemption => exemption.Reason)
                .ToList();

            // Assert
            Assert.Equal(Exemptions.Select(e => e.Reason).ToList(), exercised);
        }

        /// <summary>Pairs where satisfying the rule would cost more than it buys.</summary>
        private static readonly IReadOnlyList<OrderExemption> Exemptions = new[]
        {
            // StyleAnimationScheduler applies these rather than a caller composing them, and they are meant
            // to outrank the base utility they animate for the duration of a play.
            new OrderExemption(
                "anim-* presets are scheduler-applied and must outrank the base utilities they animate",
                broad: name => name.StartsWith("anim-", StringComparison.Ordinal),
                narrow: name => name.StartsWith("opacity-", StringComparison.Ordinal)
                    || name.StartsWith("scale-", StringComparison.Ordinal)
                    || name.StartsWith("transition-", StringComparison.Ordinal)
                    || name.StartsWith("anim-", StringComparison.Ordinal)),

            // The reference cascade emits overflow (layout) and whitespace (after textOverflow) on the same
            // side of truncate, so moving truncate earlier would break parity rather than restore it.
            new OrderExemption(
                "truncate sits where the reference cascade puts it, after the overflow and white-space utilities it contains",
                broad: name => name == "truncate",
                narrow: name => name.StartsWith("overflow-", StringComparison.Ordinal)
                    || name.StartsWith("whitespace-", StringComparison.Ordinal)
                    || name == "text-wrap"
                    || name == "text-nowrap"),

            // transition-none sits between transition-all and the property-specific utilities, so it precedes
            // only these four of the six that contain it. Moving it ahead of transition-all to match the
            // reference — which emits transitionProperty as none, all, DEFAULT, colors — would put it before
            // transition-transform and transition-filter too, adding a fifth violation rather than removing
            // four. The remaining deviation is that `transition-all transition-none` resolves to
            // transition-none here and to transition-all on the web.
            new OrderExemption(
                "transition-none precedes these four, and moving it earlier would misorder it against the rest",
                broad: name => name == "transition-opacity"
                    || name == "transition-colors"
                    || name == "transition-colors-scale"
                    || name == "transition-colors-scale-opacity",
                narrow: name => name == "transition-none"),
        };

        private static List<string> Misordered(
            IReadOnlyList<Utility> utilities, Func<OrderExemption, bool>? skip = null)
        {
            var active = Exemptions.Where(e => skip == null || skip(e)).ToList();
            var misordered = new List<string>();
            for (var broad = 0; broad < utilities.Count; broad++)
            {
                for (var narrow = 0; narrow < utilities.Count; narrow++)
                {
                    var b = utilities[broad];
                    var n = utilities[narrow];
                    if (b.Gate != n.Gate || !n.Properties.IsProperSubsetOf(b.Properties) || n.Order > b.Order)
                    {
                        continue;
                    }
                    if (active.Any(e => e.Covers(b.ClassName, n.ClassName)))
                    {
                        continue;
                    }
                    misordered.Add(
                        $"'{n.ClassName}' {{{string.Join(", ", n.Properties.OrderBy(x => x, StringComparer.Ordinal))}}} " +
                        $"is declared before '{b.ClassName}' " +
                        $"{{{string.Join(", ", b.Properties.OrderBy(x => x, StringComparer.Ordinal))}}}, " +
                        "so it can never win the properties they share");
                }
            }
            return misordered;
        }

        /// <summary>
        /// Every utility that writes at least one longhand, in the order the importer flattens the sheet to.
        /// </summary>
        private static IReadOnlyList<Utility> UtilitiesInCascadeOrder()
        {
            var utilities = new List<Utility>();
            var order = 0;
            foreach (var path in PartialsInImportOrder())
            {
                var sheet = UssStyleSheetParser.Parse(path, File.ReadAllText(path));
                foreach (var rule in sheet.Rules)
                {
                    var properties = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var declaration in rule.Declarations)
                    {
                        if (UssPropertyVocabulary.TryResolve(declaration.Property, out var longhands))
                        {
                            properties.UnionWith(longhands);
                        }
                    }
                    foreach (var target in UssSelector.Classify(rule.Selector))
                    {
                        order++;
                        if (target.Kind == UssSelectorKind.UtilityClass && properties.Count > 0)
                        {
                            utilities.Add(new Utility(target.ClassName, target.Gate, properties, order));
                        }
                    }
                }
            }
            return utilities;
        }

        /// <summary>
        /// The partials in the aggregator's @import order, which is the order the importer concatenates them
        /// in and therefore the order the cascade resolves ties by.
        /// </summary>
        private static IEnumerable<string> PartialsInImportOrder()
        {
            var styles = Path.Combine(SolutionPaths.RuntimeRoot(), "Styles");
            var aggregator = File.ReadAllText(Path.Combine(styles, "StyleUtilities.uss"));
            foreach (Match match in Regex.Matches(aggregator, @"@import\s+url\(""([^""]+)""\)"))
            {
                yield return Path.Combine(styles, match.Groups[1].Value);
            }
        }

        private sealed class Utility
        {
            public Utility(string className, UssGate gate, HashSet<string> properties, int order)
            {
                ClassName = className;
                Gate = gate;
                Properties = properties;
                Order = order;
            }

            public string ClassName { get; }

            public UssGate Gate { get; }

            public HashSet<string> Properties { get; }

            public int Order { get; }
        }

        private sealed class OrderExemption
        {
            private readonly Func<string, bool> _broad;
            private readonly Func<string, bool> _narrow;

            public OrderExemption(string reason, Func<string, bool> broad, Func<string, bool> narrow)
            {
                Reason = reason;
                _broad = broad;
                _narrow = narrow;
            }

            public string Reason { get; }

            public bool Covers(string broadClassName, string narrowClassName) =>
                _broad(broadClassName) && _narrow(narrowClassName);
        }
    }
}
