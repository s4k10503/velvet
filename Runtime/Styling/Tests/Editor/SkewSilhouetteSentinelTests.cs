using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the skew silhouette's suppression-sentinel test is BIT-EXACT, not Unity's approximate
    /// <c>Color</c> equality (epsilon ~1e-5). An exact compare still recognizes the real sentinel write but
    /// does not misclassify a genuine authored color that merely lands in the narrow band around it. GWT,
    /// one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class SkewSilhouetteSentinelTests
    {
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
