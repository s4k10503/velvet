using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the parsing contract for Velvet's <c>shadow-*</c> and <c>drop-shadow-*</c> utility classes
    /// (<see cref="StyleShadowClass"/>): preset resolution for both families, CSS-cascade "last class wins"
    /// (including <c>-none</c> overriding an earlier preset from either family — because Velvet renders both
    /// shadow families through one silhouette-following element, they share a single cascade slot), the
    /// companion <c>rounded-*</c> corner-radius resolution the shadow silhouette follows, and that the C#
    /// corner-radius mirror stays in lockstep with the <c>--radius-*</c> token scale in <c>_tokens.uss</c> (a
    /// token re-alignment that forgets to update this mirror would bake the old corner into the silhouette).
    /// These are layout-independent (pure string parsing), so they assert without a panel or a reconcile. GWT,
    /// one assert per case (Assume for preconditions).
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

        [Test]
        public void Given_ADropShadowPreset_When_Extracted_Then_AShadowIsWanted()
        {
            // Arrange
            var classes = new[] { "w-full", "drop-shadow-md" };

            // Act
            var want = StyleShadowClass.TryExtract(classes, out _);

            // Assert
            Assert.That(want, Is.True);
        }

        [Test]
        public void Given_ADropShadowPreset_When_Extracted_Then_ItUsesTheDropScaleNotTheBoxScale()
        {
            // Arrange — drop-shadow-md is tighter than shadow-md (the filter scale).
            var classes = new[] { "drop-shadow-md" };
            StyleShadowClass.TryExtract(new[] { "shadow-md" }, out var boxSpec);

            // Act
            StyleShadowClass.TryExtract(classes, out var dropSpec);

            // Assert
            Assert.That(dropSpec.Blur, Is.LessThan(boxSpec.Blur));
        }

        [Test]
        public void Given_ABareDropShadow_When_Extracted_Then_TheDefaultPresetResolves()
        {
            // Arrange
            var classes = new[] { "drop-shadow" };

            // Act
            var want = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(want && spec.Blur > 0f, Is.True);
        }

        [Test]
        public void Given_AShadowThenADropShadow_When_Extracted_Then_TheLaterFamilyWins()
        {
            // Arrange — one cascade slot across both families: the later utility wins.
            var classes = new[] { "shadow-md", "drop-shadow-lg" };
            StyleShadowClass.TryExtract(new[] { "drop-shadow-lg" }, out var expected);

            // Act
            StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(spec.Blur, Is.EqualTo(expected.Blur));
        }

        [Test]
        public void Given_AShadowThenADropShadowNone_When_Extracted_Then_NoShadowIsWanted()
        {
            // Arrange — drop-shadow-none must be able to kill an earlier shadow-lg, like CSS resets.
            var classes = new[] { "shadow-lg", "drop-shadow-none" };

            // Act
            var want = StyleShadowClass.TryExtract(classes, out _);

            // Assert
            Assert.That(want, Is.False);
        }

        [Test]
        public void Given_ADropShadowClass_When_Gated_Then_HasShadowClassSeesIt()
        {
            // Arrange — the cheap early-out gate must not skip drop-shadow-only elements.
            var classes = new[] { "drop-shadow-xl" };

            // Act
            var has = StyleShadowClass.HasShadowClass(classes);

            // Assert
            Assert.That(has, Is.True);
        }

        [Test]
        public void Given_RoundedLgClass_When_ResolvingShadowCornerRadius_Then_Is8()
        {
            // Arrange/Act — rounded-lg mirrors --radius-lg, re-aligned to 8px (was 16).
            var ok = StyleShadowClass.TryResolveCornerRadius(new[] { "rounded-lg" }, out var radius);
            Assume.That(ok, Is.True, "Precondition: rounded-lg resolves a mirrored corner radius");

            // Assert
            Assert.That(radius, Is.EqualTo(8f));
        }

        [Test]
        public void Given_Rounded3xlClass_When_ResolvingShadowCornerRadius_Then_Is24()
        {
            // Arrange/Act — rounded-3xl mirrors --radius-3xl, re-aligned to 24px (was 45).
            var ok = StyleShadowClass.TryResolveCornerRadius(new[] { "rounded-3xl" }, out var radius);
            Assume.That(ok, Is.True, "Precondition: rounded-3xl resolves a mirrored corner radius");

            // Assert
            Assert.That(radius, Is.EqualTo(24f));
        }

        [Test]
        public void Given_BareRoundedClass_When_ResolvingShadowCornerRadius_Then_Is4()
        {
            // Arrange/Act — the bare `rounded` DEFAULT mirrors --radius-default (4px); a shadow on a
            // bare-rounded box must follow that corner, not fall through to a square silhouette.
            var ok = StyleShadowClass.TryResolveCornerRadius(new[] { "rounded" }, out var radius);
            Assume.That(ok, Is.True, "Precondition: bare rounded resolves a mirrored corner radius");

            // Assert
            Assert.That(radius, Is.EqualTo(4f));
        }
    }
}
