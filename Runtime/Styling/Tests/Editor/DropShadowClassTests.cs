using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>drop-shadow-*</c> utility family (StyleShadowClass): the
    /// filter-shadow scale parses to its own (tighter, fainter) presets, the bare
    /// <c>drop-shadow</c> is the DEFAULT preset, and — because Velvet renders both shadow
    /// families through one silhouette-following element — <c>shadow-*</c> and
    /// <c>drop-shadow-*</c> share a single cascade slot where the LAST recognized utility of
    /// either family wins (<c>-none</c> included). GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class DropShadowClassTests
    {
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
    }
}
