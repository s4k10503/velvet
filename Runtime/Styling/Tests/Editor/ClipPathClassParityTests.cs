using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>clip-path-[…]</c> utility parser (<see cref="StyleClipPathClass"/>): the
    /// arbitrary-value convention (underscores stand in for spaces), the CSS
    /// <c>&lt;basic-shape&gt;</c> grammar subset (<c>polygon</c> / <c>circle</c> / <c>ellipse</c> /
    /// <c>inset</c>), cascade behavior (last recognized class wins; <c>clip-path-none</c> overrides),
    /// and rejection of unparseable values. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ClipPathClassParityTests
    {
        // Gates

        [Test]
        public void Given_NoClipClass_When_Gated_Then_HasClipPathClassIsFalse()
        {
            // Arrange
            var classes = new[] { "w-full", "rounded-lg", "shadow-lg" };

            // Act / Assert
            Assert.That(StyleClipPathClass.HasClipPathClass(classes), Is.False);
        }

        [Test]
        public void Given_AClipClass_When_Gated_Then_HasClipPathClassIsTrue()
        {
            // Arrange
            var classes = new[] { "w-full", "clip-path-[circle(50%)]" };

            // Act / Assert
            Assert.That(StyleClipPathClass.HasClipPathClass(classes), Is.True);
        }

        [Test]
        public void Given_ABaseClip_When_WrapGateChecked_Then_WantsWrapper()
        {
            Assert.That(StyleClipPathClass.WantsClipWrapper(new[] { "clip-path-[circle(50%)]" }), Is.True);
        }

        [Test]
        public void Given_OnlyClipPathNone_When_WrapGateChecked_Then_DoesNotWantWrapper()
        {
            // clip-path-none resolves to no clip, so it must not force a wrapper by itself.
            Assert.That(StyleClipPathClass.WantsClipWrapper(new[] { "clip-path-none" }), Is.False);
        }

        [Test]
        public void Given_AHoverClip_When_WrapGateChecked_Then_WantsWrapper()
        {
            // A variant clip needs the wrapper up-front (so the hover shape can light up without wrap-on-event).
            Assert.That(StyleClipPathClass.WantsClipWrapper(new[] { "hover:clip-path-[circle(50%)]" }), Is.True);
        }

        [Test]
        public void Given_OnlyHoverClipPathNone_When_WrapGateChecked_Then_DoesNotWantWrapper()
        {
            // A clip-path-none variant payload only CLEARS a clip; with no active clip anywhere, no wrapper.
            Assert.That(StyleClipPathClass.WantsClipWrapper(new[] { "hover:clip-path-none" }), Is.False);
        }

        [Test]
        public void Given_AStackedHoverClip_When_WrapGateChecked_Then_WantsWrapper()
        {
            // A STACKED variant (dark:hover:clip-path-[…]) must be peeled to the leaf clip, else it would
            // silently never wrap (and so never clip).
            Assert.That(StyleClipPathClass.WantsClipWrapper(new[] { "dark:hover:clip-path-[circle(50%)]" }), Is.True);
        }

        // polygon()

        [Test]
        public void Given_ATrianglePolygon_When_Extracted_Then_KindIsPolygon()
        {
            // Arrange
            var classes = new[] { "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found && spec.Kind == ClipPathKind.Polygon, Is.True);
        }

        [Test]
        public void Given_ATrianglePolygon_When_Extracted_Then_ThreePointPairsAreParsed()
        {
            // Arrange
            var classes = new[] { "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert: x,y interleaved ⇒ 6 entries for 3 vertices.
            Assert.That(spec.PolygonPoints.Length, Is.EqualTo(6));
        }

        [Test]
        public void Given_UnderscoreSeparatedValues_When_Extracted_Then_PercentValueIsParsed()
        {
            // Arrange: underscores stand in for the spaces of `polygon(50% 0%, …)` (arbitrary-value convention).
            var classes = new[] { "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert: first vertex x is 50%.
            Assert.That(spec.PolygonPoints[0].IsPercent && spec.PolygonPoints[0].Value == 50f, Is.True);
        }

        [Test]
        public void Given_AnEvenOddPolygon_When_Extracted_Then_FillRuleIsOddEven()
        {
            // Arrange
            var classes = new[] { "clip-path-[polygon(evenodd,50%_0%,100%_100%,0%_100%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.FillRule, Is.EqualTo(FillRule.OddEven));
        }

        [Test]
        public void Given_ATwoPointPolygon_When_Extracted_Then_NotRecognized()
        {
            // Arrange: CSS polygon() needs at least 3 vertices.
            var classes = new[] { "clip-path-[polygon(0%_0%,100%_100%)]" };

            // Act / Assert
            Assert.That(StyleClipPathClass.TryExtract(classes, out _), Is.False);
        }

        // circle()

        [Test]
        public void Given_ACircleWithoutRadius_When_Extracted_Then_ExtentDefaultsToClosestSide()
        {
            // Arrange
            var classes = new[] { "clip-path-[circle()]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.RadiusXExtent, Is.EqualTo(ClipPathExtent.ClosestSide));
        }

        [Test]
        public void Given_ACircleWithPxRadius_When_Extracted_Then_RadiusIsLengthInPx()
        {
            // Arrange
            var classes = new[] { "clip-path-[circle(40px)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.RadiusXExtent == ClipPathExtent.Length
                && !spec.RadiusX.IsPercent && spec.RadiusX.Value == 40f, Is.True);
        }

        [Test]
        public void Given_ACircleAtRightBottom_When_Extracted_Then_CenterFollowsKeywords()
        {
            // Arrange
            var classes = new[] { "clip-path-[circle(50%_at_right_bottom)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert: right ⇒ x 100%, bottom ⇒ y 100%.
            Assert.That(spec.CenterX.Value == 100f && spec.CenterY.Value == 100f, Is.True);
        }

        [Test]
        public void Given_ACircleWithoutPosition_When_Extracted_Then_CenterDefaultsTo50Percent()
        {
            // Arrange
            var classes = new[] { "clip-path-[circle(50%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.CenterX.IsPercent && spec.CenterX.Value == 50f, Is.True);
        }

        // ellipse()

        [Test]
        public void Given_AnEllipseWithRadii_When_Extracted_Then_RadiiResolvePerAxis()
        {
            // Arrange
            var classes = new[] { "clip-path-[ellipse(50%_35%_at_50%_50%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.RadiusX.Value == 50f && spec.RadiusY.Value == 35f, Is.True);
        }

        // inset()

        [Test]
        public void Given_ATwoValueInset_When_Extracted_Then_ShorthandExpandsLikeCss()
        {
            // Arrange: inset(10px 20%) ⇒ top/bottom 10px, right/left 20%.
            var classes = new[] { "clip-path-[inset(10px_20%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.InsetLeft.IsPercent && spec.InsetLeft.Value == 20f
                && !spec.InsetBottom.IsPercent && spec.InsetBottom.Value == 10f, Is.True);
        }

        [Test]
        public void Given_AnInsetWithRound_When_Extracted_Then_CornerRadiiAreExpanded()
        {
            // Arrange: round 8px 16px ⇒ tl/br 8, tr/bl 16.
            var classes = new[] { "clip-path-[inset(0px_round_8px_16px)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.CornerRadii[0].Value == 8f && spec.CornerRadii[1].Value == 16f
                && spec.CornerRadii[2].Value == 8f && spec.CornerRadii[3].Value == 16f, Is.True);
        }

        [Test]
        public void Given_AnInsetWithoutRound_When_Extracted_Then_CornerRadiiAreNull()
        {
            // Arrange
            var classes = new[] { "clip-path-[inset(10px)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.CornerRadii, Is.Null);
        }

        // Stretch invariance (the geometry sync's rescale-instead-of-rebake fast path)

        [Test]
        public void Given_AnAllPercentPolygon_When_Extracted_Then_ItIsStretchInvariant()
        {
            // Arrange
            var classes = new[] { "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.StretchInvariant, Is.True);
        }

        [Test]
        public void Given_APolygonWithAPixelCoordinate_When_Extracted_Then_ItIsNotStretchInvariant()
        {
            // Arrange: one px coordinate pins the shape to absolute pixels.
            var classes = new[] { "clip-path-[polygon(50%_0px,100%_100%,0%_100%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.StretchInvariant, Is.False);
        }

        [Test]
        public void Given_ACircle_When_Extracted_Then_ItIsNeverStretchInvariant()
        {
            // Arrange: circle() % radii resolve against the diagonal — not per-axis linear.
            var classes = new[] { "clip-path-[circle(50%)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.StretchInvariant, Is.False);
        }

        // Cascade

        [Test]
        public void Given_TwoClipClasses_When_Extracted_Then_TheLastOneWins()
        {
            // Arrange
            var classes = new[] { "clip-path-[circle(50%)]", "clip-path-[inset(10px)]" };

            // Act
            var found = StyleClipPathClass.TryExtract(classes, out var spec);
            Assume.That(found, Is.True);

            // Assert
            Assert.That(spec.Kind, Is.EqualTo(ClipPathKind.Inset));
        }

        [Test]
        public void Given_AClipFollowedByNone_When_Extracted_Then_NoClipIsWanted()
        {
            // Arrange: clip-path-none overrides the earlier clip in the cascade.
            var classes = new[] { "clip-path-[circle(50%)]", "clip-path-none" };

            // Act / Assert
            Assert.That(StyleClipPathClass.TryExtract(classes, out _), Is.False);
        }

        // Rejection

        [Test]
        public void Given_AnUnknownShapeFunction_When_Extracted_Then_NotRecognized()
        {
            // Arrange: path() is not in the supported subset.
            var classes = new[] { "clip-path-[path(M0_0L10_10)]" };

            // Act / Assert
            Assert.That(StyleClipPathClass.TryExtract(classes, out _), Is.False);
        }

        [Test]
        public void Given_AMalformedValue_When_Extracted_Then_NotRecognized()
        {
            // Arrange
            var classes = new[] { "clip-path-[circle(abc)]" };

            // Act / Assert
            Assert.That(StyleClipPathClass.TryExtract(classes, out _), Is.False);
        }
    }
}
