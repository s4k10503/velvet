using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the families Tailwind composes through one declaration — translate, the per-axis scale, skew, and
    /// the filters — where a value the browser rejects holds its property at the rank its variants give it, and the
    /// family draws nothing while it does.
    /// </summary>
    internal sealed class RejectedCompositeTests
    {
        private static readonly Dictionary<string, long> s_ranks = new()
        {
            ["sm"] = StyleLayerPriority.ResponsiveSm,
            ["md"] = StyleLayerPriority.ResponsiveMd,
            ["dark"] = StyleLayerPriority.Dark,
            ["hover"] = StyleLayerPriority.Hover,
            ["focus"] = StyleLayerPriority.Focus,
        };

        // The translate, scale and filter the classes compose to with every variant they name in force: each token
        // is applied as the reconciler and the variant manipulators apply it, a base token at the base rank once
        // Normalize has ordered the base, and a variant token at its variants' stacked rank and its rule's place.
        private static string Composed(string classes)
        {
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));
            var element = new VisualElement();
            for (var i = 0; i < tokens.Length; i++)
            {
                var core = StyleArbitraryValueResolver.StripImportant(tokens[i], out var important);
                var variants = StyleSpacingPrecedence.VariantsLength(core);
                var rank = StyleLayerPriority.Base;
                foreach (var part in core.Substring(0, variants).Split(':', System.StringSplitOptions.RemoveEmptyEntries))
                {
                    rank = StyleLayerPriority.Stack(rank, s_ranks[part]);
                }
                rank = important ? StyleLayerPriority.ImportantOf(rank) : rank;
                var key = variants == 0 ? rank : StyleLayerPriority.WithRule(rank, StyleRuleOrder.OrdinalOf(tokens, i));
                if (StyleArbitraryValueResolver.TryParseLayer(core.Substring(variants), out var style))
                {
                    StyleArbitraryValueResolver.Apply(element, in style, key);
                }
            }
            var translate = element.style.translate;
            var scale = element.style.scale;
            return string.Join(" ",
                translate.keyword == StyleKeyword.None ? "none"
                : translate.keyword == StyleKeyword.Undefined ? Pair(translate.value.x.value, translate.value.y.value) : "-",
                scale.keyword == StyleKeyword.None ? "none"
                : scale.keyword == StyleKeyword.Undefined ? Pair(scale.value.value.x, scale.value.value.y) : "-",
                (element.style.filter.value?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
        }

        private static string Pair(float x, float y)
            => string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##}", x, y);

        [TestCase("translate-x-4 hover:translate-y-[10]", "none - 0")]
        [TestCase("!translate-y-[10] hover:translate-y-2 translate-x-4", "none - 0")]
        [TestCase("translate-y-[10] translate-y-2 translate-x-4", "none - 0")]
        [TestCase("translate-x-4 translate-[10]", "none - 0")]
        [TestCase("sm:translate-x-[10] md:translate-y-6", "none - 0")]
        [TestCase("translate-x-4 dark:hover:translate-y-[10] hover:translate-y-2", "none - 0")]
        [TestCase("scale-x-[2] scale-y-[2px]", "- none 0")]
        [TestCase("blur-sm brightness-[abc]", "- - 0")]
        [TestCase("drop-shadow-lg hover:blur-[2]", "- - 0")]
        public void Given_LayersWhereARejectedValueHoldsItsProperty_When_Composed_Then_TheFamilyDrawsNothing(
            string classes, string expected)
        {
            // Act
            var composed = Composed(classes);

            // Assert
            Assert.That(composed, Is.EqualTo(expected));
        }

        [TestCase("sm:translate-8 sm:translate-x-[10] focus:translate-x-2", "8,32 - 0")]
        [TestCase("translate-x-2 translate-y-[3] hover:translate-y-1 md:translate-4", "16,4 - 0")]
        [TestCase("translate-x-4 sm:translate-x-[10] md:translate-8 md:translate-y-6", "32,24 - 0")]
        [TestCase("-translate-px translate-y-full", "-1,100 - 0")]
        [TestCase("translate-1/2", "50,50 - 0")]
        public void Given_LayersWritingBothTranslateAxes_When_Composed_Then_EachAxisHoldsByRank(string classes, string expected)
        {
            // Act
            var composed = Composed(classes);

            // Assert
            Assert.That(composed, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base read no rejected value, and in these no rejected value holds.
        [TestCase("translate-x-0 sm:translate-x-[10] md:translate-x-4 hover:translate-y-1", "16,4 - 0")]
        [TestCase("sm:translate-x-[10] md:translate-y-6 dark:translate-x-2", "8,24 - 0")]
        [TestCase("hover:translate-y-6 sm:translate-x-[10] md:translate-x-4", "16,24 - 0")]
        [TestCase("translate-x-4 hover:translate-y-[10] focus:translate-y-2", "16,8 - 0")]
        [TestCase("translate-x-4 dark:translate-y-[10] hover:translate-y-2", "16,8 - 0")]
        [TestCase("translate-x-4 hover:translate-y-[10] focus:translate-y-[2px]", "16,2 - 0")]
        [TestCase("drop-shadow-lg drop-shadow-red-500 blur-[2] hover:blur-md", "- - 2")]
        [TestCase("blur-[2] blur-md", "- - 1")]
        [TestCase("blur-md blur-[2]", "- - 1")]
        public void Given_LayersWhereEveryHoldingValueIsTaken_When_Composed_Then_TheyCompose(string classes, string expected)
        {
            // Act
            var composed = Composed(classes);

            // Assert
            Assert.That(composed, Is.EqualTo(expected));
        }

        [Test]
        public void Given_AUniformScaleBelowAHoverAxisScale_When_Hovered_Then_TheOtherAxisKeepsTheUniformScale()
        {
            // Arrange — hover:bg-red-500 is a class payload, which is what builds the class projection that takes
            // scale-50 off the element once hover:scale-x-[2] outranks it.
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "box", className: "scale-50 hover:scale-x-[2] hover:bg-red-500"));
            var box = root.Q("box");

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                box.SimulateEvent(over);
            }
            var scale = box.style.scale.value.value;

            // Assert — NaN where the hover's own axis did not land.
            Assert.That(Mathf.Approximately(scale.x, 2f) ? scale.y : float.NaN, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Given_AHoverUniformScaleHeldBeforeABaseOne_When_AnImportantAxisTakesOneAxis_Then_TheOtherAxisKeepsTheHoverScale()
        {
            // Arrange — the base class arrives once the hover one has built the projection, so the projection holds
            // the hover entry first; scale-90 follows scale-50 in the stylesheet, so only the key keeps it below.
            var element = new VisualElement();
            StyleClassProjection.Add(element, "scale-50", StyleLayerPriority.Hover);
            StyleClassProjection.Add(element, "scale-90", StyleLayerPriority.Base);
            var axis = new ArbitraryStyle(ArbitraryProperty.ScaleX, 0.75f, LengthUnit.Pixel);

            // Act
            StyleArbitraryValueResolver.Apply(element, in axis, StyleLayerPriority.ImportantOf(StyleLayerPriority.Base));
            var scale = element.style.scale.value.value;

            // Assert — NaN where the important axis did not land.
            Assert.That(Mathf.Approximately(scale.x, 0.75f) ? scale.y : float.NaN, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewAxisTheBrowserRejects_When_Extracted_Then_NoSkewIsDrawn()
        {
            // Act
            var skewed = StyleSkewClass.TryExtract(new[] { "skew-x-6", "skew-y-[10]" }, out _);

            // Assert
            Assert.That(skewed, Is.False);
        }

        [Test]
        public void Given_ATranslationAndAHoverValueTheBrowserRejects_When_Hovered_Then_NothingIsTranslated()
        {
            // Arrange
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "box", className: "translate-x-4 hover:translate-y-[10]"));
            var box = root.Q("box");
            var before = box.style.translate.value.x.value;

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                box.SimulateEvent(over);
            }

            // Assert — NaN where the translation was not drawn before the hover either.
            Assert.That(before == 16f ? box.style.translate.value.x.value : float.NaN, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ADropShadowBesideARingColourTheBrowserRejects_When_Mounted_Then_TheDropShadowIsDrawn()
        {
            // Arrange
            const string classes = "ring ring-[5PX] drop-shadow-lg";
            var root = new VisualElement();
            var ringDiscarded = !StyleRingClass.TryExtract(classes.Split(' '), out _);

            // Act
            using var mounted = V.Mount(root, V.Div(name: "box", className: classes));

            // Assert — -1 where the ring was drawn, which leaves no discarded box-shadow to survive.
            Assert.That(ringDiscarded ? root.Q("box").style.filter.value?.Count ?? 0 : -1, Is.EqualTo(1));
        }

    }
}
