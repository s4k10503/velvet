using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the pure centroid-shear math behind <see cref="StyleSkewChildTranslateManipulator"/>: the
    /// approximate descendant-shear offset a skewed caster writes onto a direct child. The model is the one
    /// <c>SilhouetteBoundsSpacer.ShearedAabb</c> documents — <c>x' = x + (y - h/2)*tanX</c>,
    /// <c>y' = y + (x - w/2)*tanY</c> — so a child ABOVE the box centre leans one way under positive
    /// <c>skewX</c> and a child BELOW it the other, the axes never cross-couple, and a child exactly at the
    /// centre (or a zero angle) gets no offset. Panel-free and layout-free: it drives
    /// <see cref="StyleSkewChildTranslateManipulator.ComputeOffset"/> directly. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class SkewChildTranslateOffsetTests
    {
        [Test]
        public void Given_ChildBelowContainerCenterUnderPositiveSkewX_When_ComputingOffset_Then_DxIsPositive()
        {
            // Arrange — a child whose centroid is below the box centre (centerY > h/2) under positive skewX.
            const float w = 100f, h = 100f, tanX = 0.2f, tanY = 0f;
            var childCenterY = (h * 0.5f) + 20f;

            // Act
            var offset = StyleSkewChildTranslateManipulator.ComputeOffset(w * 0.5f, childCenterY, w, h, tanX, tanY);

            // Assert — matching SkewSpec's sign convention (positive X shifts the bottom edge right).
            Assert.That(offset.x, Is.GreaterThan(0f));
        }

        [Test]
        public void Given_ChildAtExactContainerCenter_When_ComputingOffset_Then_OffsetIsZero()
        {
            // Arrange — the centroid sits exactly at the box centre with both angles non-zero.
            const float w = 120f, h = 80f, tanX = 0.3f, tanY = 0.15f;

            // Act
            var offset = StyleSkewChildTranslateManipulator.ComputeOffset(w * 0.5f, h * 0.5f, w, h, tanX, tanY);

            // Assert — the shear leaves the exact centroid fixed (the one point the approximation is exact at).
            Assert.That(offset, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Given_ZeroSkewAngles_When_ComputingOffset_Then_OffsetIsZeroRegardlessOfChildPosition()
        {
            // Arrange — an off-centre child but no shear on either axis.
            const float w = 200f, h = 60f, tanX = 0f, tanY = 0f;

            // Act
            var offset = StyleSkewChildTranslateManipulator.ComputeOffset(10f, 5f, w, h, tanX, tanY);

            // Assert — an unskewed frame seats every child at its own position (the post-unskew numeric path).
            Assert.That(offset, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Given_SkewYOnly_When_ComputingOffset_Then_DxStaysZeroWhileDyTracksChildCenterX()
        {
            // Arrange — skewY only, on a child left of the box centre (centerX < w/2).
            const float w = 100f, h = 100f, tanX = 0f, tanY = 0.25f;
            var childCenterX = (w * 0.5f) - 40f;

            // Act
            var offset = StyleSkewChildTranslateManipulator.ComputeOffset(childCenterX, h * 0.5f, w, h, tanX, tanY);

            // Assert — no cross term: skewY moves only dy (here negative for a left-of-centre child), never dx.
            Assert.That(offset, Is.EqualTo(new Vector2(0f, -10f)));
        }
    }
}
