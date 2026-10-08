using System;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that resolving a class list the parser has already read allocates nothing, since the
    /// reconciler resolves a gradient element's class list again on every patch. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class GradientExtractAllocationTests
    {
        private static int WarmBlocks(string[] classNames)
        {
            void Once() => StyleGradientClass.TryExtract(classNames, out _);
            for (var i = 0; i < 64; i++)
            {
                Once();
            }
            return GCAllocationProbe.MedianBlocksDuring(Once);
        }

        // A probe stuck at zero satisfies both guards below without measuring anything; this canary is
        // what makes their zeros mean something.
        // GREEN_ON_BASE(characterization): the probe counts this canary's allocation on the base as well.
        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Arrange
            Action canary = () => GC.KeepAlive(new byte[16]);
            canary();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(canary);

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test]
        public void Given_AStopListClassReadBefore_When_ExtractedAgain_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(new[] { "bg-linear-[90deg,#0f172a_0%,#0f172a_45%,#ffffff_50%,#0f172a_55%,#0f172a_100%]" });

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_FromViaToClassesReadBefore_When_ExtractedAgain_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(new[] { "bg-linear-to-r", "from-[#ff0000]", "from-10%", "via-[#00ff00]", "to-[#0000ff]" });

            // Assert
            Assert.That(blocks, Is.Zero);
        }
    }
}
