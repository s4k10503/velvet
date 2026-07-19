using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>border-dashed</c> / <c>border-dotted</c> / <c>border-solid</c> utility parser
    /// (<see cref="StyleBorderStyleClass"/>): dashed / dotted are ACTIVE (a painted outline), border-solid is a
    /// recognized RESET (it overrides an earlier dashed / dotted, keeping the native solid border), and the
    /// cascade is last-token-wins. Mirrors the skew-class parser tests. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleBorderStyleClassTests
    {
        [Test]
        public void Given_BorderDashed_When_Extracted_Then_TheStyleIsDashed()
        {
            // Act
            StyleBorderStyleClass.TryExtract(new[] { "border-2", "border-dashed" }, out var spec);

            // Assert
            Assert.That(spec.Style, Is.EqualTo(BorderLineStyle.Dashed));
        }

        [Test]
        public void Given_BorderDotted_When_Extracted_Then_TheStyleIsDotted()
        {
            // Act
            StyleBorderStyleClass.TryExtract(new[] { "border-dotted" }, out var spec);

            // Assert
            Assert.That(spec.Style, Is.EqualTo(BorderLineStyle.Dotted));
        }

        [Test]
        public void Given_BorderSolid_When_Extracted_Then_ItIsNotActive()
        {
            // Act — border-solid keeps the native (solid) border, so there is no painted outline to attach.
            var active = StyleBorderStyleClass.TryExtract(new[] { "border-solid" }, out _);

            // Assert
            Assert.That(active, Is.False);
        }

        [Test]
        public void Given_DashedThenSolid_When_Extracted_Then_SolidResets()
        {
            // Arrange — CSS cascade: border-solid later in the list overrides the earlier border-dashed.
            var classes = new[] { "border-dashed", "border-solid" };

            // Act
            var active = StyleBorderStyleClass.TryExtract(classes, out _);

            // Assert
            Assert.That(active, Is.False);
        }

        [Test]
        public void Given_SolidThenDashed_When_Extracted_Then_DashedWins()
        {
            // Arrange — last recognized token wins, so dashed overrides an earlier reset.
            var classes = new[] { "border-solid", "border-dashed" };

            // Act
            StyleBorderStyleClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(spec.Style, Is.EqualTo(BorderLineStyle.Dashed));
        }

        [Test]
        public void Given_NoBorderStyleClass_When_GateProbed_Then_ItReturnsFalse()
        {
            // Act — the create / patch fast path depends on this gate ignoring plain width classes.
            var has = StyleBorderStyleClass.HasBorderStyleClass(new[] { "border-2", "w-full" });

            // Assert
            Assert.That(has, Is.False);
        }
    }
}
