using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how events raised inside a same-panel <c>V.Portal</c> reach the portal's logical ancestors
    /// under a live dispatch, where native bubbling carries the event up the target's own chain as well:
    /// a click and a value change reach a <c>Button</c>'s <c>onClick</c> and a field's <c>onValueChanged</c>
    /// as React's <c>onClick</c> and <c>onChange</c> bubble out of a portal, and a handler reached through
    /// portals nested in each other's content fires once per event, as React's single dispatch runs it once.
    /// </summary>
    [TestFixture]
    internal sealed class SamePanelPortalClickTests : PanelTestBase
    {
        private VisualElement _root;
        private static int s_fires;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            RuntimeStateProbe.ClearPortalRegistry();
            s_fires = 0;
            s_showHeldPortal = default;
            s_betweenFires = 0;
            s_nestedByPointer = false;
            _root = new VisualElement();
            _window.rootVisualElement.Add(_root);
        }

        [TearDown]
        public override void TearDown()
        {
            RuntimeStateProbe.ClearPortalRegistry();
            base.TearDown();
        }

        private VisualElement RegisteredTarget(string id)
        {
            var target = new VisualElement();
            _window.rootVisualElement.Add(target);
            FiberPortalRegistry.Register(id, target);
            return target;
        }

        private static void Dispatch<TEvent>(TEvent evt, VisualElement target) where TEvent : EventBase<TEvent>, new()
        {
            using (evt)
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private static Action RegisterInner(VisualElement element)
        {
            FiberPortalRegistry.Register("nested-inner", element);
            return () => FiberPortalRegistry.Unregister("nested-inner");
        }

        [Test]
        public void Given_AButtonAroundASamePanelPortal_When_AClickIsDispatchedOnItsChild_Then_ItsOnClickFiresOnce()
        {
            // Arrange
            var target = RegisteredTarget("click-target");
            _mounted = V.Mount(_root, V.Button(
                onClick: () => s_fires++,
                children: new VNode[] { V.Portal("click-target", children: new VNode[] { V.Div(name: "click-child") }) }));

            // Act
            Dispatch(ClickEvent.GetPooled(), target.Q<VisualElement>("click-child"));

            // Assert
            Assert.That(s_fires, Is.EqualTo(1));
        }

        [Test]
        public void Given_AToggleAroundASamePanelPortal_When_ABoolChangeIsDispatchedOnItsChild_Then_ItsHandlerGetsTheNewValue()
        {
            // Arrange
            bool? received = null;
            var target = RegisteredTarget("change-target");
            _mounted = V.Mount(_root, V.Motion(
                elementType: typeof(Toggle),
                events: new FiberEventBinding[] { new ChangeEventBinding<bool> { Handler = v => received = v } },
                children: new VNode[] { V.Portal("change-target", children: new VNode[] { V.Div(name: "change-child") }) }));

            // Act
            Dispatch(ChangeEvent<bool>.GetPooled(false, true), target.Q<VisualElement>("change-child"));

            // Assert
            Assert.That(received, Is.EqualTo(true));
        }

        private static StateUpdater<bool> s_showHeldPortal;

        // Shown only once the target is registered under the Button, so the portal resolves it at creation.
        [Component]
        private static VNode HeldPortalCallerRender()
        {
            var (show, setShow) = Hooks.UseState(false);
            s_showHeldPortal = setShow;
            return V.When(show, () => V.Portal("held-target", children: new VNode[] { V.Div(name: "held-child") }));
        }

        // GREEN_ON_BASE(characterization): the base bridges no click, so the Button's onClick is not called there.
        // This pins that the bridge leaves a Button on the target's own physical chain to native dispatch.
        [Test]
        public void Given_AButtonHoldingBothTheTargetAndThePortal_When_AClickEventReachesIt_Then_TheBridgeDoesNotCallItsOnClick()
        {
            // Arrange — the Button is a physical ancestor of the target and the logical ancestor of the portal.
            // Native dispatch reaches it, where Clickable answers the press itself.
            _mounted = V.Mount(_root, V.Button(
                onClick: () => s_fires++,
                children: new VNode[] { V.Div(name: "slot"), V.Component(HeldPortalCallerRender) }));
            var target = new VisualElement();
            _root.Q<VisualElement>("slot").Add(target);
            FiberPortalRegistry.Register("held-target", target);
            s_showHeldPortal.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            Dispatch(ClickEvent.GetPooled(), target.Q<VisualElement>("held-child"));

            // Assert
            Assert.That(s_fires, Is.EqualTo(0));
        }

        private static int s_betweenFires;
        private static bool s_nestedByPointer;

        [Component]
        private static VNode NestedOuterDeclRender()
            => V.Portal("nested-outer", children: new VNode[] { V.Component(NestedContentRender) });

        // The inner portal's target is rendered in the outer portal's content, so the inner target sits
        // physically inside the outer one and one native dispatch reaches both bridges. The inner portal is
        // declared inside a handler of its own there, a logical ancestor of the leaf between the two portals.
        [Component]
        private static VNode NestedContentRender()
        {
            var innerPortal = new VNode[] { V.Portal("nested-inner", children: new VNode[] { V.Component(NestedLeafRender) }) };
            return V.Fragment(children: new VNode[]
            {
                V.Div(name: "nested-inner-target", refCallback: RegisterInner),
                s_nestedByPointer
                    ? V.Motion(
                        events: new FiberEventBinding[] { new PointerDownBinding { Handler = _ => s_betweenFires++ } },
                        children: innerPortal)
                    : V.Button(onClick: () => s_betweenFires++, children: innerPortal),
            });
        }

        [Component]
        private static VNode NestedLeafRender() => V.Div(name: "nested-leaf");

        private VisualElement MountNested(VNode outermost)
        {
            var outer = RegisteredTarget("nested-outer");
            _mounted = V.Mount(_root, outermost);
            _mounted.FlushStateForTest();
            return outer.Q<VisualElement>("nested-leaf");
        }

        [Test]
        public void Given_PortalsNestedInEachOthersContent_When_AClickIsDispatchedOnTheInnerChild_Then_EachButtonAroundItFiresOnce()
        {
            // Arrange
            var leaf = MountNested(V.Button(
                onClick: () => s_fires++,
                children: new VNode[] { V.Component(NestedOuterDeclRender) }));

            // Act
            Dispatch(ClickEvent.GetPooled(), leaf);

            // Assert
            Assert.That((s_betweenFires, s_fires), Is.EqualTo((1, 1)));
        }

        [Test]
        public void Given_PortalsNestedInEachOthersContent_When_APointerDownIsDispatchedOnTheInnerChild_Then_EachHandlerAroundItFiresOnce()
        {
            // Arrange
            s_nestedByPointer = true;
            var leaf = MountNested(V.Motion(
                events: new FiberEventBinding[] { new PointerDownBinding { Handler = _ => s_fires++ } },
                children: new VNode[] { V.Component(NestedOuterDeclRender) }));

            // Act
            Dispatch(PointerDownEvent.GetPooled(), leaf);

            // Assert
            Assert.That((s_betweenFires, s_fires), Is.EqualTo((1, 1)));
        }

        [Test]
        public void Given_TwoPortalsOnOneTargetUnderTwoButtons_When_AClickIsDispatchedOnTheSecondsChild_Then_OnlyItsButtonFires()
        {
            // Arrange
            var target = RegisteredTarget("shared-target");
            _mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Button(onClick: () => s_betweenFires++,
                    children: new VNode[] { V.Portal("shared-target", children: new VNode[] { V.Div(name: "first-child") }) }),
                V.Button(onClick: () => s_fires++,
                    children: new VNode[] { V.Portal("shared-target", children: new VNode[] { V.Div(name: "second-child") }) }),
            }));

            // Act
            Dispatch(ClickEvent.GetPooled(), target.Q<VisualElement>("second-child"));

            // Assert
            Assert.That((s_betweenFires, s_fires), Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_TwoPortalsOnOneTargetUnderTwoButtons_When_AClickIsDispatchedOnTheFirstsChild_Then_OnlyItsButtonFires()
        {
            // Arrange
            var target = RegisteredTarget("shared-target");
            _mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Button(onClick: () => s_betweenFires++,
                    children: new VNode[] { V.Portal("shared-target", children: new VNode[] { V.Div(name: "first-child") }) }),
                V.Button(onClick: () => s_fires++,
                    children: new VNode[] { V.Portal("shared-target", children: new VNode[] { V.Div(name: "second-child") }) }),
            }));

            // Act
            Dispatch(ClickEvent.GetPooled(), target.Q<VisualElement>("first-child"));

            // Assert
            Assert.That((s_betweenFires, s_fires), Is.EqualTo((1, 0)));
        }

        // GREEN_ON_BASE(characterization): the base bridges no click, so the Button's onClick is not called there.
        // This pins that a child other code appends behind a portal's children is no portal's.
        [Test]
        public void Given_AChildAppendedBehindAPortalsChildren_When_AClickIsDispatchedOnIt_Then_ThePortalsButtonDoesNotFire()
        {
            // Arrange
            var target = RegisteredTarget("appended-target");
            _mounted = V.Mount(_root, V.Button(
                onClick: () => s_fires++,
                children: new VNode[] { V.Portal("appended-target", children: new VNode[] { V.Div(name: "portal-child") }) }));
            var appended = new VisualElement { name = "appended" };
            target.Add(appended);

            // Act
            Dispatch(ClickEvent.GetPooled(), appended);

            // Assert
            Assert.That(s_fires, Is.EqualTo(0));
        }
    }
}
