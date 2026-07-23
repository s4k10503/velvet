using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the pure geometry behind the <c>overline</c> text-decoration's painted rule
    /// (<see cref="TextOverlinePainter.TryComputeGeometry"/>) and its thickness rule
    /// (<see cref="TextOverlinePainter.ResolveThickness"/>, public, tested directly). The horizontal/vertical
    /// anchor resolvers are private, so — mirroring how <c>DashedBorderPainterTests</c> never calls
    /// <c>DashedBorderPainter</c>'s private <c>ComputeDashes</c> / <c>ComputeDots</c> directly — they are
    /// exercised only through TryComputeGeometry's own output. Both measurements the function needs (the
    /// text's natural width and the wrapped text block's height) are ordinary parameters supplied by the
    /// caller, not a live <c>MeasureTextSize</c> call, so every case here runs headless with no panel and no
    /// TextElement. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class TextOverlinePainterTests
    {
        private static (bool Ok, Vector2 From, Vector2 To, float LineWidth) Compute(
            Rect contentRect, float measuredWidth, float textBlockHeight, float fontSize, TextAnchor align)
        {
            var ok = TextOverlinePainter.TryComputeGeometry(
                contentRect, measuredWidth, textBlockHeight, fontSize, align, out var from, out var to, out var lineWidth);
            return (ok, from, to, lineWidth);
        }

        [Test]
        public void Given_AnUpperLeftAnchor_When_GeometryComputed_Then_TheRuleStartsAtTheContentBoxLeftEdge()
        {
            // Arrange — a 100-wide content box; a 40-wide run leaves 60 of leftover space.
            var contentRect = new Rect(10f, 20f, 100f, 50f);

            // Act
            var (ok, from, _, _) = Compute(
                contentRect: contentRect, measuredWidth: 40f, textBlockHeight: 20f, fontSize: 16f, align: TextAnchor.UpperLeft);

            // Assert
            Assume.That(ok, Is.True, "Precondition: a well-formed content rect computes geometry");
            Assert.That(from.x, Is.EqualTo(10f));
        }

        [Test]
        public void Given_AnUpperCenterAnchor_When_GeometryComputed_Then_TheRuleIsCenteredInTheContentBox()
        {
            // Arrange — the 60 leftover units (100 - 40) split evenly, 30 on each side of the content box.
            var contentRect = new Rect(10f, 20f, 100f, 50f);

            // Act
            var (ok, from, _, _) = Compute(
                contentRect: contentRect, measuredWidth: 40f, textBlockHeight: 20f, fontSize: 16f, align: TextAnchor.UpperCenter);

            // Assert
            Assume.That(ok, Is.True, "Precondition: a well-formed content rect computes geometry");
            Assert.That(from.x, Is.EqualTo(40f));
        }

        [Test]
        public void Given_AnUpperRightAnchor_When_GeometryComputed_Then_TheRuleEndsAtTheContentBoxRightEdge()
        {
            // Arrange
            var contentRect = new Rect(10f, 20f, 100f, 50f);

            // Act
            var (ok, _, to, _) = Compute(
                contentRect: contentRect, measuredWidth: 40f, textBlockHeight: 20f, fontSize: 16f, align: TextAnchor.UpperRight);

            // Assert
            Assume.That(ok, Is.True, "Precondition: a well-formed content rect computes geometry");
            Assert.That(to.x, Is.EqualTo(110f));
        }

        [Test]
        public void Given_AnUpperAnchor_When_GeometryComputed_Then_TheYBaseIsTheContentBoxTopPlusTheFontNudge()
        {
            // Arrange — Upper* top-aligns the whole text block to the content box, so the first (only, here)
            // line's top IS the content box's own top edge (yMin = 20).
            var contentRect = new Rect(10f, 20f, 100f, 50f);

            // Act
            var (ok, from, _, _) = Compute(
                contentRect: contentRect, measuredWidth: 40f, textBlockHeight: 20f, fontSize: 16f, align: TextAnchor.UpperLeft);

            // Assert — yMin (20) + 15% of the 16px font (2.4).
            Assume.That(ok, Is.True, "Precondition: a well-formed content rect computes geometry");
            Assert.That(from.y, Is.EqualTo(22.4f).Within(1e-3f));
        }

        [Test]
        public void Given_AMiddleAnchor_When_GeometryComputed_Then_TheYBaseIsTheCenteredBlocksTopPlusTheFontNudge()
        {
            // Arrange — a middle anchor centers the 20-tall block in the 50-tall content box (center.y = 45):
            // the block spans 35..55, so the first line's top is 35, not the content box's own top (20).
            var contentRect = new Rect(10f, 20f, 100f, 50f);

            // Act
            var (ok, from, _, _) = Compute(
                contentRect: contentRect, measuredWidth: 40f, textBlockHeight: 20f, fontSize: 16f, align: TextAnchor.MiddleLeft);

            // Assert — 35 + 2.4.
            Assume.That(ok, Is.True, "Precondition: a well-formed content rect computes geometry");
            Assert.That(from.y, Is.EqualTo(37.4f).Within(1e-3f));
        }

        [Test]
        public void Given_ALowerAnchor_When_GeometryComputed_Then_TheYBaseIsTheBottomAlignedBlocksTopPlusTheFontNudge()
        {
            // Arrange — a lower anchor bottom-aligns the 20-tall block to the content box's own bottom (yMax
            // = 70), so the block's (and so the first line's) top is 50 (70 - 20), not the content box's own
            // top (20).
            var contentRect = new Rect(10f, 20f, 100f, 50f);

            // Act
            var (ok, from, _, _) = Compute(
                contentRect: contentRect, measuredWidth: 40f, textBlockHeight: 20f, fontSize: 16f, align: TextAnchor.LowerLeft);

            // Assert — 50 + 2.4.
            Assume.That(ok, Is.True, "Precondition: a well-formed content rect computes geometry");
            Assert.That(from.y, Is.EqualTo(52.4f).Within(1e-3f));
        }

        [Test]
        public void Given_AZeroWidthContentRect_When_GeometryComputed_Then_ItReturnsFalse()
        {
            // Arrange — a degenerate (zero-width) content box, the shape a collapsed layout can produce.
            var contentRect = new Rect(0f, 0f, 0f, 50f);

            // Act
            var (ok, _, _, _) = Compute(
                contentRect: contentRect, measuredWidth: 10f, textBlockHeight: 10f, fontSize: 16f, align: TextAnchor.UpperLeft);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AZeroHeightContentRect_When_GeometryComputed_Then_ItReturnsFalse()
        {
            // Arrange
            var contentRect = new Rect(0f, 0f, 100f, 0f);

            // Act
            var (ok, _, _, _) = Compute(
                contentRect: contentRect, measuredWidth: 10f, textBlockHeight: 10f, fontSize: 16f, align: TextAnchor.UpperLeft);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ANaNHeightContentRect_When_GeometryComputed_Then_ItReturnsFalse()
        {
            // Arrange — a NaN dimension, the shape an unresolved layout read before the first pass settles
            // can produce.
            var contentRect = new Rect(0f, 0f, 100f, float.NaN);

            // Act
            var (ok, _, _, _) = Compute(
                contentRect: contentRect, measuredWidth: 10f, textBlockHeight: 10f, fontSize: 16f, align: TextAnchor.UpperLeft);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_EmptyText_When_GeometryComputed_Then_TheRuleIsAZeroLengthRun()
        {
            // Arrange — an empty run measures to 0 width; the content box itself is still well-formed.
            var contentRect = new Rect(10f, 20f, 100f, 50f);

            // Act
            var (ok, from, to, _) = Compute(
                contentRect: contentRect, measuredWidth: 0f, textBlockHeight: 20f, fontSize: 16f, align: TextAnchor.UpperLeft);

            // Assert
            Assume.That(ok, Is.True, "Precondition: a well-formed content rect still computes geometry for empty text");
            Assert.That(from, Is.EqualTo(to));
        }

        [Test]
        public void Given_ANaturalWidthWiderThanTheContentBox_When_GeometryComputed_Then_TheRuleClampsToTheContentWidth()
        {
            // Arrange — a 150-wide natural measurement against a 100-wide content box.
            var contentRect = new Rect(10f, 20f, 100f, 50f);

            // Act
            var (ok, from, to, _) = Compute(
                contentRect: contentRect, measuredWidth: 150f, textBlockHeight: 20f, fontSize: 16f, align: TextAnchor.UpperLeft);

            // Assert
            Assume.That(ok, Is.True, "Precondition: a well-formed content rect computes geometry");
            Assert.That(to.x - from.x, Is.EqualTo(100f));
        }

        [Test]
        public void Given_ATinyFontSize_When_ThicknessResolved_Then_ItIsFlooredAtOnePixel()
        {
            // Arrange — 4 / 16 = 0.25, below the 1px floor.
            const float fontSize = 4f;

            // Act
            var thickness = TextOverlinePainter.ResolveThickness(fontSize);

            // Assert
            Assert.That(thickness, Is.EqualTo(1f));
        }

        [Test]
        public void Given_A32PixelFontSize_When_ThicknessResolved_Then_ItIsTwoPixels()
        {
            // Arrange — 32 / 16 = 2, above the floor, so the plain ratio applies unmodified.
            const float fontSize = 32f;

            // Act
            var thickness = TextOverlinePainter.ResolveThickness(fontSize);

            // Assert
            Assert.That(thickness, Is.EqualTo(2f));
        }
    }
}
