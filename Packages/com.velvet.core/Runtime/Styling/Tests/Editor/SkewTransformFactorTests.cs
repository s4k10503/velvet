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
            var ok = SkewTransformFactor.TryFactor(xDeg, yDeg, out var rotation, out var scale);
            var carried = Matrix4x4.Rotate(rotation);

            // Assert
            Assert.That(
                ok ? new[] { scale * carried.m00, scale * carried.m01, scale * carried.m10, scale * carried.m11 }
                    : new[] { float.NaN },
                Is.EqualTo(new[] { 1f + (tx * ty), tx, ty, 1f }).Within(1e-4f));
        }
    }
}
