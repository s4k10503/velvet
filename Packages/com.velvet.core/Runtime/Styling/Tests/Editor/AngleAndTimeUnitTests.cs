using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the angle and time units a bracket reads: in any case, as CSS reads them, and a bare number only
    /// where the CSS property Tailwind writes takes one.
    /// </summary>
    internal sealed class AngleAndTimeUnitTests
    {
        [TestCase("duration-[150MS]", 0.15f)]
        [TestCase("duration-[1.5S]", 1.5f)]
        public void Given_ATimeUnitInUpperCase_When_ParsedAsADuration_Then_ItIsThatManySeconds(string utility, float seconds)
        {
            // Act
            StyleArbitraryValueResolver.TryParse(utility, out var s);

            // Assert
            Assert.That(s.Property == ArbitraryProperty.TransitionDuration ? s.Value : float.NaN,
                Is.EqualTo(seconds).Within(1e-6f));
        }

        [Test]
        public void Given_AnAnimateDurationInUpperCase_When_Extracted_Then_ItIsThatManySeconds()
        {
            // Act
            StyleAnimateClass.TryExtract(new[] { "animate-spin-[2500MS]" }, out var spec);

            // Assert
            Assert.That(spec.DurationSec, Is.EqualTo(2.5f).Within(1e-6f));
        }

        [TestCase("rotate-[45DEG]", 45f)]
        [TestCase("rotate-[.125TURN]", 45f)]
        [TestCase("rotate-[50GRAD]", 45f)]
        public void Given_AnAngleInUpperCase_When_ParsedAsARotate_Then_ItIsThatManyDegrees(string utility, float degrees)
        {
            // Act
            StyleArbitraryValueResolver.TryParse(utility, out var s);

            // Assert
            Assert.That(s.Property == ArbitraryProperty.Rotate ? s.Value : float.NaN, Is.EqualTo(degrees).Within(1e-3f));
        }

        [TestCase("rotate-[45]", "rotate-[45deg]")]
        [TestCase("rotate-[0]", "rotate-[0deg]")]
        public void Given_ABareNumberRotate_When_Parsed_Then_TheClassIsDeclined(string written, string withAUnit)
        {
            // Act
            var declined = !StyleArbitraryValueResolver.TryParse(written, out _);
            var resolved = StyleArbitraryValueResolver.TryParse(withAUnit, out _);

            // Assert
            Assert.That((declined, resolved), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AHueRotateInUpperCase_When_Parsed_Then_ItIsThatManyDegrees()
        {
            // Act
            StyleArbitraryValueResolver.TryParse("hue-rotate-[.5TURN]", out var s);

            // Assert
            Assert.That(s.Property == ArbitraryProperty.FilterHueRotate ? s.Value : float.NaN, Is.EqualTo(180f).Within(1e-3f));
        }

        [TestCase("skew-x-[10DEG]", 10f)]
        [TestCase("skew-x-[.25turn]", 90f)]
        [TestCase("skew-x-[0]", 0f)]
        public void Given_ASkewBracketInAnyAngleUnit_When_Parsed_Then_ItIsThatManyDegrees(string utility, float degrees)
        {
            // Act
            var ok = StyleSkewClass.TryParse(utility, out _, out var deg);

            // Assert
            Assert.That(ok ? deg : float.NaN, Is.EqualTo(degrees).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base took a skew bracket only in deg, so a bare 10 was declined there too.
        [Test]
        public void Given_ABareNonZeroSkew_When_Parsed_Then_TheClassIsDeclined()
        {
            // Act
            var declined = !StyleSkewClass.TryParse("skew-x-[10]", out _, out _);
            var resolved = StyleSkewClass.TryParse("skew-x-[10deg]", out _, out _);

            // Assert
            Assert.That((declined, resolved), Is.EqualTo((true, true)));
        }

        [TestCase("bg-linear-[45DEG]", 45f)]
        [TestCase("bg-linear-[.125turn]", 45f)]
        public void Given_ALinearAngleBracket_When_Extracted_Then_ItIsThatAngle(string utility, float degrees)
        {
            // Act
            var ok = StyleGradientClass.TryExtract(new[] { utility, "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(ok ? spec.AngleDeg : float.NaN, Is.EqualTo(degrees).Within(1e-3f));
        }

        [TestCase("bg-linear-[45]")]
        [TestCase("-bg-linear-[0]")]
        [TestCase("bg-conic-[45deg]")]
        public void Given_AnAngleBracketTailwindOrCssDeclines_When_Extracted_Then_ThePlainActivatorStillWins(string utility)
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r", "from-[#ff0000]", utility, "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        // GREEN_ON_BASE(characterization): the base read a bracket angle only in lower-case deg, so this was declined.
        [Test]
        public void Given_ANegatedAngleOutOfTailwindsCase_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "-bg-linear-[45DEG]", "to-[#0000ff]" }, out var written);
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "-bg-linear-[45deg]", "to-[#0000ff]" }, out var inTailwindsCase);

            // Assert
            Assert.That((written.AngleDeg, inTailwindsCase.AngleDeg), Is.EqualTo((90f, -45f)));
        }

        [Test]
        public void Given_AConicStartInUpperCase_When_Extracted_Then_ItIsThatAngle()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "bg-conic-[from_90DEG]", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(ok ? spec.AngleDeg : float.NaN, Is.EqualTo(90f));
        }
    }
}
