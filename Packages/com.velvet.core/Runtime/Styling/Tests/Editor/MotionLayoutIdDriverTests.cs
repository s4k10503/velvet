using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="MotionLayoutIdDriver.ComputeDeltaPlan"/>: the pure old-rect/new-rect → SpringPlan
    /// math behind V.Motion's layoutId (Framer's shared-element layout animation parity). Panel-free by
    /// design, mirroring <c>MotionSpringDriverTests</c>' own rationale — this resolves numeric deltas
    /// directly from two Rects, with no style resolution or scheduler tick involved.
    /// </summary>
    [TestFixture]
    internal sealed class MotionLayoutIdDriverTests
    {
        [Test]
        public void Given_TwoIdenticalRects_When_DeltaComputed_Then_ThePlanIsEmpty()
        {
            // Arrange
            var rect = new Rect(10f, 20f, 100f, 50f);

            // Act
            var plan = MotionLayoutIdDriver.ComputeDeltaPlan(rect, rect);

            // Assert
            Assert.That(plan.IsEmpty, Is.True);
        }

        [Test]
        public void Given_ARectMovedWithoutResizing_When_DeltaComputed_Then_OnlyTranslateChannelsAreSet()
        {
            // Arrange — moved from (10,20) to (110,220), same 100x50 size.
            var oldRect = new Rect(10f, 20f, 100f, 50f);
            var newRect = new Rect(110f, 220f, 100f, 50f);

            // Act
            var plan = MotionLayoutIdDriver.ComputeDeltaPlan(oldRect, newRect);
            (float, float)? expectedX = (-100f, 0f);
            (float, float)? expectedY = (-200f, 0f);
            (float, float)? expectedScale = null;

            // Assert — TranslateX/Y carry the OLD-minus-NEW delta (the inverse offset to apply immediately,
            // animating back toward 0), Scale stays unset (no size change).
            Assert.That((plan.TranslateX, plan.TranslateY, plan.Scale), Is.EqualTo((expectedX, expectedY, expectedScale)));
        }

        [Test]
        public void Given_ARectResizedWithoutMoving_When_DeltaComputed_Then_OnlyTheScaleChannelIsSet()
        {
            // Arrange — grew from 100x100 to 200x200 (uniform 2x), position unchanged.
            var oldRect = new Rect(0f, 0f, 100f, 100f);
            var newRect = new Rect(0f, 0f, 200f, 200f);

            // Act
            var plan = MotionLayoutIdDriver.ComputeDeltaPlan(oldRect, newRect);
            (float, float)? expectedTranslateX = null;
            (float, float)? expectedScale = (0.5f, 1f);

            // Assert — Scale's "from" is oldSize/newSize (0.5: the inverse pose to start from, since the
            // element is now visually twice as big and must start scaled down to 0.5 before springing to 1).
            Assert.That((plan.TranslateX, plan.Scale), Is.EqualTo((expectedTranslateX, expectedScale)));
        }

        [Test]
        public void Given_ANonUniformResize_When_DeltaComputed_Then_TheScaleFactorIsTheAverageOfBothAxes()
        {
            // Arrange — width unchanged (ratio 1), height doubled (ratio 0.5) — averages to 0.75.
            var oldRect = new Rect(0f, 0f, 100f, 100f);
            var newRect = new Rect(0f, 0f, 100f, 200f);

            // Act
            var plan = MotionLayoutIdDriver.ComputeDeltaPlan(oldRect, newRect);
            (float, float)? expectedScale = (0.75f, 1f);

            // Assert
            Assert.That(plan.Scale, Is.EqualTo(expectedScale));
        }
    }
}
