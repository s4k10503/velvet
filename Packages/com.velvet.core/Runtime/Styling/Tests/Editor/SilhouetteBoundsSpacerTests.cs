using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the pure geometry of the filter bounds-spacer — the sheared silhouette AABB, the shear growth
    /// applied to a shadow quad, the axis-aligned union, and the trailing-spacer child count — alongside two
    /// related silhouette-layer contracts: <see cref="SilhouetteFaceStash"/>'s <c>includeBackground</c> flag
    /// (the skew / drop-shadow layers own the whole face and suppress both background and border, while the
    /// border-dashed layer restyles only the border and must never touch the background), and that the skew
    /// silhouette's suppression-sentinel test is BIT-EXACT rather than Unity's approximate <c>Color</c> equality
    /// (epsilon ~1e-5), so a genuine authored color that merely lands in the narrow band around the sentinel is
    /// not misclassified. GWT, one assert each; these need no panel.
    /// </summary>
    [TestFixture]
    internal sealed class SilhouetteBoundsSpacerTests
    {
        [Test]
        public void Given_ASkewX_When_AabbComputed_Then_ItGrowsWidthByHeightTimesTan()
        {
            // Arrange — a 100x40 box sheared on X by tan = 0.5.
            // Act
            var aabb = SilhouetteBoundsSpacer.ShearedAabb(100f, 40f, 0.5f, 0f);

            // Assert — width grows by h*|tanX| = 40*0.5 = 20 (10 each side).
            Assert.That(aabb.width, Is.EqualTo(120f).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewX_When_AabbComputed_Then_ItOverhangsLeftByHalfHeightTimesTan()
        {
            // Act
            var aabb = SilhouetteBoundsSpacer.ShearedAabb(100f, 40f, 0.5f, 0f);

            // Assert — the left overhang is -(h/2)*|tanX| = -10.
            Assert.That(aabb.xMin, Is.EqualTo(-10f).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewY_When_AabbComputed_Then_ItGrowsHeightByWidthTimesTan()
        {
            // Act
            var aabb = SilhouetteBoundsSpacer.ShearedAabb(100f, 40f, 0f, 0.25f);

            // Assert — height grows by w*|tanY| = 100*0.25 = 25.
            Assert.That(aabb.height, Is.EqualTo(65f).Within(1e-4f));
        }

        [Test]
        public void Given_NoSkew_When_AabbComputed_Then_ItIsExactlyTheBox()
        {
            // Act
            var aabb = SilhouetteBoundsSpacer.ShearedAabb(100f, 40f, 0f, 0f);

            // Assert
            Assert.That(aabb, Is.EqualTo(new Rect(0f, 0f, 100f, 40f)));
        }

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
        public void Given_ContainerWithTrailingSpacer_When_Counted_Then_TheSpacerIsExcluded()
        {
            // Arrange — two rendered children then a spacer.
            var container = new VisualElement();
            container.Add(new VisualElement());
            container.Add(new VisualElement());
            var spacer = new VisualElement();
            spacer.AddToClassList(SilhouetteBoundsSpacer.MarkerClass);
            container.Add(spacer);

            // Act / Assert
            Assert.That(SilhouetteBoundsSpacer.NonSpacerChildCount(container), Is.EqualTo(2));
        }

        [Test]
        public void Given_ContainerWithTwoTrailingSpacers_When_Counted_Then_BothAreExcluded()
        {
            // Arrange — one rendered child then two spacers (a skewed + shadowed caster).
            var container = new VisualElement();
            container.Add(new VisualElement());
            for (var i = 0; i < 2; i++)
            {
                var s = new VisualElement();
                s.AddToClassList(SilhouetteBoundsSpacer.MarkerClass);
                container.Add(s);
            }

            // Act / Assert
            Assert.That(SilhouetteBoundsSpacer.NonSpacerChildCount(container), Is.EqualTo(1));
        }

        [Test]
        public void Given_ALeadingBackZLayerContainerIsMomentarilyTheOnlyOtherChildBesidesATrailingSpacer_When_Counted_Then_TheBackContainerIsNotAlsoTrimmedAwayAsASpacer()
        {
            // Arrange — a leading back z-layer container (FiberZLayerCoordinator's own convention: the ONE
            // reconciler-invisible child ever placed leading, never trailing) immediately followed by a
            // trailing bounds-spacer, with no ordinary child between them — the exact transient shape the
            // leading floor guards against (every ordinary sibling's own placeholder momentarily absent).
            // IsSpacer recognizes BOTH kinds of reconciler-invisible child, so an unguarded trailing trim would
            // walk straight through the back container too and return 0, undercounting it out of the physical
            // range entirely (a later ordinary insert clamped against that undercount would land BEFORE the
            // back container instead of after it, permanently misplacing it).
            var container = new VisualElement();
            var back = new VisualElement();
            back.AddToClassList(FiberZLayerCoordinator.BackMarkerClass);
            container.Add(back);
            var spacer = new VisualElement();
            spacer.AddToClassList(SilhouetteBoundsSpacer.MarkerClass);
            container.Add(spacer);

            // Act / Assert — only the trailing spacer is excluded; the back container itself still counts.
            Assert.That(SilhouetteBoundsSpacer.NonSpacerChildCount(container), Is.EqualTo(1));
        }

        [Test]
        public void Given_ABackContainerAndAFrontContainerAndATrailingBoundsSpacerAllCoexist_When_Counted_Then_OnlyTheTwoTrailingReconcilerInvisibleChildrenAreExcluded()
        {
            // Arrange — the full 3-way combo a stacking parent carrying both z-layer containers AND a
            // filtered caster's own bounds-spacer can reach simultaneously: a leading back container (never
            // trimmed — it stays counted by its own contract), one ordinary rendered child, then the two
            // TRAILING reconciler-invisible children (front container, bounds-spacer), deliberately in THIS
            // relative order since GetOrCreateContainer's own comment notes the two trailing spacers
            // tolerate either order between them.
            var container = new VisualElement();
            var back = new VisualElement();
            back.AddToClassList(FiberZLayerCoordinator.BackMarkerClass);
            container.Add(back);
            container.Add(new VisualElement());
            var front = new VisualElement();
            front.AddToClassList(FiberZLayerCoordinator.FrontMarkerClass);
            container.Add(front);
            var spacer = new VisualElement();
            spacer.AddToClassList(SilhouetteBoundsSpacer.MarkerClass);
            container.Add(spacer);

            // Act / Assert — the back container and the ordinary child stay counted; both trailing
            // reconciler-invisible children (front container, bounds-spacer) are excluded: 2 total.
            Assert.That(SilhouetteBoundsSpacer.NonSpacerChildCount(container), Is.EqualTo(2));
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
            // Arrange — the default (includeBackground:true) stash pins SkewBinding / DropShadowBinding's
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

        [Test]
        public void Given_AColorInTheEpsilonBandAroundTheSentinel_When_Tested_Then_ItIsNotTheSentinel()
        {
            // Arrange — a real authored color a hair off the sentinel alpha, inside Unity's approximate == band.
            var sentinel = SkewSilhouette.SuppressedColor;
            var nearButReal = new Color(sentinel.r, sentinel.g, sentinel.b, sentinel.a + 5e-6f);
            Assume.That(nearButReal == sentinel, Is.True,
                "Precondition: Unity's approximate == misreads this near color as the sentinel");

            // Act / Assert — the bit-exact test keeps it as a real color.
            Assert.That(SkewSilhouette.IsSentinel(nearButReal), Is.False);
        }

        [Test]
        public void Given_TheExactSentinelColor_When_Tested_Then_ItIsTheSentinel()
        {
            // Arrange/Act/Assert — the real suppression write must still be recognized.
            Assert.That(SkewSilhouette.IsSentinel(SkewSilhouette.SuppressedColor), Is.True);
        }
    }
}
