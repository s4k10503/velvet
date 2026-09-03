using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Pins each of the seven enum-argument refusals on the <c>V.*</c> factories to no allocation of its
    /// own: <c>layer:</c> and <c>focusOrder:</c> on <c>V.Portal</c>, <c>focusOrder:</c> on
    /// <c>V.WorldSpace</c>, <c>playOn:</c> on <c>V.Particles</c>, <c>movement:</c> on
    /// <c>V.Draggable</c>, <c>mode:</c> on <c>V.AnimatePresence</c> and <c>loaderMode:</c> on
    /// <c>V.Route</c>. <see cref="VNodePoolZeroAllocTests"/> names an accidental boxing as what it
    /// exists to catch, but it measures <see cref="VNodePool"/>; a factory allocates its node by design,
    /// so a bare zero-allocation assertion cannot be made about one. Each case here measures the factory
    /// against building the same node directly and pins the difference at zero, which is the refusal's
    /// own cost and nothing else.
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
        private static readonly VNode[] NoChildren = Array.Empty<VNode>();

        [Component]
        private static VNode RouteElementRender() => V.Div();

        private static readonly ComponentNode RouteElement = V.Component(RouteElementRender);

        #region delegates that allocate only their node

        private static readonly Action PortalViaFactory = () => GC.KeepAlive(
            V.Portal(UILayer.Overlay, children: NoChildren, focusOrder: PanelFocusOrder.Isolated));

        private static readonly Action PortalViaInit = () => GC.KeepAlive(new PortalNode
        {
            Layer = UILayer.Overlay, Children = NoChildren, FocusOrder = PanelFocusOrder.Isolated,
        });

        private static readonly Action WorldSpaceViaFactory = () => GC.KeepAlive(
            V.WorldSpace(Vector3.zero, children: NoChildren, focusOrder: PanelFocusOrder.Isolated));

        private static readonly Action WorldSpaceViaInit = () => GC.KeepAlive(new WorldSpaceNode
        {
            Position = Vector3.zero,
            Rotation = Quaternion.identity,
            PanelSize = new Vector2(1920f, 1080f),
            FocusOrder = PanelFocusOrder.Isolated,
            Children = NoChildren,
        });

        private static readonly Action PresenceViaFactory = () => GC.KeepAlive(
            V.AnimatePresence(children: NoChildren, mode: AnimatePresenceMode.Sync));

        private static readonly Action PresenceViaInit = () => GC.KeepAlive(new AnimatePresenceNode
        {
            Children = NoChildren, Mode = AnimatePresenceMode.Sync,
        });

        private static readonly Action RouteViaFactory = () => GC.KeepAlive(
            V.Route("reports", element: RouteElement, loaderMode: LoaderMode.Await));

        private static readonly Action RouteViaInit = () => GC.KeepAlive(new RouteDefinition
        {
            Path = "reports", Element = RouteElement, LoaderMode = LoaderMode.Await,
        });

        #endregion

        #region delegates that also rent a props bag

        // V.Particles and V.Draggable rent from VNodePool, so both sides of their pairs return the bag.
        // Renting without returning would leave the pool empty at every call and grow the rented set
        // across the two measurements, and a set that resizes between them charges one side and not
        // the other.
        private static readonly Action ParticlesViaFactory = () =>
            VNodePool.ReturnProps(V.Particles(effect: null, playOn: PlayTrigger.Mount).Props);

        private static readonly Action ParticlesViaInit = () =>
        {
            var props = VNodePool.RentProps();
            props.Particles = new ParticlesSettings(null, PlayTrigger.Mount, 100f);
            GC.KeepAlive(new ElementNode
            {
                ElementType = typeof(ParticlesElement),
                ClassNames = Array.Empty<string>(),
                Props = props,
                Children = NoChildren,
                Events = Array.Empty<FiberEventBinding>(),
            });
            VNodePool.ReturnProps(props);
        };

        private static readonly Action DraggableViaFactory = () =>
            VNodePool.ReturnProps(V.Draggable("row", movement: DragMovement.Translate).Props);

        private static readonly Action DraggableViaInit = () =>
        {
            var props = VNodePool.RentProps();
            props.Draggable = new DraggableSettings("row");
            GC.KeepAlive(new ElementNode
            {
                ElementType = typeof(VisualElement),
                ClassNames = Array.Empty<string>(),
                Props = props,
                Children = NoChildren,
                Events = Array.Empty<FiberEventBinding>(),
            });
            VNodePool.ReturnProps(props);
        };

        #endregion

        // The difference is what the refusal costs; the second term is what a probe stuck at zero fails,
        // since two zeroed counts would satisfy the difference vacuously. Both delegates run once first,
        // for the warming reason VNodePoolZeroAllocTests states.
        private static (int Difference, bool Measured) RefusalCost(Action viaFactory, Action viaInit)
        {
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

        // GREEN_ON_BASE(characterization): the base's V.WorldSpace allocates its node and no more.
        // It carries no refusal there at all, so the difference is zero for a different reason than it is
        // here. What this pins for the branch is that adding one costs nothing: spell the check
        // `Enum.IsDefined` and the difference measures two blocks.
        [Test]
        public void Given_ANamedFocusOrder_When_VWorldSpaceIsCalled_Then_ItsRefusalAllocatesNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(WorldSpaceViaFactory, WorldSpaceViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        // GREEN_ON_BASE(characterization): the base's V.Particles allocates its node and its settings and
        // no more. It carries no refusal there at all, so the difference is zero for a different reason
        // than it is here. What this pins for the branch is that adding one costs nothing: spell the
        // check `Enum.IsDefined` and the difference measures two blocks.
        [Test]
        public void Given_ANamedPlayOn_When_VParticlesIsCalled_Then_ItsRefusalAllocatesNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(ParticlesViaFactory, ParticlesViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        // GREEN_ON_BASE(characterization): the base's V.Draggable allocates its node and its settings and
        // no more. It carries no refusal there at all, so the difference is zero for a different reason
        // than it is here. What this pins for the branch is that adding one costs nothing: spell the
        // check `Enum.IsDefined` and the difference measures two blocks.
        [Test]
        public void Given_ANamedMovement_When_VDraggableIsCalled_Then_ItsRefusalAllocatesNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(DraggableViaFactory, DraggableViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        // GREEN_ON_BASE(characterization): the base's V.AnimatePresence allocates its node and no more.
        // It carries no refusal there at all, so the difference is zero for a different reason than it is
        // here. What this pins for the branch is that adding one costs nothing: spell the check
        // `Enum.IsDefined` and the difference measures two blocks. The V.Portal case above needs no
        // declaration -- the base already refuses `layer:` and pays a block pair for it, so it is red there.
        [Test]
        public void Given_ANamedMode_When_VAnimatePresenceIsCalled_Then_ItsRefusalAllocatesNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(PresenceViaFactory, PresenceViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        // GREEN_ON_BASE(characterization): the base's V.Route allocates its definition and no more.
        // It carries no refusal there at all, so the difference is zero for a different reason than it is
        // here. What this pins for the branch is that adding one costs nothing: spell the check
        // `Enum.IsDefined` and the difference measures two blocks.
        [Test]
        public void Given_ANamedLoaderMode_When_VRouteIsCalled_Then_ItsRefusalAllocatesNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(RouteViaFactory, RouteViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }
    }
}
