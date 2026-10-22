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
        [TestCase("ring ring-[5PX]")]
        [TestCase("ring-2 ring-[2]")]
        [TestCase("ring ring-[length:2]")]
        [TestCase("ring shadow-[0_4_8_#101820]")]
        [TestCase("ring shadow-[4px]")]
        [TestCase("ring-2 shadow-[0_4_8_#000] shadow-[#ff0000]")]
        [TestCase("ring-2 shadow-[0_4_8_#000] shadow-[color:red]")]
        public void Given_ARingWhoseBoxShadowTheBrowserDiscards_When_Extracted_Then_NoRingIsDrawn(string classes)
        {
            // Act
            var ok = StyleRingClass.TryExtract(classes.Split(' '), out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base drew these rings, whose box-shadow holds no value the browser rejects.
        [TestCase("ring ring-[var(--brand)]")]
        [TestCase("ring-2 ring-[oklch(62.3%_0.214_259.815)]")]
        [TestCase("ring ring-[currentColor]")]
        [TestCase("ring ring-[rebeccapurple]")]
        [TestCase("ring ring-[length:var(--w)]")]
        [TestCase("ring shadow-[var(--shadow)]")]
        [TestCase("ring shadow-[var(--shadow)] shadow-[#ggg]")]
        [TestCase("ring inset-ring-[length:0]")]
        public void Given_ARingValueVelvetDoesNotRead_When_Extracted_Then_TheRingIsDrawn(string classes)
        {
            // Act
            var ok = StyleRingClass.TryExtract(classes.Split(' '), out _);

            // Assert
            Assert.That(ok, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base drew this shadow, whose ring colour the browser takes.
        [Test]
        public void Given_AShadowBesideARingColourVelvetDoesNotRead_When_Extracted_Then_TheShadowIsDrawn()
        {
            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "ring-2", "ring-[oklch(62.3%_0.214_259.815)]", "shadow-md" }, out _);

            // Assert
            Assert.That(ok, Is.True);
        }

        [TestCase("ring-2 shadow-lg ring-[5PX]")]
        [TestCase("shadow-lg ring-[length:2]")]
        [TestCase("shadow-lg ring-[length:0]")]
        [TestCase("shadow-lg ring-[length:50%]")]
        [TestCase("shadow-md shadow-[#ggg]")]
        [TestCase("shadow-md inset-ring-[length:2]")]
        [TestCase("shadow-md inset-ring inset-ring-[2]")]
        [TestCase("shadow-md inset-shadow-[4px]")]
        [TestCase("shadow-md inset-shadow-[inset_0_2px_4px_red]")]
        [TestCase("shadow-md inset-shadow-sm inset-shadow-[#ggg]")]
        public void Given_AShadowBesideAValueTheBrowserRejects_When_Extracted_Then_TheShadowIsDiscarded(string classes)
        {
            // Act
            var ok = StyleShadowClass.TryExtract(classes.Split(' '), out _);

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

        // GREEN_ON_BASE(characterization): the base left a stop colour it could not read alone, as the browser may take it.
        [Test]
        public void Given_AFromColourVelvetDoesNotRead_When_Extracted_Then_TheEarlierColourHolds()
        {
            // Arrange
            VelvetPalette.TryResolveColorToken("red-500", out var red);

            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-red-500", "from-[var(--x)]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Stops?.Length > 0 ? spec.Stops[0].Color : Color.clear, Is.EqualTo(red));
        }

        // GREEN_ON_BASE(characterization): the base left a hinted position it could not place alone, as the browser takes it.
        [Test]
        public void Given_AViaPositionInAUnitTheLineCannotPlace_When_Extracted_Then_TheEarlierPositionHolds()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "via-[#00ff00]", "via-30%", "via-[length:2em]", "to-[#0000ff]" },
                out var spec);

            // Assert
            Assert.That(spec.Stops?.Length > 1 ? spec.Stops[1].Position : float.NaN, Is.EqualTo(0.3f).Within(1e-5f));
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
