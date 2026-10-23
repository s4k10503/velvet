using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the pure geometry of the filter bounds-spacer — the axis-aligned union, the growth a shadow quad takes
    /// from a shear and the border inset —
    /// alongside two related silhouette-layer contracts: <see cref="SilhouetteFaceStash"/>'s
    /// <c>includeBackground</c> flag (the drop-shadow layer owns the whole face and suppresses both background and
    /// border, while the border-dashed layer restyles only the border and must never touch the background), and that
    /// the suppression-sentinel test is BIT-EXACT rather than Unity's approximate <c>Color</c> equality
    /// (epsilon ~1e-5), so a genuine authored color that merely lands in the narrow band around the sentinel is
    /// not misclassified. GWT, one assert each; these need no panel.
    /// </summary>
    [TestFixture]
    internal sealed class SilhouetteBoundsSpacerTests
    {
        [Test]
        public void Given_TwoRects_When_Unioned_Then_TheResultEnclosesBoth()
        {
            // Arrange — the box and a shadow quad shifted up-left and larger.
            var box = new Rect(0f, 0f, 100f, 40f);
            var quad = new Rect(-30f, -20f, 160f, 100f);

            // Act
            var u = SilhouetteBoundsSpacer.Union(box, quad);

            // Assert — the union spans from the quad's min to its max (it encloses the box).
            Assert.That(u, Is.EqualTo(new Rect(-30f, -20f, 160f, 100f)));
        }

        [Test]
        public void Given_ARect_When_ExpandedForShear_Then_ItGrowsByItsOwnExtentTimesTan()
        {
            // Arrange — a 100x40 rect at origin, tan = 0.5 on X.
            var r = new Rect(0f, 0f, 100f, 40f);

            // Act
            var e = SilhouetteBoundsSpacer.ExpandForShear(r, 0.5f, 0f);

            // Assert — grows width by height*|tanX| = 20.
            Assert.That(e.width, Is.EqualTo(120f).Within(1e-4f));
        }

        [Test]
        public void Given_ARectShearedOnBothAxes_When_ExpandedForShear_Then_ItCoversTheSkewXSkewYProduct()
        {
            // Arrange — skewX(θx) skewY(θy) carries a corner (±50, ±20) about the centre to
            // ((1 + tanX·tanY)·dx + tanX·dy, tanY·dx + dy): half-extents 1.125·50 + 0.5·20 and 0.25·50 + 20.
            var r = new Rect(0f, 0f, 100f, 40f);

            // Act
            var e = SilhouetteBoundsSpacer.ExpandForShear(r, 0.5f, 0.25f);

            // Assert
            Assert.That(new[] { e.xMin, e.yMin, e.width, e.height },
                Is.EqualTo(new[] { -16.25f, -12.5f, 132.5f, 65f }).Within(1e-4f));
        }

        [Test]
        public void Given_AllSidesScaleBorder_When_InsetParsed_Then_LeftMatchesTheScale()
        {
            SilhouetteBoundsSpacer.BorderInset(new[] { "border-8" }, out var left, out _);

            Assert.That(left, Is.EqualTo(8f));
        }

        [Test]
        public void Given_BareBorder_When_InsetParsed_Then_TopIsOne()
        {
            SilhouetteBoundsSpacer.BorderInset(new[] { "border" }, out _, out var top);

            Assert.That(top, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ALeftOnlyBorder_When_InsetParsed_Then_TopStaysZero()
        {
            SilhouetteBoundsSpacer.BorderInset(new[] { "border-l-4" }, out _, out var top);

            Assert.That(top, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AnXAxisBorder_When_InsetParsed_Then_LeftIsInset()
        {
            // border-x sets the left (and right) border; only left matters for the top-left origin.
            SilhouetteBoundsSpacer.BorderInset(new[] { "border-x-4" }, out var left, out _);

            Assert.That(left, Is.EqualTo(4f));
        }

        [Test]
        public void Given_ABorderColorClass_When_InsetParsed_Then_ItContributesNoInset()
        {
            // border-red-500 is a color, not a width — it must not be read as an inset.
            SilhouetteBoundsSpacer.BorderInset(new[] { "border-red-500" }, out var left, out var top);

            Assert.That(left + top, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AVariantCarriedBorder_When_InsetParsed_Then_ItIsStillReserved()
        {
            // A state border (hover:border-8) applies outside the reconcile, so it must be reserved for.
            SilhouetteBoundsSpacer.BorderInset(new[] { "hover:border-8" }, out var left, out _);

            Assert.That(left, Is.EqualTo(8f));
        }

        [Test]
        public void Given_AnArbitraryBorder_When_InsetParsed_Then_ThePixelValueIsRead()
        {
            SilhouetteBoundsSpacer.BorderInset(new[] { "border-[16px]" }, out var left, out _);

            Assert.That(left, Is.EqualTo(16f));
        }

        [Test]
        public void Given_ConflictingBorders_When_InsetParsed_Then_TheMaxIsTaken()
        {
            // Over-covering is invisible, so the widest left-affecting class wins rather than the cascade.
            SilhouetteBoundsSpacer.BorderInset(new[] { "border-2", "border-l-8" }, out var left, out _);

            Assert.That(left, Is.EqualTo(8f));
        }

        [Test]
        public void Given_AnImportantBorder_When_InsetParsed_Then_TheBangIsStrippedAndReserved()
        {
            SilhouetteBoundsSpacer.BorderInset(new[] { "border-8!" }, out var left, out _);

            Assert.That(left, Is.EqualTo(8f));
        }

        [Test]
        public void Given_AnImportantVariantBorderWithItsBangBehindTheVariant_When_InsetParsed_Then_ItIsReserved()
        {
            // Act
            SilhouetteBoundsSpacer.BorderInset(new[] { "hover:!border-8" }, out var left, out _);

            // Assert
            Assert.That(left, Is.EqualTo(8f));
        }

        [Test]
        public void Given_AnArbitraryRemBorder_When_InsetParsed_Then_ItConvertsAtSixteenPxPerRem()
        {
            SilhouetteBoundsSpacer.BorderInset(new[] { "border-[2rem]" }, out var left, out _);

            Assert.That(left, Is.EqualTo(32f));
        }

        [Test]
        public void Given_APaintAabb_When_ShiftedToPaddingBox_Then_TheOriginInsetsByTheBorder()
        {
            var shifted = SilhouetteBoundsSpacer.ShiftToPaddingBox(new Rect(-10f, -6f, 120f, 60f), 8f, 4f);

            Assert.That(shifted.xMin, Is.EqualTo(-18f).Within(1e-4f));
        }

        [Test]
        public void Given_APaintAabb_When_ShiftedToPaddingBox_Then_TheSizeIsUnchanged()
        {
            var shifted = SilhouetteBoundsSpacer.ShiftToPaddingBox(new Rect(-10f, -6f, 120f, 60f), 8f, 4f);

            Assert.That(shifted.width, Is.EqualTo(120f).Within(1e-4f));
        }

        private static readonly Color Fill = new(0.2f, 0.4f, 0.6f, 1f);
        private static readonly Color Border = new(1f, 0f, 0f, 1f);

        private static VisualElement ColoredElement()
        {
            var element = new VisualElement();
            element.style.backgroundColor = Fill;
            element.style.borderLeftColor = Border;
            return element;
        }

        [Test]
        public void Given_ABorderOnlyStash_When_Stashed_Then_TheBackgroundIsUntouched()
        {
            // Arrange — a border-only stash (the border-dashed layer's shape).
            var element = ColoredElement();
            var stash = new SilhouetteFaceStash(includeBackground: false);

            // Act — off-panel with an inline border, TryStash captures + suppresses the border color only.
            stash.TryStash(element);

            // Assert — the authored fill survives (a dashed outline composes with the background, never hides it).
            Assert.That(element.style.backgroundColor.value, Is.EqualTo(Fill));
        }

        [Test]
        public void Given_ABorderOnlyStash_When_Stashed_Then_TheBorderColorIsSuppressed()
        {
            // Arrange
            var element = ColoredElement();
            var stash = new SilhouetteFaceStash(includeBackground: false);

            // Act
            stash.TryStash(element);

            // Assert — the native border color is masked with the sentinel so only our repaint shows.
            Assert.That(SilhouetteFace.IsSentinel(element.style.borderLeftColor.value), Is.True);
        }

        [Test]
        public void Given_ADefaultStash_When_Stashed_Then_BothBackgroundAndBorderAreSuppressed()
        {
            // Arrange — the default (includeBackground:true) stash pins BoxShadowBinding's
            // existing behavior: it owns the whole face.
            var element = ColoredElement();
            var stash = new SilhouetteFaceStash();

            // Act
            stash.TryStash(element);

            // Assert
            Assert.That(
                (SilhouetteFace.IsSentinel(element.style.backgroundColor.value),
                    SilhouetteFace.IsSentinel(element.style.borderLeftColor.value)),
                Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its shear-growth cases and this one reads none of them.
        [Test]
        public void Given_AColorInTheEpsilonBandAroundTheSentinel_When_Tested_Then_ItIsNotTheSentinel()
        {
            // Arrange — a real authored color a hair off the sentinel alpha, inside Unity's approximate == band.
            var sentinel = SilhouetteFace.SuppressedColor;
            var nearButReal = new Color(sentinel.r, sentinel.g, sentinel.b, sentinel.a + 5e-6f);
            Assume.That(nearButReal == sentinel, Is.True,
                "Precondition: Unity's approximate == misreads this near color as the sentinel");

            // Act / Assert — the bit-exact test keeps it as a real color.
            Assert.That(SilhouetteFace.IsSentinel(nearButReal), Is.False);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its shear-growth cases and this one reads none of them.
        [Test]
        public void Given_TheExactSentinelColor_When_Tested_Then_ItIsTheSentinel()
        {
            // Arrange/Act/Assert — the real suppression write must still be recognized.
            Assert.That(SilhouetteFace.IsSentinel(SilhouetteFace.SuppressedColor), Is.True);
        }
    }
}
