using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class BenchmarkRunTests
    {
        [Test]
        public void Given_NoFullFlag_When_CountsAreAsked_Then_ABenchmarkTakesOneIterationAndNoWarmup()
        {
            // Arrange
            var args = new[] { "Unity", "-runTests", "-testPlatform", "PlayMode" };

            // Act
            var isFull = BenchmarkRun.IsFull(args);

            // Assert
            Assert.That((isFull, BenchmarkRun.Warmup(5, isFull), BenchmarkRun.Measurements(20, isFull)),
                Is.EqualTo((false, 0, 1)));
        }

        [Test]
        public void Given_TheFullFlag_When_CountsAreAsked_Then_ABenchmarkKeepsTheCountsItDeclares()
        {
            // Arrange
            var args = new[] { "Unity", "-runTests", BenchmarkRun.FullFlag };

            // Act
            var isFull = BenchmarkRun.IsFull(args);

            // Assert
            Assert.That((isFull, BenchmarkRun.Warmup(5, isFull), BenchmarkRun.Measurements(20, isFull)),
                Is.EqualTo((true, 5, 20)));
        }
    }
}
