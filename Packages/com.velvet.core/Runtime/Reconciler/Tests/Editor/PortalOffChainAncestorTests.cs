using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that an element holding a <c>V.Portal</c>'s target without being a logical ancestor of the
    /// portal's call site runs none of its <c>events:</c> handlers for an event raised in the portal's
    /// content, in either phase, as React's synthetic dispatch walks the fiber tree and never the container
    /// the content is attached to. The logical ancestors keep their handlers, once in each phase.
    /// </summary>
    [TestFixture]
    internal sealed class PortalOffChainAncestorTests : PanelTestBase
    {
        private const string TargetId = "off-chain-target";

        private static readonly List<string> s_log = new();
        private static StateUpdater<bool> s_showPortal;

        private VisualElement _root;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_log.Clear();
            s_showPortal = default;
            RuntimeStateProbe.ClearPortalRegistry();
            _root = new VisualElement();
            _window.rootVisualElement.Add(_root);
        }

        [TearDown]
        public override void TearDown()
        {
            RuntimeStateProbe.ClearPortalRegistry();
            base.TearDown();
        }

        [Component]
        private static VNode CallerRender()
        {
            var (show, setShow) = Hooks.UseState(false);
            s_showPortal = setShow;
            return V.When(show, () => V.Portal(TargetId, children: new VNode[] { V.Div(name: "content") }));
        }

        private static FiberEventBinding[] PointerDown(string entry, bool capture) =>
            new FiberEventBinding[] { new PointerDownBinding { Handler = _ => s_log.Add(entry), Capture = capture } };

        private static FiberEventBinding[] Wheel(string entry, bool capture) =>
            new FiberEventBinding[] { new WheelBinding { Handler = _ => s_log.Add(entry), Capture = capture } };

        private static FiberEventBinding[] Change(string entry) =>
            new FiberEventBinding[] { new ChangeEventBinding<bool> { Handler = _ => s_log.Add(entry) } };

        private static void Dispatch<TEvent>(TEvent evt, VisualElement target) where TEvent : EventBase<TEvent>, new()
        {
            using (evt)
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private static System.Action RegisterTarget(VisualElement element)
        {
            FiberPortalRegistry.Register(TargetId, element);
            return () => FiberPortalRegistry.Unregister(TargetId);
        }

        // The portal's target is put under "holder" once mounted, so "holder" holds the content without being
        // above the call site, which sits under "caller". Returns the portal's content.
        private VisualElement MountHolderBesideCaller(
            System.Type holderType, FiberEventBinding[] holderEvents, System.Type callerType, FiberEventBinding[] callerEvents)
        {
            _mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Motion(name: "holder", elementType: holderType, events: holderEvents,
                    children: new VNode[] { V.Div(name: "slot") }),
                V.Motion(name: "caller", elementType: callerType, events: callerEvents,
                    children: new VNode[] { V.Component(CallerRender) }),
            }));
            var target = new VisualElement();
            _root.Q<VisualElement>("slot").Add(target);
            FiberPortalRegistry.Register(TargetId, target);
            s_showPortal.Invoke(true);
            _mounted.FlushStateForTest();
            return target.Q<VisualElement>("content");
        }

        [Test]
        public void Given_APhysicalOnlyAncestorOfThePortalTarget_When_APointerDownReachesTheContent_Then_OnlyTheLogicalAncestorRunsItsBubbleHandler()
        {
            // Arrange
            var content = MountHolderBesideCaller(
                typeof(VisualElement), PointerDown("holder", false), typeof(VisualElement), PointerDown("caller", false));

            // Act
            Dispatch(PointerDownEvent.GetPooled(), content);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("caller"));
        }

        [Test]
        public void Given_APhysicalOnlyAncestorOfThePortalTarget_When_APointerDownReachesTheContent_Then_OnlyTheLogicalAncestorRunsItsCaptureHandler()
        {
            // Arrange
            var content = MountHolderBesideCaller(
                typeof(VisualElement), PointerDown("holder", true), typeof(VisualElement), PointerDown("caller", true));

            // Act
            Dispatch(PointerDownEvent.GetPooled(), content);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("caller"));
        }

        [Test]
        public void Given_APhysicalOnlyAncestorOfThePortalTarget_When_AWheelReachesTheContent_Then_OnlyTheLogicalAncestorRunsItsCaptureHandler()
        {
            // Arrange
            var content = MountHolderBesideCaller(
                typeof(VisualElement), Wheel("holder", true), typeof(VisualElement), Wheel("caller", true));

            // Act
            Dispatch(WheelEvent.GetPooled(), content);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("caller"));
        }

        [Test]
        public void Given_APhysicalOnlyAncestorOfThePortalTarget_When_AWheelReachesTheContent_Then_OnlyTheLogicalAncestorRunsItsBubbleHandler()
        {
            // Arrange
            var content = MountHolderBesideCaller(
                typeof(VisualElement), Wheel("holder", false), typeof(VisualElement), Wheel("caller", false));

            // Act
            Dispatch(WheelEvent.GetPooled(), content);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("caller"));
        }

        [Test]
        public void Given_APhysicalOnlyToggleAroundThePortalTarget_When_ABoolChangeReachesTheContent_Then_OnlyTheLogicalToggleGetsIt()
        {
            // Arrange
            var content = MountHolderBesideCaller(
                typeof(Toggle), Change("holder"), typeof(Toggle), Change("caller"));

            // Act
            Dispatch(ChangeEvent<bool>.GetPooled(false, true), content);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("caller"));
        }

        [Test]
        public void Given_APortalIntoAnElementCarryingHandlers_When_APointerDownReachesTheContent_Then_TheElementRunsNoneOfThem()
        {
            // Arrange — the element the portal renders into is the bridge's own anchor.
            _mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Motion(name: "container", events: PointerDown("container", false), refCallback: RegisterTarget),
                V.Motion(events: PointerDown("caller", false), children: new VNode[]
                {
                    V.Portal(TargetId, children: new VNode[] { V.Div(name: "content") }),
                }),
            }));

            // Act
            Dispatch(PointerDownEvent.GetPooled(), _root.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("caller"));
        }

        private static System.Action RegisterScrollViewTarget(VisualElement element)
        {
            element.RegisterCallback<WheelEvent>(_ => s_log.Add("scroll-view"));
            return RegisterTarget(element);
        }

        // GREEN_ON_BASE(characterization): the base's bridge listens on the ScrollView itself, behind its own callbacks.
        // This pins that a portal into a ScrollView still lets the ScrollView answer first.
        [Test]
        public void Given_APortalIntoAScrollViewDeclaredUnderAHandlerThatStopsWheel_When_AWheelIsDispatchedOnItsChild_Then_TheScrollViewsOwnCallbackStillRuns()
        {
            // Arrange — the handler that stops the wheel is the portal's logical ancestor alone, not the
            // ScrollView's, so only the bridge carries the wheel to it.
            _mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Motion(name: "scroll", elementType: typeof(ScrollView), refCallback: RegisterScrollViewTarget),
                V.Motion(
                    events: new FiberEventBinding[] { new WheelBinding { Handler = evt => evt.StopPropagation() } },
                    children: new VNode[] { V.Portal(TargetId, children: new VNode[] { V.Div(name: "content") }) }),
            }));
            var content = _root.Q<VisualElement>("scroll").contentContainer.Q<VisualElement>("content");

            // Act
            Dispatch(WheelEvent.GetPooled(), content);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("scroll-view"));
        }

        // GREEN_ON_BASE(characterization): the base runs an element above both the target and the call site once per phase.
        // This pins that the suppression leaves such an element alone.
        [Test]
        public void Given_AnElementHoldingBothThePortalTargetAndTheCallSite_When_APointerDownReachesTheContent_Then_ItRunsOnceInEachPhase()
        {
            // Arrange
            var bindings = new FiberEventBinding[]
            {
                new PointerDownBinding { Handler = _ => s_log.Add("shared:capture"), Capture = true },
                new PointerDownBinding { Handler = _ => s_log.Add("shared:bubble") },
            };
            _mounted = V.Mount(_root, V.Motion(name: "shared", events: bindings, children: new VNode[]
            {
                V.Div(name: "slot"),
                V.Component(CallerRender),
            }));
            var target = new VisualElement();
            _root.Q<VisualElement>("slot").Add(target);
            FiberPortalRegistry.Register(TargetId, target);
            s_showPortal.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            Dispatch(PointerDownEvent.GetPooled(), target.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("shared:capture,shared:bubble"));
        }
    }
}
