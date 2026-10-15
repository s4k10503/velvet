using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The line height each <c>text-&lt;size&gt;</c> carries beside its font size, the
    /// <c>text-&lt;size&gt;/&lt;leading&gt;</c> form, and that a <c>leading-*</c> outranks the pair.
    /// </summary>
    [TestFixture]
    internal sealed class StyleNamedTextSizeTests
    {
        [TestCase("text-xs", 1.3333334f)]
        [TestCase("text-sm", 1.4285715f)]
        [TestCase("text-base", 1.5f)]
        [TestCase("text-lg", 1.5555556f)]
        [TestCase("text-xl", 1.4f)]
        [TestCase("text-2xl", 1.3333334f)]
        [TestCase("text-3xl", 1.2f)]
        [TestCase("text-4xl", 1.1111112f)]
        [TestCase("text-5xl", 1f)]
        [TestCase("text-6xl", 1f)]
        [TestCase("text-7xl", 1f)]
        [TestCase("text-8xl", 1f)]
        [TestCase("text-9xl", 1f)]
        public void Given_ANamedTextSize_When_Parsed_Then_ItsLeadingIsTailwindsPairedEmMultiplier(
            string className, float expected)
        {
            // Act
            var leading = StyleTextEffectClass.Parse(new[] { className }).Leading;

            // Assert
            Assert.That(leading is { Unit: LeadingUnit.Em } em ? em.Value : float.NaN,
                Is.EqualTo(expected).Within(1e-6f));
        }

        // GREEN_ON_BASE(characterization): the base reads no size class, so the leading is all it holds.
        [Test]
        public void Given_ALeadingBeforeATextSize_When_Parsed_Then_TheLeadingWins()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { "leading-loose", "text-sm" });

            // Assert
            Assert.That(effect.Leading, Is.EqualTo(new LeadingValue(LeadingUnit.Em, 2f)));
        }

        // GREEN_ON_BASE(characterization): the base reads no size class, so the leading is all it holds.
        [Test]
        public void Given_ALeadingAfterATextSize_When_Parsed_Then_TheLeadingWins()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { "text-sm", "leading-6" });

            // Assert
            Assert.That(effect.Leading, Is.EqualTo(new LeadingValue(LeadingUnit.Pixel, 24f)));
        }

        [Test]
        public void Given_AnImportantTextSizeAndAPlainLeading_When_Parsed_Then_TheImportantSizesPairWins()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { "!text-base", "leading-6" });

            // Assert
            Assert.That(effect.Leading, Is.EqualTo(new LeadingValue(LeadingUnit.Em, 1.5f)));
        }

        // GREEN_ON_BASE(characterization): the base reads no size class, so the leading is all it holds.
        [Test]
        public void Given_APlainTextSizeAndAnImportantLeading_When_Parsed_Then_TheImportantLeadingWins()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { "text-base", "!leading-6" });

            // Assert
            Assert.That(effect.Leading, Is.EqualTo(new LeadingValue(LeadingUnit.Pixel, 24f)));
        }

        // GREEN_ON_BASE(characterization): the base reads no size class, so the leading is all it holds.
        [Test]
        public void Given_ASizeAndALeadingBesideAnUnrelatedImportantClass_When_Parsed_Then_TheLeadingStillWins()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { "text-base", "leading-6", "!uppercase" });

            // Assert
            Assert.That(effect.Leading, Is.EqualTo(new LeadingValue(LeadingUnit.Pixel, 24f)));
        }

        [Test]
        public void Given_TwoTextSizes_When_Parsed_Then_TheLastOnesPairWins()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { "text-xs", "text-base" });

            // Assert
            Assert.That(effect.Leading, Is.EqualTo(new LeadingValue(LeadingUnit.Em, 1.5f)));
        }

        [TestCase("text-sm/6", LeadingUnit.Pixel, 24f)]
        [TestCase("text-9xl/[22px]", LeadingUnit.Pixel, 22f)]
        [TestCase("text-xs/loose", LeadingUnit.Em, 2f)]
        [TestCase("text-base/[1.2]", LeadingUnit.Em, 1.2f)]
        [TestCase("text-[14px]/6", LeadingUnit.Pixel, 24f)]
        [TestCase("text-[length:2rem]/[1.2]", LeadingUnit.Em, 1.2f)]
        public void Given_ATextSizeWithALeadingSuffix_When_Parsed_Then_TheSuffixIsTheLeadingUtilitysValue(
            string className, LeadingUnit unit, float amount)
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { className });

            // Assert
            Assert.That(effect.Leading, Is.EqualTo(new LeadingValue(unit, amount)));
        }

        // GREEN_ON_BASE(characterization): the base reads no size class, so the leading is all it holds.
        [Test]
        public void Given_ASlashTextSizeAfterALeading_When_Parsed_Then_TheLeadingWins()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { "leading-loose", "text-sm/6" });

            // Assert
            Assert.That(effect.Leading, Is.EqualTo(new LeadingValue(LeadingUnit.Em, 2f)));
        }

        // GREEN_ON_BASE(characterization): the base reads no size class, so it sets no leading from one.
        [TestCase("text-sm/abc")]
        [TestCase("text-sm/")]
        [TestCase("text-huge/6")]
        [TestCase("p-4")]
        [TestCase("text-[14px]")]
        [TestCase("text-[#fff]/50")]
        [TestCase("text-red-500/50")]
        [TestCase("text-[abc]/6")]
        [TestCase("text-[14px]/abc")]
        public void Given_ATokenThatIsNoReadableTextSize_When_Parsed_Then_NoLeadingIsSet(string className)
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { className });

            // Assert
            Assert.That(effect.Leading, Is.Null);
        }

        [TestCase("text-xs/4", 12f)]
        [TestCase("text-sm/6", 14f)]
        [TestCase("text-9xl/none", 128f)]
        [TestCase("text-[14px]/6", 14f)]
        [TestCase("text-[length:20px]/6", 20f)]
        [TestCase("text-[1.5rem]/none", 24f)]
        public void Given_ATextSizeWithALeadingSuffix_When_ResolvedAsAnInlineValue_Then_ItIsTheSizesFontSize(
            string className, float expectedPx)
        {
            // Act
            var parsed = StyleArbitraryValueResolver.TryParse(className, out var style);

            // Assert
            Assert.That((parsed, style.Property, style.Value, style.Unit),
                Is.EqualTo((true, ArbitraryProperty.FontSize, expectedPx, LengthUnit.Pixel)));
        }

        [Test]
        public void Given_ALengthHintedBracketSize_When_ResolvedAsAnInlineValue_Then_ItIsTheFontSize()
        {
            // Act
            var parsed = StyleArbitraryValueResolver.TryParse("text-[length:20px]", out var style);

            // Assert
            Assert.That((parsed, style.Property, style.Value, style.Unit),
                Is.EqualTo((true, ArbitraryProperty.FontSize, 20f, LengthUnit.Pixel)));
        }

        // GREEN_ON_BASE(characterization): the base resolves no slash form of a size, so it declines this.
        [TestCase("text-sm/abc")]
        [TestCase("text-[14px]/abc")]
        public void Given_ATextSizeWithAnUnreadableSuffix_When_ResolvedAsAnInlineValue_Then_ItIsDeclined(
            string className)
        {
            // Act
            var parsed = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That(parsed, Is.False);
        }

        [TestCase("hover:text-sm/6")]
        [TestCase("hover:text-[14px]/6")]
        public void Given_AHoverGatedSlashTextSize_When_TheElementIsHoveredAndLeft_Then_TheLineHeightAppliesOnlyWhileHovered(
            string className)
        {
            // Arrange
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Label(className: className, text: "hi", name: "label") });
            var label = scope.Root.Q<Label>("label");
            var before = label.text;

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                label.SimulateEvent(over);
            }
            var hovered = label.text;
            using (var leave = PointerOutEvent.GetPooled())
            {
                label.SimulateEvent(leave);
            }

            // Assert
            Assert.That((before, hovered, label.text),
                Is.EqualTo(("hi", "<line-height=24px>hi</line-height>", "hi")));
        }

        [Test]
        public void Given_ALabelWithATextSize_When_Mounted_Then_ItsTextCarriesThePairedLineHeight()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Label(className: "text-base", text: "hi", name: "label") });

            // Assert
            Assert.That(scope.Root.Q<Label>("label").text, Is.EqualTo("<line-height=1.5em>hi</line-height>"));
        }

        [Test]
        public void Given_ALabelWithASlashTextSize_When_Mounted_Then_ItsTextCarriesTheSuffixLineHeight()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Label(className: "text-sm/6", text: "hi", name: "label") });

            // Assert
            Assert.That(scope.Root.Q<Label>("label").text, Is.EqualTo("<line-height=24px>hi</line-height>"));
        }

        [Test]
        public void Given_ALabelWithASlashTextSize_When_Mounted_Then_ItsInlineFontSizeIsTheSizes()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Label(className: "text-sm/6", text: "hi", name: "label") });

            // Assert
            Assert.That(scope.Root.Q<Label>("label").style.fontSize.value.value, Is.EqualTo(14f));
        }

        [Test]
        public void Given_ATextSizeUnderALeadingAncestor_When_Mounted_Then_TheSizesPairReplacesTheAncestorsLeading()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "leading-loose", children: new VNode[]
                {
                    V.Label(className: "text-base", text: "hi", name: "label"),
                }),
            });

            // Assert
            Assert.That(scope.Root.Q<Label>("label").text, Is.EqualTo("<line-height=1.5em>hi</line-height>"));
        }
    }
}
