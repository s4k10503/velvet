using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class BenchmarkRunTests
    {
        // GREEN_ON_BASE(construction): the base run carries TestUtilities along with the tests that use it.
        // BenchmarkRun is this branch's helper on both sides, and making `IsFull` true for every argument list reddens this.
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

        // GREEN_ON_BASE(construction): the base run carries TestUtilities along with the tests that use it.
        // BenchmarkRun is this branch's helper on both sides, and making `Warmup` return the count it is given reddens this.
        [Test]
        public void Given_AnEditorRunWithoutTheFlag_When_WarmupIsAsked_Then_ItIsZero()
        {
            // Act
            var warmup = BenchmarkRun.Warmup(5);

            // Assert
            Assert.That(warmup, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(construction): the base run carries TestUtilities along with the tests that use it.
        // BenchmarkRun is this branch's helper on both sides, and making `Measurements` return the count it is given reddens this.
        [Test]
        public void Given_AnEditorRunWithoutTheFlag_When_MeasurementsAreAsked_Then_ItIsOne()
        {
            // Act
            var measurements = BenchmarkRun.Measurements(20);

            // Assert
            Assert.That(measurements, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(construction): the base run carries TestUtilities along with the tests that use it.
        // BenchmarkRun is this branch's helper on both sides, and making `IsFull` false for every argument list reddens this.
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
