using System;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Pins what the <c>events:</c> parameter costs a warm render at zero beyond the node: a caller's array
    /// handed to a factory is not copied, and the array that puts a factory's own binding ahead of it comes
    /// from the pool and goes back to it. Each case measures against the same factory without the array, as
    /// <see cref="VFactoryEnumArgumentAllocTests"/> measures a refusal, since a factory allocates its node
    /// by design.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class ElementFactoryEventsAllocTests
    {
        // Static, so the measured delegates capture nothing and allocate no closure of their own.
        private static readonly FiberEventBinding[] CachedEvents =
        {
            new PointerDownBinding { Handler = _ => { } },
            new WheelBinding { Handler = _ => { } },
        };

        private static readonly Action OnClick = () => { };

        // Each side hands back what its node rented, as a retired tree would, so the pool is warm at every
        // call and the rented-out sets do not grow between the two measurements.
        private static void Retire(ElementNode node)
        {
            VNodePool.ReturnProps(node.Props);
            VNodePool.ReturnEventArray(node.Events);
        }

        private static readonly Action DivWithEvents = () => Retire(V.Div(events: CachedEvents));

        private static readonly Action DivWithout = () => Retire(V.Div());

        private static readonly Action ButtonWithOnClickAndEvents = () => Retire(V.Button(onClick: OnClick, events: CachedEvents));

        private static readonly Action ButtonWithOnClick = () => Retire(V.Button(onClick: OnClick));

        private static readonly Action ThreeSlotCycle = () => VNodePool.ReturnEventArray(VNodePool.RentEventArray(3));

        private static readonly Action Canary = () => GC.KeepAlive(new byte[16]);

        // The second term is what a probe stuck at zero fails, since two zeroed counts would satisfy the
        // difference vacuously. Both delegates run once first, for the warming reason
        // VNodePoolZeroAllocTests states.
        private static (int Difference, bool Measured) Cost(Action with, Action without)
        {
            with();
            without();
            var withBlocks = GCAllocationProbe.MedianBlocksDuring(with);
            var withoutBlocks = GCAllocationProbe.MedianBlocksDuring(without);
            return (withBlocks - withoutBlocks, withoutBlocks > 0);
        }

        [Test]
        public void Given_ACachedEventsArray_When_VDivIsCalledWithIt_Then_ItAllocatesNothingTheBareCallDoesNot()
        {
            // Arrange + Act
            var cost = Cost(DivWithEvents, DivWithout);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        [Test]
        public void Given_ACachedEventsArray_When_VButtonIsCalledWithItBesideOnClick_Then_ItAllocatesNothingOnClickAloneDoesNot()
        {
            // Arrange + Act
            var cost = Cost(ButtonWithOnClickAndEvents, ButtonWithOnClick);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        [Test]
        public void Given_AWarmThreeSlotEventArrayPool_When_RentReturnCycle_Then_DoesNotAllocate()
        {
            // Arrange
            ThreeSlotCycle();
            Canary();

            // Act
            var cost = (GCAllocationProbe.MedianBlocksDuring(ThreeSlotCycle), GCAllocationProbe.MedianBlocksDuring(Canary) > 0);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }
    }
}
