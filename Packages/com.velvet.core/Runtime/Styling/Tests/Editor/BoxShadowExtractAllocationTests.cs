using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the shadow pass's two reads of a shadowed element's classes on each patch, the layers it draws and
    /// the colours a child's -inherit takes, allocate nothing but an inset ring's band for the single family's classes
    /// below, beside utilities of other families and an important one among them. The presets below are named shorter
    /// than any palette colour; shadow-inner, as long as white, is cut out. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class BoxShadowExtractAllocationTests
    {
        private static int WarmBlocks(Action once)
        {
            for (var i = 0; i < 64; i++)
            {
                once();
            }
            return GCAllocationProbe.MedianBlocksDuring(once);
        }

        // A probe stuck at zero satisfies every guard below without measuring anything; this canary is what makes
        // their zeros mean something.
        // GREEN_ON_BASE(characterization): the probe already counts a 16-byte array allocation on the base, which is all this case asserts.
        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Act
            var blocks = GCAllocationCanary.BlocksForKnownArray();

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test]
        public void Given_AShadowPreset_When_ItsLayersAreExtractedAgain_Then_NothingIsAllocated()
        {
            // Arrange
            var classNames = new[] { "flex", "rounded", "shadow-sm", "border" };

            // Act
            var blocks = WarmBlocks(() => StyleShadowClass.TryExtract(classNames, out _));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AnInsetShadowPreset_When_ItsLayersAreExtractedAgain_Then_NothingIsAllocated()
        {
            // Arrange
            var classNames = new[] { "inset-shadow-sm" };

            // Act
            var blocks = WarmBlocks(() => StyleShadowClass.TryExtract(classNames, out _));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        // The band is a layer of its own, built for the width the class reads, so it is the one block allocated.
        [Test]
        public void Given_AnInsetRing_When_ItsLayersAreExtractedAgain_Then_TheBandIsTheOneBlockAllocated()
        {
            // Arrange
            var classNames = new[] { "inset-ring" };

            // Act
            var blocks = WarmBlocks(() => StyleShadowClass.TryExtract(classNames, out _));

            // Assert
            Assert.That(blocks, Is.EqualTo(1));
        }

        [Test]
        public void Given_AShadowPresetBesideAnImportantClassOfAnotherFamily_When_ItsLayersAreExtractedAgain_Then_NothingIsAllocated()
        {
            // Arrange
            var classNames = new[] { "!bg-red-500", "shadow-lg" };

            // Act
            var blocks = WarmBlocks(() => StyleShadowClass.TryExtract(classNames, out _));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AShadowPresetRecordedOnAnElement_When_ItsColoursAreRecordedAgain_Then_NothingIsAllocated()
        {
            // Arrange
            var element = new VisualElement();
            var classNames = new[] { "flex", "rounded", "shadow-sm", "border" };

            // Act
            var blocks = WarmBlocks(() => BoxShadowSilhouette.RecordDeclaredColours(element, classNames));

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AShadowPresetBesideAnImportantClassOfAnotherFamily_When_ItsColoursAreRecordedAgain_Then_NothingIsAllocated()
        {
            // Arrange
            var element = new VisualElement();
            var classNames = new[] { "!bg-red-500", "shadow-lg" };

            // Act
            var blocks = WarmBlocks(() => BoxShadowSilhouette.RecordDeclaredColours(element, classNames));

            // Assert
            Assert.That(blocks, Is.Zero);
        }
    }
}
