using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which of a variant's classes a spring/bezier plan reads each slot from when several of them write
    /// it: the one the cascade lets hold the slot at rest, decided slot by slot.
    /// </summary>
    [TestFixture]
    internal sealed class MotionSlotCascadeTests
    {
        [Test]
        public void Given_TwoPresetOpacitiesInOneVariant_When_Resolved_Then_TheOneDeclaredLaterInTheStylesheetIsTheTarget()
        {
            // Arrange — .opacity-20 comes later in the class list, .opacity-50 later in the stylesheet.
            var to = new[] { "opacity-50", "opacity-20" };

            // Act
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, to);

            // Assert
            Assert.That(plan.Opacity?.to, Is.EqualTo(0.5f));
        }

        [Test]
        public void Given_ABracketOpacityBeforeAPresetOne_When_Resolved_Then_TheBracketValueIsTheTarget()
        {
            // Arrange — the bracket form resolves to inline style, which outranks the preset's stylesheet rule.
            var to = new[] { "opacity-[.3]", "opacity-50" };

            // Act
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, to);

            // Assert
            Assert.That(plan.Opacity?.to, Is.EqualTo(0.3f));
        }

        [Test]
        public void Given_AShorthandAndALonghandOnBothSides_When_Resolved_Then_EachEdgeAnimatesFromItsOwnHolder()
        {
            // Arrange — .pt-* is declared after .p-*, so it holds the top edge and the shorthand the other three.
            var from = new[] { "p-0", "pt-4" };
            var to = new[] { "p-8", "pt-2" };

            // Act
            var plan = MotionSpringClassParser.Resolve(from, to);

            // Assert
            Assert.That(Lengths(plan),
                Is.EqualTo("PaddingBottom:0->32 PaddingLeft:0->32 PaddingRight:0->32 PaddingTop:16->8"));
        }

        [Test]
        public void Given_AnUnreadableCornerAfterAReadableShorthand_When_Resolved_Then_ThatCornerIsNotAnimated()
        {
            // Arrange — .rounded-tl-full is declared after .rounded-3xl and holds the top-left corner at rest,
            // and no magnitude is read from it.
            var to = new[] { "rounded-3xl", "rounded-tl-full" };

            // Act
            var plan = MotionSpringClassParser.Resolve(new[] { "rounded-none" }, to);

            // Assert
            Assert.That(Lengths(plan), Is.EqualTo(
                "BorderBottomLeftRadius:0->24 BorderBottomRightRadius:0->24 BorderTopRightRadius:0->24"));
        }

        [Test]
        public void Given_AnImportantBracketWidthBeforeAPlainOne_When_Resolved_Then_TheImportantValueIsTheTarget()
        {
            // Arrange
            var to = new[] { "!w-[40px]", "w-[20px]" };

            // Act
            var plan = MotionSpringClassParser.Resolve(new[] { "w-[0px]" }, to);

            // Assert
            Assert.That(Lengths(plan), Is.EqualTo("Width:0->40"));
        }

        [Test]
        public void Given_APerAxisScaleAfterAUniformOne_When_Resolved_Then_NoScaleChannelIsPlanned()
        {
            // Arrange — the per-axis layer writes the x axis over the uniform one, a pair no single scale
            // magnitude describes.
            var to = new[] { "scale-[1.4]", "scale-x-[.5]" };

            // Act
            var plan = MotionSpringClassParser.Resolve(new[] { "scale-[2]" }, to);

            // Assert
            Assert.That(plan.Scale, Is.Null);
        }

        [Test]
        public void Given_APerAxisScaleBeforeAUniformOne_When_Resolved_Then_OnlyTheUniformControlHasAScaleChannel()
        {
            // Arrange — the resolver composes a per-axis layer over the uniform one whichever comes first.
            var to = new[] { "scale-x-[.5]", "scale-[1.4]" };

            // Act
            var control = MotionSpringClassParser.Resolve(new[] { "scale-[2]" },
                to.Where(cls => !cls.StartsWith("scale-x", System.StringComparison.Ordinal)).ToArray());
            var plan = MotionSpringClassParser.Resolve(new[] { "scale-[2]" }, to);

            // Assert
            Assert.That((control.Scale?.to, plan.Scale.HasValue), Is.EqualTo(((float?)1.4f, false)));
        }

        [TestCase("!p-8", "pt-[2px]")]
        [TestCase("pt-[2px]", "!p-8")]
        public void Given_AnImportantPresetShorthandAndAPlainInlineLonghand_When_Resolved_Then_TheImportantShorthandHoldsTheTop(string first, string second)
        {
            // Arrange
            var to = new[] { first, second };

            // Act
            var root = new VisualElement();
            var controlRoot = new VisualElement();
            using var control = V.Mount(controlRoot, V.Div(name: "control", className:
                string.Join(" ", to.Where(cls => !cls.StartsWith("!", System.StringComparison.Ordinal)))));
            using var mounted = V.Mount(root, V.Div(name: "leaf", className: string.Join(" ", to)));
            var plan = MotionSpringClassParser.Resolve(new[] { "pt-[0px]" }, to);

            // Assert
            Assert.That((Lengths(plan), root.Q<VisualElement>("leaf").style.paddingTop.keyword,
                controlRoot.Q<VisualElement>("control").style.paddingTop.value.value),
                Is.EqualTo(("PaddingTop:0->32", StyleKeyword.Null, 2f)));
        }

        [Test]
        public void Given_AnImportantInlineLonghandBeforeAPlainShorthand_When_Resolved_Then_TheLaterShorthandHoldsTheTop()
        {
            // Arrange
            var to = new[] { "!pt-[2px]", "p-[8px]" };
            var controlRoot = new VisualElement();

            // Act
            using var control = V.Mount(controlRoot, V.Div(name: "control", className: to[0] + " pt-[1px]"));
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Div(name: "leaf", className: string.Join(" ", to)));
            var plan = MotionSpringClassParser.Resolve(new[] { "pt-[0px]" }, to);

            // Assert
            Assert.That((Lengths(plan), root.Q<VisualElement>("leaf").style.paddingTop.value.value,
                controlRoot.Q<VisualElement>("control").style.paddingTop.value.value),
                Is.EqualTo(("PaddingTop:0->8", 8f, 2f)));
        }

        [Test]
        public void Given_APlainInlineLonghandBeforeAnImportantShorthand_When_Resolved_Then_TheImportantShorthandHoldsTheTop()
        {
            // Arrange
            var to = new[] { "pt-[2px]", "!p-[8px]" };

            // Act
            var plan = MotionSpringClassParser.Resolve(new[] { "pt-[0px]" }, to);

            // Assert
            Assert.That(Lengths(plan), Is.EqualTo("PaddingTop:0->8"));
        }

        // GREEN_ON_BASE(characterization): the overlap fallback already leaves this projection-dependent
        // shorthand/longhand pair undriven; the expanded slot reading must preserve that fallback.
        [TestCase(false, StyleKeyword.Undefined, 2f)]
        [TestCase(true, StyleKeyword.Null, 0f)]
        public void Given_AnImportantInlineShorthandBeforeAPlainLonghand_When_Resolved_Then_TheProjectionDependentTopIsNotDriven(bool withProjection, StyleKeyword keyword, float value)
        {
            // Arrange
            var to = new[] { "!p-[8px]", "pt-[2px]" };
            var root = new VisualElement();
            var className = string.Join(" ", to) + (withProjection ? " !opacity-50" : "");

            // Act
            using var mounted = V.Mount(root, V.Div(name: "leaf", className: className));
            var plan = MotionSpringClassParser.Resolve(new[] { "pt-[0px]" }, to);
            var settled = root.Q<VisualElement>("leaf").style.paddingTop;

            // Assert
            Assert.That((Lengths(plan), settled.keyword, settled.value.value),
                Is.EqualTo(("", keyword, value)));
        }

        // GREEN_ON_BASE(characterization): an x and a y translate on one side each drive their own axis.
        // The slot-by-slot reading has to keep that although the two share one longhand.
        [Test]
        public void Given_AnXAndAYTranslateOnOneSide_When_Resolved_Then_BothAxesTakeTheirOwnValue()
        {
            // Arrange — translate-x-4 is 16px and translate-y-2 is 8px on the spacing scale.
            var to = new[] { "translate-x-4", "translate-y-2" };

            // Act
            var plan = MotionSpringClassParser.Resolve(new string[0], to);

            // Assert
            Assert.That((plan.TranslateX?.to, plan.TranslateY?.to), Is.EqualTo(((float?)16f, (float?)8f)));
        }

        [TestCase("scale-x-[.5]")]
        [TestCase("scale-y-[.5]")]
        public void Given_APerAxisScaleOnTheLeavingSide_When_Resolved_Then_OnlyTheUniformControlHasAScaleChannel(string layer)
        {
            // Arrange
            var from = new[] { "scale-[2]", layer };
            var to = new[] { "scale-[1]" };

            // Act
            var control = MotionSpringClassParser.Resolve(new[] { "scale-[2]" }, to);
            var plan = MotionSpringClassParser.Resolve(from, to);

            // Assert
            Assert.That((control.Scale?.to, plan.Scale.HasValue), Is.EqualTo(((float?)1f, false)));
        }

        // GREEN_ON_BASE(characterization): the old numeric reader ignores gated stylesheet classes;
        // assigning cascade ranks must preserve that omission.
        [Test]
        public void Given_AGatedScaleRuleBesideAPlainOne_When_Resolved_Then_OnlyThePlainRuleSelectsTheTarget()
        {
            // Arrange
            const string gatedClass = "active-scale-95";
            var exists = StyleUtilityProperties.TryGet(gatedClass, out var rule);
            var to = new[] { "scale-150", gatedClass };

            // Act
            var plan = MotionSpringClassParser.Resolve(new[] { "scale-50" }, to);

            // Assert
            Assert.That((exists, rule.Gate, plan.Scale?.to),
                Is.EqualTo((true, StyleUtilityGate.Active, (float?)1.5f)));
        }

        // GREEN_ON_BASE(construction): the base compares its own drivable map; replacing Height's
        // mask with `StyleArbitraryLonghands.Of(ArbitraryProperty.Width)` would introduce an alias.
        [Test]
        public void Given_TheDrivableProperties_When_TheirLonghandMasksAreGrouped_Then_TheirMasksAreNonemptyAndDistinct()
        {
            // Arrange
            var properties = System.Enum.GetValues(typeof(ArbitraryProperty)).Cast<ArbitraryProperty>()
                .Distinct().Where(MotionPropertyClassParser.IsDrivable).ToArray();

            // Act
            var empty = properties.Where(property => StyleArbitraryLonghands.Of(property).IsEmpty);
            var aliases = properties.GroupBy(StyleArbitraryLonghands.Of).Where(group => group.Count() > 1)
                .Select(group => string.Join(",", group));

            // Assert
            Assert.That((properties.Length > 0, string.Join(" ", empty), string.Join(" ", aliases)),
                Is.EqualTo((true, string.Empty, string.Empty)));
        }

        private static string Lengths(MotionSpringClassParser.SpringPlan plan)
            => string.Join(" ", (plan.Lengths ?? new List<MotionSpringClassParser.LengthChannelPlan>())
                .Select(channel => $"{channel.Property}:{channel.From}->{channel.To}")
                .OrderBy(text => text, System.StringComparer.Ordinal));
    }
}
