using System.Collections;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the gradient parser's per-class memo to its bound, read by reflection.
    /// </summary>
    [TestFixture]
    internal sealed class GradientStopCapacityTests
    {
        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost the shader-source case and this memo case reads none of it.
        [Test]
        public void Given_MoreDistinctActivatorsThanTheMemoHolds_When_Extracted_Then_TheMemoStaysWithinItsBound()
        {
            // Arrange — the parser's memo of activator readings, emptied, then sent one more distinct class
            // than the 256 its bound admits.
            var memo = typeof(StyleGradientClass).GetField("s_activators", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            var entries = (IDictionary)memo.GetType().GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(memo);
            entries.Clear();

            // Act
            for (var angle = 0; angle <= 256; angle++)
            {
                StyleGradientClass.TryExtract(new[] { "bg-linear-" + angle, "from-[#ff0000]" }, out _);
            }

            // Assert
            Assert.That(entries.Count, Is.LessThanOrEqualTo(256));
        }
    }
}
