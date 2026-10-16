using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies Tailwind's bracket type hint (<c>[length:…]</c>, <c>[color:…]</c>): a utility that writes its
    /// bracket through drops it, and one that reads the bracket as either a length or a colour takes the branch
    /// the hint names in place of its type tests.
    /// </summary>
    internal sealed class TypeHintTests
    {
        [TestCase("w-[length:10px]", 10f)]
        [TestCase("top-[length:1IN]", 96f)]
        [TestCase("rotate-[angle:45deg]", 45f)]
        [TestCase("border-[length:2PX]", 2f)]
        [TestCase("border-t-[length:2PX]", 2f)]
        [TestCase("border-[line-width:thick]", 5f)]
        [TestCase("text-[absolute-size:x-large]", 24f)]
        public void Given_AHintedBracket_When_Parsed_Then_ItIsTheValueAfterTheHint(string utility, float expected)
        {
            // Act
            StyleArbitraryValueResolver.TryParse(utility, out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_AHintedDuration_When_Parsed_Then_TheHintIsDropped()
        {
            // Act
            StyleArbitraryValueResolver.TryParse("duration-[length:150ms]", out var s);

            // Assert
            Assert.That(s.Property == ArbitraryProperty.TransitionDuration ? s.Value : float.NaN, Is.EqualTo(0.15f).Within(1e-6f));
        }

        [TestCase("text-[color:#ff0000]")]
        [TestCase("border-[color:#ff0000]")]
        [TestCase("bg-[color:#ff0000]")]
        public void Given_AColourHint_When_Parsed_Then_ItIsThatColour(string utility)
        {
            // Act
            StyleArbitraryValueResolver.TryParse(utility, out var s);

            // Assert
            Assert.That(s.Color, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_AColourHintWithAnOpacityModifier_When_Parsed_Then_ItIsThatColourAtHalfAlpha()
        {
            // Act
            StyleArbitraryValueResolver.TryParse("bg-[color:#ff0000]/50", out var s);

            // Assert
            Assert.That(s.Color, Is.EqualTo(new Color(1f, 0f, 0f, 0.5f)));
        }

        // GREEN_ON_BASE(characterization): the base read no hint, so each of these failed both its parses there too.
        [TestCase("border-[color:2px]", "border-[2px]")]
        [TestCase("text-[color:16px]", "text-[16px]")]
        [TestCase("border-[length:red]", "border-[red]")]
        [TestCase("text-[length:red]/50", "text-[red]/50")]
        public void Given_AHintNamingTheOtherBranch_When_Parsed_Then_TheClassIsDeclined(string hinted, string unhinted)
        {
            // Act
            var declined = !StyleArbitraryValueResolver.TryParse(hinted, out _);
            var resolved = StyleArbitraryValueResolver.TryParse(unhinted, out _);

            // Assert
            Assert.That((declined, resolved), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ALengthHintedRingWidth_When_Extracted_Then_ItIsThatWidth()
        {
            // Act
            var ok = StyleRingClass.TryExtract(new[] { "ring-[length:5PX]" }, out var spec);

            // Assert
            Assert.That(ok ? spec.Width : float.NaN, Is.EqualTo(5f));
        }

        [Test]
        public void Given_AColourHintedRing_When_Extracted_Then_ItIsThatColour()
        {
            // Act
            var ok = StyleRingClass.TryExtract(new[] { "ring-2", "ring-[color:#ff0000]" }, out var spec);

            // Assert
            Assert.That(ok ? spec.Color : Color.clear, Is.EqualTo(Color.red));
        }

        [TestCase("outline-[length:2PX]", 2f)]
        [TestCase("outline-[length:2]", 3f)]
        public void Given_ALengthHintedOutline_When_Extracted_Then_ItIsTheWidthTheBrowserKeeps(string utility, float width)
        {
            // Act
            var ok = StyleRingClass.TryExtract(new[] { utility }, out var spec);

            // Assert
            Assert.That(ok ? spec.Width : float.NaN, Is.EqualTo(width));
        }

        [TestCase("ring-offset-[length:5PX]")]
        [TestCase("outline-offset-[length:5px]")]
        public void Given_AHintedOffset_When_Extracted_Then_ItIsThatOffset(string utility)
        {
            // Act
            StyleRingClass.TryExtract(new[] { "ring", utility }, out var spec);

            // Assert
            Assert.That(spec.Offset, Is.EqualTo(5f));
        }

        [Test]
        public void Given_ALengthHintedFromPosition_When_Extracted_Then_ItIsThosePixels()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#00ff00]", "from-[length:1IN]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Stops?.Length > 0 ? spec.Stops[0].PositionPx : float.NaN, Is.EqualTo(96f).Within(1e-3f));
        }

        [Test]
        public void Given_AColourHintedFromStop_When_Extracted_Then_ItIsThatColour()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r", "from-[color:#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Stops?.Length > 0 ? spec.Stops[0].Color : Color.clear, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_AnAngleHintedNegativeLinear_When_Extracted_Then_ItIsNegated()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "-bg-linear-[angle:45DEG]", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(ok ? spec.AngleDeg : float.NaN, Is.EqualTo(-45f));
        }

        [Test]
        public void Given_AHintedGap_When_Parsed_Then_ItIsThatGap()
        {
            // Act
            StyleGapClass.TryParse("gap-[length:8px]", out var gap, out _);

            // Assert
            Assert.That(gap, Is.EqualTo(8f));
        }

        [Test]
        public void Given_AHintedDivideWidth_When_Extracted_Then_ItIsThatWidth()
        {
            // Act
            StyleDivideClass.TryExtract(new[] { "divide-x-[length:2px]" }, out var spec);

            // Assert
            Assert.That(spec.Width, Is.EqualTo(2f));
        }

        [Test]
        public void Given_AHintedSkew_When_Parsed_Then_ItIsThatAngle()
        {
            // Act
            var ok = StyleSkewClass.TryParse("skew-x-[angle:10deg]", out _, out var deg);

            // Assert
            Assert.That(ok ? deg : float.NaN, Is.EqualTo(10f));
        }

        [Test]
        public void Given_AHintedZIndex_When_Parsed_Then_ItIsThatIndex()
        {
            // Act
            StyleZIndexClass.TryParse("z-[integer:5]", out var z);

            // Assert
            Assert.That(z, Is.EqualTo(5));
        }

        [Test]
        public void Given_AHintedLeading_When_ParsedAndApplied_Then_ProducesThePixelTag()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { "leading-[length:20px]" });
            var result = StyleTextEffectClass.Apply("hi", null, null, null, effect.Leading);

            // Assert
            Assert.That(result, Is.EqualTo("<line-height=20px>hi</line-height>"));
        }

        [Test]
        public void Given_AShadowWithAHint_When_Extracted_Then_TheHintIsDropped()
        {
            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "shadow-[length:0_4px_8px_#101820]" }, out var spec);

            // Assert
            Assert.That(ok ? spec.OffsetY : float.NaN, Is.EqualTo(4f));
        }
    }
}
