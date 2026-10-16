using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// which is what the importer flattens the sheet to. The preflight sheet is a base layer and is not a
    /// utility, so it is left out.
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

        [Fact]
        public void Given_TheBundledStyleSheets_When_TheirCascadeOrderIsRead_Then_ThePreflightSheetComesBeforeEverySheetOfRules()
        {
            // Arrange — the preflight rules are a base layer a utility must outrank, so the guard above leaves
            // the sheet out by name as the table builder does; this is what keeps that from hiding a reset
            // that moved after the utilities and would win over them.
            var withRules = UssCascadeOrder.SheetsIn(Path.Combine(SolutionPaths.RuntimeRoot(), "Styles"))
                .Where(source => !UssStyleSheetParser.Parse(source.Path, source.Text).Rules.IsEmpty)
                .Select(source => Path.GetFileName(source.Path))
                .ToList();
            Assume.NotEmpty(withRules, "the bundled stylesheets declare rules");

            // Act
            var first = withRules[0];

            // Assert
            Assert.Equal(StyleUtilityTableBuilder.PreflightSheetName, first);
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

            // StyleRuleOrder sorts `.grid` as the display utility it stands for, and Tailwind emits flex ahead
            // of grid, so the sheet declares `.flex` ahead of the `.grid` whose set contains it.
            new OrderExemption(
                "flex precedes grid, the order Tailwind emits them in",
                broad: name => name == "grid",
                narrow: name => name == "flex"),
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
            foreach (var source in UssCascadeOrder.SheetsIn(
                Path.Combine(SolutionPaths.RuntimeRoot(), "Styles")))
            {
                if (Path.GetFileName(source.Path) == StyleUtilityTableBuilder.PreflightSheetName)
                {
                    // A base layer declared ahead of every utility to lose to them, so a reset may precede a
                    // utility writing the same property.
                    continue;
                }
                var sheet = UssStyleSheetParser.Parse(source.Path, source.Text);
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
