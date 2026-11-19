using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    internal sealed class SkewTransformFactorTests
    {
        [TestCase(30f, 0f, TestName = "Given_ASkewOnX_When_Factored_Then_TheTransformCarriesTheShear")]
        [TestCase(12f, 6f, TestName = "Given_ASkewOnBothAxes_When_Factored_Then_TheTransformCarriesTheShear")]
        [TestCase(-12f, 6f, TestName = "Given_AnOppositeSkewOnBothAxes_When_Factored_Then_TheTransformCarriesTheShear")]
        public void Given_ASkew_When_Factored_Then_TheTransformCarriesTheShear(float xDeg, float yDeg)
        {
            // Arrange — skewX(x) skewY(y) as the matrix [[1 + tx ty, tx], [ty, 1]].
            var tx = Mathf.Tan(xDeg * Mathf.Deg2Rad);
            var ty = Mathf.Tan(yDeg * Mathf.Deg2Rad);

            // Act
            var ok = SkewTransformFactor.TryFactor(1f + (tx * ty), tx, ty, 1f, out var rotation, out var scale);
            var carried = Matrix4x4.Rotate(rotation);

            // Assert
            Assert.That(
                ok ? new[] { scale.x * carried.m00, scale.y * carried.m01, scale.x * carried.m10, scale.y * carried.m11 }
                    : new[] { float.NaN },
                Is.EqualTo(new[] { 1f + (tx * ty), tx, ty, 1f }).Within(1e-4f));
        }

        // M = rotate(theta) * scale(sx, sy) * skewX(x) skewY(y), the order CSS applies the individual `rotate` and
        // `scale` properties around `transform`.
        [TestCase(30f, 1f, 1f, 12f, 0f, TestName = "Given_ARotateAndASkew_When_Factored_Then_TheSlotsCarryTheComposite")]
        [TestCase(0f, 1.5f, 1.5f, -12f, 6f, TestName = "Given_AUniformScaleAndASkew_When_Factored_Then_TheSlotsCarryTheComposite")]
        [TestCase(-40f, 2f, 0.5f, 12f, 6f, TestName = "Given_ANonUniformScaleARotateAndASkew_When_Factored_Then_TheSlotsCarryTheComposite")]
        [TestCase(0f, 1f, -1f, 12f, 6f, TestName = "Given_ANegativeScaleOnYAndASkew_When_Factored_Then_TheSlotsCarryTheComposite")]
        [TestCase(25f, -2f, 1f, -20f, 0f, TestName = "Given_ANegativeScaleOnXWithARotateAndASkew_When_Factored_Then_TheSlotsCarryTheComposite")]
        public void Given_ARotateScaleAndSkew_When_Factored_Then_TheSlotsCarryTheComposite(
            float rotateDeg, float scaleX, float scaleY, float xDeg, float yDeg)
        {
            // Arrange
            var tx = Mathf.Tan(xDeg * Mathf.Deg2Rad);
            var ty = Mathf.Tan(yDeg * Mathf.Deg2Rad);
            var cos = Mathf.Cos(rotateDeg * Mathf.Deg2Rad);
            var sin = Mathf.Sin(rotateDeg * Mathf.Deg2Rad);
            // rotate * scale
            var p00 = cos * scaleX;
            var p01 = -sin * scaleY;
            var p10 = sin * scaleX;
            var p11 = cos * scaleY;
            // ... * skewX(x) skewY(y), which is [[1 + tx ty, tx], [ty, 1]]
            var a = (p00 * (1f + (tx * ty))) + (p01 * ty);
            var b = (p00 * tx) + p01;
            var c = (p10 * (1f + (tx * ty))) + (p11 * ty);
            var d = (p10 * tx) + p11;

            // Act
            var ok = SkewTransformFactor.TryFactor(a, b, c, d, out var rotation, out var scale);
            var carried = Matrix4x4.Rotate(rotation);

            // Assert
            Assert.That(
                ok
                    ? new[]
                    {
                        scale.x * carried.m00, scale.y * carried.m01, scale.x * carried.m10, scale.y * carried.m11,
                    }
                    : new[] { float.NaN },
                Is.EqualTo(new[] { a, b, c, d }).Within(1e-4f));
        }

        [Test]
        public void Given_AMatrixThatCollapsesThePlane_When_Factored_Then_NoSlotsAreOffered()
        {
            // Arrange — scale 0 on y leaves the matrix with no area.

            // Act
            var ok = SkewTransformFactor.TryFactor(1f, 0.2f, 0f, 0f, out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }
    }
}
