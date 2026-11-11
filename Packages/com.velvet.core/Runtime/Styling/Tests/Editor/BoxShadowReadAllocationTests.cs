using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that reading whether an element's box-shadow is one the browser discards allocates nothing for the
    /// class shapes below, since the shadow pass reads it on each patch of a shadowed element no clip-path suppresses.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class BoxShadowReadAllocationTests
    {
        private static int WarmBlocks(string[] classNames)
        {
            void Once() => StyleRingClass.ReadBoxShadow(classNames);
            for (var i = 0; i < 64; i++)
            {
                Once();
            }
            return GCAllocationProbe.MedianBlocksDuring(Once);
        }

        // A probe stuck at zero satisfies every guard below without measuring anything; this canary is what makes
        // their zeros mean something.
        // GREEN_ON_BASE(refactor): the probe already counts a 16-byte array allocation on the base, which is all this case asserts.
        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Act
            var blocks = GCAllocationCanary.BlocksForKnownArray();

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test]
        public void Given_AShadowPresetBesideOtherUtilities_When_TheBoxShadowIsReadAgain_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(new[] { "flex", "rounded", "shadow-sm", "border" });

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AClassOpeningWithTheShadowRootAndNoDash_When_TheBoxShadowIsReadAgain_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(new[] { "shadowed" });

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        // A keyword colour is read only after the bracket reading has declined it.
        [Test]
        public void Given_AShadowKeywordColour_When_TheBoxShadowIsReadAgain_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(new[] { "shadow-current" });

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        // transparent is a palette name as well, which the palette takes as a string.
        [Test]
        public void Given_AShadowTransparentColour_When_TheBoxShadowIsReadAgain_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(new[] { "shadow-transparent" });

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AnImportantClassOfAnotherFamily_When_TheBoxShadowIsReadAgain_Then_NothingIsAllocated()
        {
            // Act
            var blocks = WarmBlocks(new[] { "!bg-red-500" });

            // Assert
            Assert.That(blocks, Is.Zero);
        }
    }
}
