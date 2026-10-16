using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The bundled stylesheets declare their utilities in the order Tailwind emits them, so of two that write one
    /// property the one Tailwind emits later wins, whichever is written first in a class list. The animation
    /// presets are left out: the scheduler applies them to outrank the base utilities they animate.
    /// </summary>
    [TestFixture]
    internal sealed class StyleSheetTailwindOrderTests
    {
        private const string AnimationPresetPrefix = "anim-";

        private const int ReportedPairs = 10;

        private static (string Name, StyleUtilityRule Rule)[] BundledUtilitiesInCascadeOrder()
        {
            var table = (IDictionary)typeof(StyleUtilityProperties)
                .GetField("ByClassName", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            return table.Keys.Cast<string>()
                .Where(name => !name.StartsWith(AnimationPresetPrefix, StringComparison.Ordinal))
                .Select(name =>
                {
                    StyleUtilityProperties.TryGet(name, out var rule);
                    return (name, rule);
                })
                .Where(utility => utility.rule.Gate == StyleUtilityGate.None)
                .OrderBy(utility => utility.rule.CascadePosition)
                .ToArray();
        }

        private static string Summary(List<string> misordered)
        {
            var distinct = misordered.Distinct().ToList();
            return distinct.Count == 0
                ? string.Empty
                : distinct.Count + " pair(s), the first " + ReportedPairs + ": "
                    + string.Join("; ", distinct.Take(ReportedPairs));
        }

        [Test]
        public void Given_TheBundledUtilitiesThatShareALonghand_When_TakenInCascadeOrder_Then_TailwindEmitsEachAfterThePreviousOne()
        {
            // Arrange
            var utilities = BundledUtilitiesInCascadeOrder();
            var ordinals = StyleRuleOrder.OrdinalsOf(utilities.Select(utility => utility.Name).ToArray());
            var misordered = new List<string>();

            // Act
            foreach (StyleLonghand longhand in Enum.GetValues(typeof(StyleLonghand)))
            {
                var previous = -1;
                for (var i = 0; i < utilities.Length; i++)
                {
                    if (!utilities[i].Rule.Properties.Contains(longhand))
                    {
                        continue;
                    }
                    if (previous >= 0 && ordinals[i] < ordinals[previous])
                    {
                        misordered.Add(utilities[previous].Name + " is declared before " + utilities[i].Name);
                    }
                    previous = i;
                }
            }

            // Assert
            Assert.That(Summary(misordered), Is.Empty);
        }

        [Test]
        public void Given_FlexGridAndHidden_When_Ordered_Then_TheyRunInTailwindsDisplayOrder()
        {
            // Arrange
            var classNames = new[] { "hidden", "grid", "flex" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(classNames);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 2, 1, 0 }));
        }

        [Test]
        public void Given_TextSmAndTextLg_When_TheirSizesAreRanked_Then_TheOneTailwindEmitsLaterRanksHigher()
        {
            // Act
            StyleNamedTextSize.TryParseLineHeight("text-sm", out _, out var small);
            StyleNamedTextSize.TryParseLineHeight("text-lg", out _, out var large);

            // Assert
            Assert.That(small, Is.GreaterThan(large));
        }

        [TestCase("truncate whitespace-pre")]
        [TestCase("whitespace-pre truncate")]
        public void Given_TruncateBesideAWhiteSpaceClass_When_ATextBalanceLeafIsReconciled_Then_TheWhiteSpaceClassGivesTheCollapse(
            string className)
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = V.Div(className: className, V.Label(className: "text-balance", text: "a  b"));

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new[] { tree });
            var label = scope.Root.Q<Label>();

            // Assert
            Assert.That((label.style.whiteSpace.keyword, label.style.whiteSpace.value),
                Is.EqualTo((StyleKeyword.Undefined, WhiteSpace.PreWrap)));
        }
    }
}
