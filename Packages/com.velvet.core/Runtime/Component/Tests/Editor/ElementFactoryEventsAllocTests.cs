using System;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Pins what the <c>events:</c> parameter costs at zero beyond the node: a caller's array handed to a factory
    /// is not copied, and putting a factory's own binding ahead of it costs the one array the factory builds for
    /// that binding alone. Each case measures against the same factory without the array, as
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

        private const int HeldButtons = 8;

        private static readonly Action DivWithEvents = () => GC.KeepAlive(V.Div(events: CachedEvents));

        private static readonly Action DivWithout = () => GC.KeepAlive(V.Div());

        private static readonly Action ButtonWithOnClickAndEvents = () => GC.KeepAlive(V.Button(onClick: OnClick, events: CachedEvents));

        private static readonly Action ButtonWithOnClick = () => GC.KeepAlive(V.Button(onClick: OnClick));

        // The second term is what a probe stuck at zero fails, since two zeroed counts would satisfy the
        // difference vacuously. Both delegates run once first, for the warming reason
        // FactoryCallAllocationTests states.
        private static (int Difference, bool Measured) Cost(Action with, Action without)
        {
            with();
            without();
            var withBlocks = GCAllocationProbe.MedianBlocksDuring(with);
            var withoutBlocks = GCAllocationProbe.MedianBlocksDuring(without);
            return (withBlocks - withoutBlocks, withoutBlocks > 0);
        }

        // GREEN_ON_BASE(refactor): this case is the base's own text; only the delegates beside it stopped handing their parts back to a pool.
        [Test]
        public void Given_ACachedEventsArray_When_VDivIsCalledWithIt_Then_ItAllocatesNothingTheBareCallDoesNot()
        {
            // Arrange + Act
            var cost = Cost(DivWithEvents, DivWithout);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        // GREEN_ON_BASE(refactor): the base takes each side's event array from the pool the disposed mounts filled, so the sides read alike there too.
        [Test]
        public void Given_ACachedEventsArray_When_VButtonIsCalledWithItBesideOnClick_Then_ItAllocatesNothingOnClickAloneDoesNot()
        {
            // Arrange — disposed trees held buttons of both kinds, so a factory that took arrays from a disposed tree
            // would take one for each side alike.
            DisposedMount.Holding(HeldButtons, () => V.Button(onClick: OnClick, events: CachedEvents));
            DisposedMount.Holding(HeldButtons, () => V.Button(onClick: OnClick));

            // Act
            var cost = Cost(ButtonWithOnClickAndEvents, ButtonWithOnClick);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }
    }
}
