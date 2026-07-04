using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the parsing contract for Velvet's <c>shadow-*</c> utility classes
    /// (<see cref="StyleShadowClass"/>): preset resolution, CSS-cascade "last class wins" (including
    /// <c>shadow-none</c> overriding an earlier <c>shadow-lg</c>), and the companion <c>rounded-*</c>
    /// corner-radius resolution the shadow silhouette follows. These are layout-independent (pure string
    /// parsing), so they assert without a panel or a reconcile. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ShadowClassParityTests
    {
        [Test]
        public void Given_ShadowLg_When_Extracted_Then_ResolvesTheLgPresetBlur()
        {
            // Arrange
            var classes = new[] { "rounded-2xl", "shadow-lg" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found && spec.Blur == 34f, Is.True);
        }

        [Test]
        public void Given_BareShadow_When_Extracted_Then_ResolvesTheDefaultPreset()
        {
            // Arrange
            var classes = new[] { "shadow" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found && spec.Blur == 14f, Is.True);
        }

        [Test]
        public void Given_ShadowLgThenShadowNone_When_Extracted_Then_NoneWinsAndNoShadowIsWanted()
        {
            // Arrange: later class wins the cascade.
            var classes = new[] { "shadow-lg", "shadow-none" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out _);

            // Assert
            Assert.That(found, Is.False);
        }

        [Test]
        public void Given_ShadowNoneThenShadowLg_When_Extracted_Then_LgWins()
        {
            // Arrange
            var classes = new[] { "shadow-none", "shadow-lg" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found && spec.Blur == 34f, Is.True);
        }

        [Test]
        public void Given_NoShadowClass_When_Extracted_Then_NoShadowIsWanted()
        {
            // Arrange
            var classes = new[] { "rounded-2xl", "bg-white" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out _);

            // Assert
            Assert.That(found, Is.False);
        }

        [Test]
        public void Given_Rounded2xl_When_CornerRadiusResolved_Then_Returns16()
        {
            // Arrange
            var classes = new[] { "shadow-lg", "rounded-2xl" };

            // Act
            var resolved = StyleShadowClass.TryResolveCornerRadius(classes, out var radius);

            // Assert — --radius-2xl == 16px (the radius scale).
            Assert.That(resolved && radius == 16f, Is.True);
        }

        [Test]
        public void Given_RoundedTl2xl_When_CornerRadiusResolved_Then_TopLeftCornerIsRead()
        {
            // Arrange: per-corner top-left utility (not swallowed by the rounded- prefix).
            var classes = new[] { "rounded-tl-2xl" };

            // Act
            var resolved = StyleShadowClass.TryResolveCornerRadius(classes, out var radius);

            // Assert — --radius-2xl == 16px (the radius scale).
            Assert.That(resolved && radius == 16f, Is.True);
        }

        [Test]
        public void Given_RoundedFull_When_CornerRadiusResolved_Then_LeftToGeometryPath()
        {
            // Arrange: rounded-full has no fixed px in the scale; the resolvedStyle path handles it.
            var classes = new[] { "rounded-full" };

            // Act
            var resolved = StyleShadowClass.TryResolveCornerRadius(classes, out _);

            // Assert
            Assert.That(resolved, Is.False);
        }

        [Test]
        public void Given_ArbitraryShadowValue_When_Extracted_Then_ParsesTheBlurLength()
        {
            // Arrange: an arbitrary box-shadow (x_y_blur_color). Blur is the third length.
            var classes = new[] { "rounded-2xl", "shadow-[0_4px_8px_#000000]" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found && spec.Blur == 8f, Is.True);
        }

        [Test]
        public void Given_ArbitraryShadowValue_When_Extracted_Then_OffsetsAreAssignedPositionally()
        {
            // Arrange: x=2, y=4 — CSS order is offset-x then offset-y.
            var classes = new[] { "shadow-[2px_4px_8px_#101820]" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found && spec.OffsetX == 2f && spec.OffsetY == 4f, Is.True);
        }

        [Test]
        public void Given_ArbitraryShadowWithRgbaColor_When_Extracted_Then_ParsesTheAlpha()
        {
            // Arrange: an rgba() shadow color (the token keeps its commas through the underscore split).
            var classes = new[] { "shadow-[0_4px_8px_rgba(10,20,30,0.5)]" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found && spec.Color.a == 0.5f, Is.True);
        }
    }
}
