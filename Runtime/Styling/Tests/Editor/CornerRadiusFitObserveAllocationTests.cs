using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that watching a div or a label the factory builds, for a radius the fit has not been told of, allocates
    /// nothing. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class CornerRadiusFitObserveAllocationTests
    {
        private static int BlocksToObserve(System.Func<VisualElement> create)
        {
            // A first watch outside the windows, as TextLineBoxAllocationTests makes a first box outside them.
            CornerRadiusFit.Observe(create());
            VisualElement element = null;
            return GCAllocationProbe.MedianBlocksDuring(() => element = create(), () => CornerRadiusFit.Observe(element));
        }

        // A probe stuck at zero satisfies the zero guards below without measuring anything; this canary is what makes
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
        public void Given_ADivElement_When_ObservedForARadius_Then_NothingIsAllocated()
        {
            // Act
            var blocks = BlocksToObserve(() => new SkewPickElement());

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_ALabelElement_When_ObservedForARadius_Then_NothingIsAllocated()
        {
            // Act
            var blocks = BlocksToObserve(() => new SkewPickLabel(string.Empty));

            // Assert
            Assert.That(blocks, Is.Zero);
        }
    }
}
