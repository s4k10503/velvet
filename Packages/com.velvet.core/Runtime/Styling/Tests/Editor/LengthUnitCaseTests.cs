using System;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the case a bracketed length's unit may be written in: any, where Tailwind writes the bracket
    /// through to CSS, and Tailwind's own, where it reads the bracket as a length or a colour.
    /// </summary>
    internal sealed class LengthUnitCaseTests
    {
        [TestCase("96PX")]
        [TestCase("96Px")]
        [TestCase("6REM")]
        [TestCase("1IN")]
        [TestCase("2.54CM")]
        [TestCase("25.4MM")]
        [TestCase("72PT")]
        [TestCase("6PC")]
        [TestCase("101.6q")]
        public void Given_AnInchSpelledInAnotherCase_When_ParsedAsAnArbitraryTop_Then_ItIsNinetySixPixels(string length)
        {
            // Act
            StyleArbitraryValueResolver.TryParse("top-[" + length + "]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(96f).Within(1e-3f));
        }

        [TestCase("calc(96PX)")]
        [TestCase("calc(1IN)")]
        public void Given_AMathFunctionOfAnUpperCaseUnit_When_ParsedAsAnArbitraryTop_Then_ItIsNinetySixPixels(string length)
        {
            // Act
            StyleArbitraryValueResolver.TryParse("top-[" + length + "]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(96f).Within(1e-3f));
        }

        [TestCase("2EM", 32f)]
        [TestCase("50VW", 400f)]
        [TestCase("50SVH", 300f)]
        public void Given_AnElementRelativeUnitInUpperCase_When_ParsedAsAnArbitraryWidth_Then_ItMeasuresAsLowerCaseDoes(
            string length, float expected)
        {
            // Arrange
            var basis = new RelativeLengthBasis(16f, 0f, 800f, 600f);

            // Act
            StyleArbitraryValueResolver.TryParse("w-[" + length + "]", out var s);

            // Assert
            Assert.That(s.Expression?.Evaluate(basis) ?? float.NaN, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_AnUpperCaseEmLeading_When_ParsedAndApplied_Then_ProducesTheEmTag()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { "leading-[1.5EM]" });
            var result = StyleTextEffectClass.Apply("hi", null, null, null, effect.Leading);

            // Assert
            Assert.That(result, Is.EqualTo("<line-height=1.5em>hi</line-height>"));
        }

        [Test]
        public void Given_AnUpperCasePixelSliceInset_When_Parsed_Then_ItIsThatManyPixels()
        {
            // Act
            StyleArbitraryValueResolver.TryParse("slice-[12PX]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(12f));
        }

        [Test]
        public void Given_AnUpperCaseOutlineOffset_When_Extracted_Then_ItIsThatManyPixels()
        {
            // Act
            StyleRingClass.TryExtract(new[] { "outline", "outline-offset-[5PX]" }, out var spec);

            // Assert
            Assert.That(spec.Offset, Is.EqualTo(5f));
        }

        // GREEN_ON_BASE(characterization): the base matched every unit case-sensitively, so these were declined.
        [TestCase("border-[2PX]")]
        [TestCase("border-t-[2PX]")]
        [TestCase("border-s-[2PX]")]
        [TestCase("text-[16PX]")]
        [TestCase("text-[1IN]")]
        [TestCase("text-[2EM]")]
        [TestCase("text-[101.6q]")]
        public void Given_AUnitOutOfTailwindsCaseWhereItInfersALength_When_Parsed_Then_TheClassIsDeclined(string utility)
        {
            // Act
            var parsed = StyleArbitraryValueResolver.TryParse(utility, out _);

            // Assert
            Assert.That(parsed, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base read these in Tailwind's case, the control for the declining cases above.
        [TestCase("border-[2px]")]
        [TestCase("border-t-[2px]")]
        [TestCase("border-s-[2px]")]
        [TestCase("text-[16px]")]
        [TestCase("text-[1in]")]
        [TestCase("text-[2em]")]
        [TestCase("text-[101.6Q]")]
        public void Given_AUnitInTailwindsCaseWhereItInfersALength_When_Parsed_Then_TheClassResolves(string utility)
        {
            // Act
            var parsed = StyleArbitraryValueResolver.TryParse(utility, out _);

            // Assert
            Assert.That(parsed, Is.True);
        }

        [TestCase("text-[length:16PX]")]
        [TestCase("text-[length:16PX]/6")]
        public void Given_AnUpperCaseFontSizeWithTheLengthHint_When_Parsed_Then_ItIsThatManyPixels(string utility)
        {
            // Act
            StyleArbitraryValueResolver.TryParse(utility, out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(16f));
        }

        [Test]
        public void Given_AMathFunctionOfAnUpperCaseUnitWhereTailwindInfersALength_When_Parsed_Then_ItIsALength()
        {
            // Act
            StyleArbitraryValueResolver.TryParse("border-[calc(2PX)]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(2f));
        }

        // GREEN_ON_BASE(characterization): the base matched every unit case-sensitively, so these were declined.
        [TestCase("ring-[5PX]")]
        [TestCase("outline-[5PX]")]
        public void Given_AnUpperCaseRingWidth_When_Extracted_Then_TheClassIsIgnored(string utility)
        {
            // Act
            var extracted = StyleRingClass.TryExtract(new[] { utility }, out _);

            // Assert
            Assert.That(extracted, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base read these in Tailwind's case, the control for the ignoring cases above.
        [TestCase("ring-[5px]")]
        [TestCase("outline-[5px]")]
        public void Given_ALowerCaseRingWidth_When_Extracted_Then_TheClassIsTaken(string utility)
        {
            // Act
            var extracted = StyleRingClass.TryExtract(new[] { utility }, out _);

            // Assert
            Assert.That(extracted, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base matched every unit case-sensitively, so this was declined.
        [Test]
        public void Given_AnUpperCaseRingOffset_When_Extracted_Then_TheOffsetIsUnset()
        {
            // Act
            StyleRingClass.TryExtract(new[] { "ring", "ring-offset-[5PX]" }, out var spec);

            // Assert
            Assert.That(spec.Offset, Is.EqualTo(0f));
        }

        // GREEN_ON_BASE(characterization): the base read a lower-case offset, the control for the unset case above.
        [Test]
        public void Given_ALowerCaseRingOffset_When_Extracted_Then_ItIsThatManyPixels()
        {
            // Act
            StyleRingClass.TryExtract(new[] { "ring", "ring-offset-[5px]" }, out var spec);

            // Assert
            Assert.That(spec.Offset, Is.EqualTo(5f));
        }

        // GREEN_ON_BASE(characterization): the base matched every unit case-sensitively, so this was declined.
        [Test]
        public void Given_AnUpperCaseFromPosition_When_Extracted_Then_TheStopHasNoPixelPosition()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-[1IN]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(float.IsNaN(spec.Stops[0].PositionPx), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base read a lower-case position, the control for the unset case above.
        [Test]
        public void Given_ALowerCaseFromPosition_When_Extracted_Then_TheStopIsThatManyPixelsIn()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-[1in]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Stops[0].PositionPx, Is.EqualTo(96f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base matched every unit case-sensitively, so this was not reserved.
        [Test]
        public void Given_AnUpperCaseBorderWidth_When_InsetParsed_Then_ItReservesNothing()
        {
            // Act
            SilhouetteBoundsSpacer.BorderInset(new[] { "border-[2PX]" }, out var inset, out _);

            // Assert
            Assert.That(inset, Is.EqualTo(0f));
        }

        // GREEN_ON_BASE(characterization): the base reserved a lower-case width, the control for the reserving-nothing case above.
        [Test]
        public void Given_ALowerCaseBorderWidth_When_InsetParsed_Then_ItReservesThatWidth()
        {
            // Act
            SilhouetteBoundsSpacer.BorderInset(new[] { "border-[2px]" }, out var inset, out _);

            // Assert
            Assert.That(inset, Is.EqualTo(2f));
        }

        [Test]
        public void Given_AParenthesisedUnitWithNoMathFunction_When_ItsUnitCaseIsAsked_Then_TailwindDoesNotTakeItForALength()
        {
            // Act
            var length = StyleArbitraryValueResolver.SpellsUnitAsTailwindInfers("(2PX)".AsSpan());

            // Assert
            Assert.That(length, Is.False);
        }

        [Test]
        public void Given_AnEmptyValue_When_ItsUnitCaseIsAsked_Then_NothingThrows()
        {
            // Assert
            Assert.That(() => StyleArbitraryValueResolver.SpellsUnitAsTailwindInfers(string.Empty.AsSpan()), Throws.Nothing);
        }
    }
}
