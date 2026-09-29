using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A driver's hold against an <c>animate-*</c> loop does not outlive the element's teardown: a hold a
    /// driver never released would keep a pooled element's next loop from writing its slot.
    /// </summary>
    [TestFixture]
    internal sealed class MotionLoopHoldCleanupTests : VariantCleanupTestsBase
    {
        [Test]
        public void Given_ALeafWhoseOpacityADriverHeld_When_TheLeafIsRemoved_Then_ALoopAttachedAfterwardsWritesItsOpacity()
        {
            // Arrange — a hold nobody releases, as a teardown that pre-empts the driver leaves it.
            using var mounted = MountHost(_ => V.Div(name: "leaf"), out var scheduler, out _);
            var leaf = _root.Q<VisualElement>("leaf");
            StyleAnimateDriver.HoldAgainstLoop(leaf, new object(), MotionTransitionSlots.Opacity);
            s_store.Set(1);
            scheduler.DrainImmediateForTest();
            var pulse = StyleAnimateDriver.Attach(leaf, new AnimateSpec(AnimateMode.Pulse, 2f), panVertical: false);

            // Act
            StyleAnimateDriver.ReassertLoop(leaf);
            var opacity = leaf.style.opacity.value;
            StyleAnimateDriver.Detach(leaf, pulse);

            // Assert
            Assert.That(opacity, Is.GreaterThanOrEqualTo(0.5f));
        }
    }
}
