using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a value the browser rejects does to the rule Tailwind composes it into: a rejected ring
    /// colour, ring width, ring offset colour or shadow-[…] value discards the element's whole box-shadow, and a
    /// rejected gradient stop colour or position falls back to its registered initial value.
    /// </summary>
    internal sealed class RejectedValueTests
    {
        [TestCase("ring", "ring-[5PX]")]
        [TestCase("ring-2", "ring-[2]")]
        [TestCase("ring", "ring-[length:2]")]
        [TestCase("ring", "shadow-[0_4_8_#101820]")]
        public void Given_ARingWhoseBoxShadowTheBrowserDiscards_When_Extracted_Then_NoRingIsDrawn(string ring, string rejected)
        {
            // Act
            var ok = StyleRingClass.TryExtract(new[] { ring, rejected }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AShadowBesideARejectedRingColour_When_Extracted_Then_TheShadowIsDiscardedToo()
        {
            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "ring-2", "shadow-lg", "ring-[5PX]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AShadowBesideARejectedRingWidth_When_Extracted_Then_TheShadowIsDiscarded()
        {
            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "shadow-lg", "ring-[length:2]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ARingWithAnOffsetWidthAndARejectedOffsetColour_When_Extracted_Then_NoRingIsDrawn()
        {
            // Act
            var ok = StyleRingClass.TryExtract(new[] { "ring", "ring-offset-2", "ring-offset-[2]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base ignored a rejected offset colour, as a ring without an offset width must.
        [Test]
        public void Given_ARejectedOffsetColourWithoutAnOffsetWidth_When_Extracted_Then_TheRingIsDrawn()
        {
            // Act
            var ok = StyleRingClass.TryExtract(new[] { "ring", "ring-offset-[2]" }, out _);

            // Assert
            Assert.That(ok, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base ignored a rejected ring colour, and a later colour still wins here.
        [Test]
        public void Given_ARejectedRingColourOverriddenByALaterColour_When_Extracted_Then_TheRingIsDrawn()
        {
            // Act
            var ok = StyleRingClass.TryExtract(new[] { "ring", "ring-[5PX]", "ring-[#ff0000]" }, out var spec);

            // Assert
            Assert.That(ok ? spec.Color : Color.clear, Is.EqualTo(Color.red));
        }

        [Test]
        public void Given_AFromColourTheBrowserRejects_When_Extracted_Then_TheStopIsTransparent()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-[1IN]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Stops?.Length > 0 ? spec.Stops[0].Color : Color.red, Is.EqualTo(Color.clear));
        }

        [Test]
        public void Given_AFromPositionTheBrowserRejects_When_Extracted_Then_TheStopIsAtItsInitialPosition()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-25%", "from-[length:red]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Stops?.Length > 0 ? spec.Stops[0].Position : float.NaN, Is.EqualTo(0f));
        }
    }
}
