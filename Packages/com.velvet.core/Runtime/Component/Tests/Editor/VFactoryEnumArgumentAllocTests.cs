using System;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Pins the enum-argument refusals on the <c>V.*</c> factories to no allocation of their own.
    /// <see cref="VNodePoolZeroAllocTests"/> names an accidental boxing as what it exists to catch, but it
    /// measures <see cref="VNodePool"/>; a factory allocates its node by design, so a bare zero-allocation
    /// assertion cannot be made about one. Each case here measures the factory against constructing the
    /// same node directly and pins the difference at zero, which is the refusal's own cost and nothing else.
    /// </summary>
    /// <remarks>
    /// <c>Enum.IsDefined(Type, object)</c> boxes its argument, and netstandard 2.1 — this project's API
    /// level — declares no generic overload that would not. Velvet rebuilds the VNode tree per render, so
    /// a factory in a render body runs on each of them and the boxing was a per-render heap allocation.
    /// </remarks>
    [TestFixture]
    [Category("Performance")]
    internal sealed class VFactoryEnumArgumentAllocTests
    {
        // Static, so the measured delegates capture nothing and allocate no closure of their own.
        private static readonly VNode[] NoChildren = new VNode[0];

        private static readonly Action PortalViaFactory = () => GC.KeepAlive(
            V.Portal(UILayer.Overlay, children: NoChildren, focusOrder: PanelFocusOrder.Isolated));

        private static readonly Action PortalViaInit = () => GC.KeepAlive(new PortalNode
        {
            Layer = UILayer.Overlay, Children = NoChildren, FocusOrder = PanelFocusOrder.Isolated,
        });

        private static readonly Action PresenceViaFactory = () => GC.KeepAlive(
            V.AnimatePresence(children: NoChildren, mode: AnimatePresenceMode.Sync));

        private static readonly Action PresenceViaInit = () => GC.KeepAlive(new AnimatePresenceNode
        {
            Children = NoChildren, Mode = AnimatePresenceMode.Sync,
        });

        // The difference is what the refusal costs; the second term is what a probe stuck at zero fails,
        // since two zeroed counts would satisfy the difference vacuously.
        private static (int Difference, bool Measured) RefusalCost(Action viaFactory, Action viaInit)
        {
            // The first execution of a path charges one-time runtime work to whatever scope runs it, so
            // both delegates run once outside the measured window.
            viaFactory();
            viaInit();
            var factoryBlocks = GCAllocationProbe.SampleBlocksDuring(viaFactory);
            var initBlocks = GCAllocationProbe.SampleBlocksDuring(viaInit);
            return (factoryBlocks - initBlocks, initBlocks > 0);
        }

        [Test]
        public void Given_ANamedLayerAndFocusOrder_When_VPortalIsCalled_Then_ItsTwoRefusalsAllocateNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(PortalViaFactory, PortalViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        // GREEN_ON_BASE(characterization): the base's V.AnimatePresence allocates its node and no more.
        // It carries no refusal there at all, so the difference is zero for a different reason than it
        // is here. What this pins is that adding one costs nothing: spell the check `Enum.IsDefined`
        // and the difference measures two blocks. The V.Portal case above needs no declaration -- the
        // base already refuses `layer:` and pays a block pair for it, so it is red there.
        [Test]
        public void Given_ANamedMode_When_VAnimatePresenceIsCalled_Then_ItsRefusalAllocatesNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(PresenceViaFactory, PresenceViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }
    }
}
