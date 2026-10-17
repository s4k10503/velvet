using NUnit.Framework;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class RunningStyleTransitionTests
    {
        [TestCase(0f, 0f, 0f, true)]
        [TestCase(float.NaN, 0f, 0f, false)]
        [TestCase(0f, float.NaN, 0f, false)]
        [TestCase(0f, 0f, float.NaN, false)]
        public void Given_ATransitionsStartEndAndReversalStart_When_AskedWhetherAllAreNumbers_Then_OnlyAWholeSetIs(
            float from, float to, float reversingStart, bool expected)
        {
            // Arrange — the values are the arguments.

            // Act
            var all = RunningStyleTransition.AllNumbers(from, to, reversingStart);

            // Assert
            Assert.That(all, Is.EqualTo(expected));
        }
    }
}
