using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// The <c>drop-shadow-*</c> utilities as Tailwind v4 writes them: which <c>drop-shadow()</c> functions each
    /// token, or pair of size and colour tokens, composes into the inline filter.
    /// </summary>
    internal sealed class DropShadowFilterTests
    {
        private const string Control = "drop-shadow-[0_1px_2px_black]";

        private static string Composed(params (string Token, long Priority)[] layers)
        {
            var element = new VisualElement();
            foreach (var (token, priority) in layers)
            {
                if (!StyleArbitraryValueResolver.TryParse(token, out var style))
                {
                    return $"{token} unparsed";
                }
                StyleArbitraryValueResolver.Apply(element, in style, priority);
            }
            var filter = element.style.filter;
            return filter.keyword != StyleKeyword.Undefined || filter.value == null
                ? "none"
                : string.Join(" ", filter.value.Select(Describe));
        }

        private static string Composed(params string[] tokens)
            => Composed(tokens.Select(token => (token, StyleLayerPriority.Base)).ToArray());

        private static string Describe(FilterFunction function)
        {
            if (function.type != FilterFunctionType.Custom || function.customDefinition == null
                || function.customDefinition.filterName != "velvet-drop-shadow")
            {
                return function.type.ToString().ToLowerInvariant();
            }
            var color = function.GetParameter(3).colorValue;
            return string.Format(CultureInfo.InvariantCulture, "drop-shadow({0:0.##} {1:0.##} {2:0.##} {3:0.###},{4:0.###},{5:0.###},{6:0.###})",
                function.GetParameter(0).floatValue, function.GetParameter(1).floatValue, function.GetParameter(2).floatValue,
                color.r, color.g, color.b, color.a);
        }

        [Test]
        public void Given_ASizeToken_When_Applied_Then_ItsThemeShadowComposes()
        {
            // Arrange
            const string token = "drop-shadow-md";

            // Act
            var composed = Composed(token);

            // Assert
            Assert.That(composed, Is.EqualTo("drop-shadow(0 3 3 0,0,0,0.12)"));
        }

        [Test]
        public void Given_TheBareToken_When_Applied_Then_BothDeprecatedThemeShadowsCompose()
        {
            // Arrange
            const string token = "drop-shadow";

            // Act
            var composed = Composed(token);

            // Assert
            Assert.That(composed, Is.EqualTo("drop-shadow(0 1 2 0,0,0,0.1) drop-shadow(0 1 1 0,0,0,0.06)"));
        }

        [Test]
        public void Given_TheBareTokenWithAModifier_When_Applied_Then_ItKeepsItsThemeAlpha()
        {
            // Arrange — Tailwind's bare rule writes the theme value itself, whatever the modifier says.
            const string token = "drop-shadow/50";

            // Act
            var composed = Composed(token);

            // Assert
            Assert.That(composed, Is.EqualTo("drop-shadow(0 1 2 0,0,0,0.1) drop-shadow(0 1 1 0,0,0,0.06)"));
        }

        [Test]
        public void Given_AnArbitraryShadowList_When_Applied_Then_EachShadowComposesInOrder()
        {
            // Arrange
            const string token = "drop-shadow-[0_35px_35px_rgba(0,0,0,0.25),-2px_0_1rem_#ff0000]";

            // Act
            var composed = Composed(token);

            // Assert
            Assert.That(composed, Is.EqualTo("drop-shadow(0 35 35 0,0,0,0.25) drop-shadow(-2 0 16 1,0,0,1)"));
        }

        // Each is paired with a spelling that resolves, so a grammar that resolves nothing fails here too.
        [TestCase("drop-shadow-[0_1px_2px]", TestName = "Given_AShadowWithNoColour_When_Parsed_Then_OnlyTheColouredSpellingResolves")]
        [TestCase("drop-shadow-[0_1px_2px_3px_black]", TestName = "Given_AShadowWithASpread_When_Parsed_Then_OnlyTheThreeLengthSpellingResolves")]
        [TestCase("drop-shadow-[0_1px_-2px_black]", TestName = "Given_ANegativeDeviation_When_Parsed_Then_OnlyTheNonNegativeSpellingResolves")]
        [TestCase("drop-shadow-[0_10%_2px_black]", TestName = "Given_APercentOffset_When_Parsed_Then_OnlyThePixelSpellingResolves")]
        [TestCase("drop-shadow-[0_1px_2px_black_white]", TestName = "Given_TwoColours_When_Parsed_Then_OnlyTheOneColourSpellingResolves")]
        [TestCase("drop-shadow-none/50", TestName = "Given_NoneWithAModifier_When_Parsed_Then_OnlyTheSizeSpellingResolves")]
        public void Given_AMalformedDropShadow_When_Parsed_Then_OnlyTheWellFormedSpellingResolves(string token)
        {
            // Arrange
            var malformed = token;

            // Act
            var parsed = (StyleArbitraryValueResolver.TryParse(malformed, out _), StyleArbitraryValueResolver.TryParse(Control, out _));

            // Assert
            Assert.That(parsed, Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ASizeAndAColour_When_Applied_Then_TheColourAndItsModifierReplaceTheThemeColour()
        {
            // Arrange
            var tokens = new[] { "drop-shadow-md", "drop-shadow-[#ff0000]/50" };

            // Act
            var composed = Composed(tokens);

            // Assert
            Assert.That(composed, Is.EqualTo("drop-shadow(0 3 3 1,0,0,0.5)"));
        }

        [Test]
        public void Given_APaletteColour_When_AppliedBesideASize_Then_ThePaletteValueColoursIt()
        {
            // Arrange
            var tokens = new[] { "drop-shadow-sm", "drop-shadow-white" };

            // Act
            var composed = Composed(tokens);

            // Assert
            Assert.That(composed, Is.EqualTo("drop-shadow(0 1 2 1,1,1,1)"));
        }

        [Test]
        public void Given_ASizeOutrankingAColour_When_Applied_Then_TheSizeKeepsItsThemeColour()
        {
            // Arrange — Tailwind's size rule writes its own colours into --tw-drop-shadow, and the hover rule wins.
            var layers = new[] { ("drop-shadow-[#ff0000]", StyleLayerPriority.Base), ("drop-shadow-md", StyleLayerPriority.Hover) };

            // Act
            var composed = Composed(layers);

            // Assert
            Assert.That(composed, Is.EqualTo("drop-shadow(0 3 3 0,0,0,0.12)"));
        }

        [Test]
        public void Given_AnArbitraryShadowOutrankingAColour_When_Applied_Then_TheColourStillReachesIt()
        {
            // Arrange — an arbitrary shadow's colour reads the colour variable whichever rule wins.
            var layers = new[] { ("drop-shadow-[#ff0000]", StyleLayerPriority.Base), ("drop-shadow-[0_2px_4px_#0000ff]", StyleLayerPriority.Hover) };

            // Act
            var composed = Composed(layers);

            // Assert
            Assert.That(composed, Is.EqualTo("drop-shadow(0 2 4 1,0,0,1)"));
        }

        [Test]
        public void Given_ASizeWithAModifier_When_Applied_Then_TheModifierReplacesTheThemeAlpha()
        {
            // Arrange
            const string token = "drop-shadow-lg/50";

            // Act
            var composed = Composed(token);

            // Assert
            Assert.That(composed, Is.EqualTo("drop-shadow(0 4 4 0,0,0,0.5)"));
        }

        [Test]
        public void Given_ASizeModifierAndAColour_When_Applied_Then_TheModifierScalesTheColour()
        {
            // Arrange
            var tokens = new[] { "drop-shadow-xl/50", "drop-shadow-[#ff000080]" };

            // Act
            var composed = Composed(tokens);

            // Assert
            Assert.That(composed, Is.EqualTo("drop-shadow(0 9 7 1,0,0,0.251)"));
        }

        [Test]
        public void Given_AModifierOnALowerSize_When_AHigherSizeHasNone_Then_TheLowerModifierStillScalesTheColour()
        {
            // Arrange — only a size with a modifier writes --tw-drop-shadow-alpha, so the hover size leaves it.
            var layers = new[]
            {
                ("drop-shadow-md/50", StyleLayerPriority.Base),
                ("drop-shadow-[#ff0000]", StyleLayerPriority.Base),
                ("drop-shadow-[0_2px_4px_#000000]", StyleLayerPriority.Hover),
            };

            // Act
            var composed = Composed(layers);

            // Assert
            Assert.That(composed, Is.EqualTo("drop-shadow(0 2 4 1,0,0,0.5)"));
        }

        [Test]
        public void Given_DropShadowBesideAnotherFilter_When_Applied_Then_ItComposesLast()
        {
            // Arrange
            var tokens = new[] { "drop-shadow-xs", "sepia" };

            // Act
            var composed = Composed(tokens);

            // Assert
            Assert.That(composed, Is.EqualTo("sepia drop-shadow(0 1 1 0,0,0,0.05)"));
        }

        [Test]
        public void Given_NoneOutrankingASize_When_Applied_Then_NoShadowComposes()
        {
            // Arrange
            var layers = new[] { ("drop-shadow-md", StyleLayerPriority.Base), ("drop-shadow-none", StyleLayerPriority.Hover) };

            // Act
            var composed = Composed(layers);

            // Assert
            Assert.That(composed, Is.EqualTo("none"));
        }

        [Test]
        public void Given_AColourAlone_When_Applied_Then_NoShadowComposes()
        {
            // Arrange — Tailwind's colour rule sets variables and no filter.
            const string token = "drop-shadow-[#ff0000]";

            // Act
            var composed = Composed(token);

            // Assert
            Assert.That(composed, Is.EqualTo("none"));
        }

        [Test]
        public void Given_ADropShadowAddedAtTheEnd_When_TheTweenIsHalfway_Then_ItFadesFromCssInitialValueForInterpolation()
        {
            // Arrange — CSS pads a drop-shadow from every length 0 and a transparent colour.
            var from = new VisualElement();
            var to = new VisualElement();
            StyleArbitraryValueResolver.TryParse("drop-shadow-md", out var style);
            StyleArbitraryValueResolver.Apply(to, in style);
            var target = to.style.filter.value;
            StyleFilterTransitionDriver.TryBuildChannels(null, target, out var channels);
            var binding = new StyleFilterTransitionBinding { Channels = channels, Easing = EasingMode.Linear, Target = target };

            // Act
            StyleFilterTransitionDriver.ApplyFrame(from, binding, 0.5f);

            // Assert
            var filter = from.style.filter;
            Assert.That(filter.value == null ? "none" : string.Join(" ", filter.value.Select(Describe)),
                Is.EqualTo("drop-shadow(0 1.5 1.5 0,0,0,0.06)"));
        }

        // GREEN_ON_BASE(characterization): the base keys every class it cannot parse by its spelling, so two
        // drop-shadow sizes are already keyed apart; this keeps them apart once both parse to one property.
        [Test]
        public void Given_TwoDropShadowSizes_When_Keyed_Then_TheirKeysDiffer()
        {
            // Arrange
            const string medium = "drop-shadow-md";

            // Act
            var keys = (FiberNodePatcher.ValueKey(medium), FiberNodePatcher.ValueKey("drop-shadow-lg"));

            // Assert
            Assert.That(keys.Item1, Is.Not.EqualTo(keys.Item2));
        }
    }
}
