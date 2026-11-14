using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Pins each of the eight enum-argument refusals on the <c>V.*</c> factories to no allocation of its
    /// own: <c>layer:</c> and <c>focusOrder:</c> on <c>V.Portal</c>, <c>focusOrder:</c> on
    /// <c>V.WorldSpace</c>, <c>playOn:</c> on <c>V.Particles</c>, <c>movement:</c> on
    /// <c>V.Draggable</c>, <c>mode:</c> on <c>V.AnimatePresence</c>, <c>loaderMode:</c> on
    /// <c>V.Route</c> and <c>direction:</c> on <c>V.Slider</c>. A factory allocates its node by design, so a
    /// bare zero-allocation assertion cannot be made about one. Each case here measures the factory against
    /// building the same node directly and pins the difference at zero, which is the refusal's own cost and
    /// nothing else.
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

        #region delegates that also allocate a props bag

        private static readonly Action ParticlesViaFactory = () =>
            GC.KeepAlive(V.Particles(effect: null, playOn: PlayTrigger.Mount));

        private static readonly Action ParticlesViaInit = () =>
        {
            var props = new FiberElementProps();
            props.Particles = new ParticlesSettings(null, PlayTrigger.Mount, 100f);
            GC.KeepAlive(new ElementNode
            {
                ElementType = typeof(ParticlesElement),
                ClassNames = Array.Empty<string>(),
                Props = props,
                Children = NoChildren,
                Events = Array.Empty<FiberEventBinding>(),
            });
        };

        private static readonly Action DraggableViaFactory = () =>
            GC.KeepAlive(V.Draggable("row", movement: DragMovement.Translate));

        private static readonly Action DraggableViaInit = () =>
        {
            var props = new FiberElementProps();
            props.Draggable = new DraggableSettings("row");
            GC.KeepAlive(new ElementNode
            {
                ElementType = typeof(VisualElement),
                ClassNames = Array.Empty<string>(),
                Props = props,
                Children = NoChildren,
                Events = Array.Empty<FiberEventBinding>(),
            });
        };

        private static readonly Action SliderViaFactory = () =>
            GC.KeepAlive(V.Slider(direction: SliderDirection.Vertical));

        private static readonly Action SliderViaInit = () =>
        {
            var props = new FiberElementProps();
            props.Slider = new SliderSettings(null, null, SliderDirection.Vertical, null);
            GC.KeepAlive(new ElementNode
            {
                ElementType = typeof(Slider),
                ClassNames = Array.Empty<string>(),
                Props = props,
                Children = NoChildren,
                Events = Array.Empty<FiberEventBinding>(),
            });
        };

        #endregion

        // The difference is what the refusal costs; the second term is what a probe stuck at zero fails,
        // since two zeroed counts would satisfy the difference vacuously. Both delegates run once first,
        // for the warming reason VNodePoolZeroAllocTests states.
        private static (int Difference, bool Measured) RefusalCost(Action viaFactory, Action viaInit)
        {
            viaFactory();
            viaInit();
            var factoryBlocks = GCAllocationProbe.MedianBlocksDuring(viaFactory);
            var initBlocks = GCAllocationProbe.MedianBlocksDuring(viaInit);
            return (factoryBlocks - initBlocks, initBlocks > 0);
        }

        // GREEN_ON_BASE(refactor): this case is the base's own text; only the delegates beside it stopped handing a props bag back to a pool.
        [Test]
        public void Given_ANamedLayerAndFocusOrder_When_VPortalIsCalled_Then_ItsTwoRefusalsAllocateNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(PortalViaFactory, PortalViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        // GREEN_ON_BASE(refactor): this case is the base's own text; only the delegates beside it stopped handing a props bag back to a pool.
        [Test]
        public void Given_ANamedFocusOrder_When_VWorldSpaceIsCalled_Then_ItsRefusalAllocatesNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(WorldSpaceViaFactory, WorldSpaceViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        // GREEN_ON_BASE(refactor): this case is the base's own text; only the delegates beside it stopped handing a props bag back to a pool.
        [Test]
        public void Given_ANamedPlayOn_When_VParticlesIsCalled_Then_ItsRefusalAllocatesNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(ParticlesViaFactory, ParticlesViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        // GREEN_ON_BASE(refactor): this case is the base's own text; only the delegates beside it stopped handing a props bag back to a pool.
        [Test]
        public void Given_ANamedMovement_When_VDraggableIsCalled_Then_ItsRefusalAllocatesNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(DraggableViaFactory, DraggableViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        // GREEN_ON_BASE(refactor): this case is the base's own text; only the delegates beside it stopped handing a props bag back to a pool.
        [Test]
        public void Given_ANamedMode_When_VAnimatePresenceIsCalled_Then_ItsRefusalAllocatesNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(PresenceViaFactory, PresenceViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        // GREEN_ON_BASE(refactor): this case is the base's own text; only the delegates beside it stopped handing a props bag back to a pool.
        [Test]
        public void Given_ANamedLoaderMode_When_VRouteIsCalled_Then_ItsRefusalAllocatesNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(RouteViaFactory, RouteViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        // GREEN_ON_BASE(refactor): this case is the base's own text; only the delegates beside it stopped handing a props bag back to a pool.
        [Test]
        public void Given_ANamedDirection_When_VSliderIsCalled_Then_ItsRefusalAllocatesNothing()
        {
            // Arrange + Act
            var cost = RefusalCost(SliderViaFactory, SliderViaInit);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }
    }
}
