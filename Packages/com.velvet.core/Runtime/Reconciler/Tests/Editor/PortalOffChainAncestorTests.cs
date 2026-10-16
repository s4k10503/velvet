using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        private static StateUpdater<bool> s_showFirst;
        private static StateUpdater<bool> s_showSecond;
        private static StateUpdater<bool> s_showButton;

        private VisualElement _root;
        private MountedTree _other;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_log.Clear();
            s_showPortal = default;
            s_showFirst = default;
            s_showSecond = default;
            s_showButton = default;
            RuntimeStateProbe.ClearPortalRegistry();
            _root = new VisualElement();
            _window.rootVisualElement.Add(_root);
        }

        [TearDown]
        public override void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _other?.Dispose();
            _other = null;
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

        private static FiberEventBinding[] Click(string entry) =>
            new FiberEventBinding[] { new ClickedBinding { Handler = () => s_log.Add(entry) } };

        private Button MountButtonHolderBesideCaller() =>
            MountButtonHolderBesideCaller(Click("click"));

        private Button MountButtonHolderBesideCaller(FiberEventBinding[] holderEvents)
        {
            MountHolderBesideCaller(typeof(Button), holderEvents, typeof(VisualElement), null);
            return _root.Q<Button>("holder");
        }

        [Test]
        public void Given_APhysicalOnlyButtonAroundThePortalTarget_When_APressInTheContentIsFollowedByItsClick_Then_ItsOnClickDoesNotRun()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            Dispatch(PointerDownEvent.GetPooled(), button.Q<VisualElement>("content"));

            // Act
            button.SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo(""));
        }

        [Test]
        public void Given_APhysicalOnlyButtonAroundThePortalTarget_When_ASubmitInTheContentIsFollowedByItsClick_Then_ItsOnClickDoesNotRun()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            Dispatch(EventBase<NavigationSubmitEvent>.GetPooled(), button.Q<VisualElement>("content"));

            // Act
            button.SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo(""));
        }

        [Test]
        public void Given_APhysicalOnlyButtonWithAClickedEventBinding_When_APressInTheContentIsFollowedByItsClick_Then_ItDoesNotRun()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller(new FiberEventBinding[]
            {
                new ClickedEventBinding { Handler = _ => s_log.Add("click-event") },
            });
            Dispatch(PointerDownEvent.GetPooled(), button.Q<VisualElement>("content"));

            // Act
            button.SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo(""));
        }

        // GREEN_ON_BASE(characterization): the base runs a Button's click whatever began it.
        // This pins that a press which ended in a pointer-up leaves the next click alone.
        [Test]
        public void Given_APressInTheContentThatEndedInAPointerUp_When_TheButtonIsClicked_Then_ItsOnClickRuns()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            Dispatch(PointerDownEvent.GetPooled(), button.Q<VisualElement>("content"));
            Dispatch(PointerUpEvent.GetPooled(), button);

            // Act
            button.SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("click"));
        }

        // GREEN_ON_BASE(characterization): the base runs a Button's click whatever began it.
        // This pins that a cancelled press leaves the next click alone.
        [Test]
        public void Given_APressInTheContentThatWasCancelled_When_TheButtonIsClicked_Then_ItsOnClickRuns()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            Dispatch(PointerDownEvent.GetPooled(), button.Q<VisualElement>("content"));
            Dispatch(EventBase<PointerCancelEvent>.GetPooled(), button);

            // Act
            button.SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("click"));
        }

        // GREEN_ON_BASE(characterization): the base runs a Button's click whatever began it.
        // This pins that a press on the Button's own child is no press from off the chain.
        [Test]
        public void Given_APressInTheContentFollowedByAPressOnTheButtonsOwnChild_When_TheButtonIsClicked_Then_ItsOnClickRuns()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            Dispatch(PointerDownEvent.GetPooled(), button.Q<VisualElement>("content"));
            Dispatch(PointerDownEvent.GetPooled(), button.Q<VisualElement>("slot"));

            // Act
            button.SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("click"));
        }

        private static int s_clickRenders;

        [Component]
        private static VNode ReboundButtonRender()
        {
            var (_, setTick) = Hooks.UseState(false);
            s_showButton = setTick;
            var render = ++s_clickRenders;
            return V.Motion(name: "holder", elementType: typeof(Button),
                events: new FiberEventBinding[] { new ClickedBinding { Handler = () => s_log.Add("click" + render) } },
                children: new VNode[] { V.Div(name: "slot") });
        }

        [Test]
        public void Given_APhysicalOnlyButtonBoundAgainByARender_When_APressInTheContentIsFollowedByItsClick_Then_ItsOnClickDoesNotRun()
        {
            // Arrange — the press starts before the render that binds the Button again.
            s_clickRenders = 0;
            _mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Component(ReboundButtonRender),
                V.Motion(name: "caller", children: new VNode[] { V.Component(CallerRender) }),
            }));
            var target = new VisualElement();
            _root.Q<VisualElement>("slot").Add(target);
            FiberPortalRegistry.Register(TargetId, target);
            s_showPortal.Invoke(true);
            _mounted.FlushStateForTest();
            Dispatch(PointerDownEvent.GetPooled(), target.Q<VisualElement>("content"));
            var rendersBefore = s_clickRenders;
            s_showButton.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            _root.Q<Button>("holder").SimulateClick();

            // Assert
            Assert.That((s_clickRenders > rendersBefore, string.Join(",", s_log)), Is.EqualTo((true, "")));
        }

        [Component]
        private static VNode ShownButtonRender()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_showButton = setShow;
            return V.When(show, () => V.Button(onClick: () => s_log.Add("click")));
        }

        // Zero where the manager keeps no press origins at all, so a tree without the mechanism answers with a
        // count rather than a missing member.
        private static int ClickGates(FiberEventBindingManager manager) =>
            (s_gatesField?.GetValue(manager) as ICollection)?.Count ?? 0;

        [Test]
        public void Given_AButtonWithAnOnClick_When_ItLeavesTheTree_Then_TheManagerKeepsNoPressOriginForIt()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(ShownButtonRender));
            var mountedGates = ClickGates(EventManager);

            // Act
            s_showButton.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((mountedGates, ClickGates(EventManager)), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_APortalInAnotherMount_When_APointerDownReachesItsContent_Then_TheElementItRendersIntoRunsNoneOfItsHandlers()
        {
            // Arrange — the element is rendered by one mount and rendered into by another.
            var rootOfOther = new VisualElement();
            _window.rootVisualElement.Add(rootOfOther);
            _other = V.Mount(rootOfOther, V.Motion(
                name: "modal-root", events: PointerDown("modal-root", false), refCallback: RegisterTarget));
            _mounted = V.Mount(_root, V.Motion(events: PointerDown("caller", false), children: new VNode[]
            {
                V.Portal(TargetId, children: new VNode[] { V.Div(name: "content") }),
            }));

            // Act
            Dispatch(PointerDownEvent.GetPooled(), rootOfOther.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("caller"));
        }

        // The field holds the portal's target without being above the call site. Returns the portal's content.
        private VisualElement MountTextFieldAroundTarget(EventCallback<FocusInEvent> onFocus)
        {
            _mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.TextField(name: "field", onFocus: onFocus),
                V.Motion(name: "caller",
                    events: new FiberEventBinding[] { new FocusInBinding { Handler = _ => s_log.Add("caller") } },
                    children: new VNode[] { V.Component(CallerRender) }),
            }));
            var target = new VisualElement();
            _root.Q<VisualElement>("field").Add(target);
            FiberPortalRegistry.Register(TargetId, target);
            s_showPortal.Invoke(true);
            _mounted.FlushStateForTest();
            return target.Q<VisualElement>("content");
        }

        [Test]
        public void Given_AFocusInReachingPortalContentInsideATextField_When_TheFieldHasAnOnFocus_Then_OnlyTheLogicalAncestorHearsIt()
        {
            // Arrange
            var content = MountTextFieldAroundTarget(_ => s_log.Add("field"));

            // Act
            DispatchFocus<FocusInEvent>(content);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("caller"));
        }

        private static void DispatchFocus<TEvent>(VisualElement target) where TEvent : FocusEventBase<TEvent>, new()
        {
            using (var evt = FocusEventBase<TEvent>.GetPooled(target, null, FocusChangeDirection.unspecified, target.focusController))
            {
                target.SendEvent(evt);
            }
        }

        [Test]
        public void Given_AFocusOutRaisedInPortalContentInsideATextField_When_TheKeyboardReportsDone_Then_OnlyAFocusOutOnTheFieldItselfSubmits()
        {
            // Arrange
            var content = MountTextFieldAroundTarget(_ => { });
            var field = _root.Q<TextField>("field");
            var submits = new List<bool>();
            field.RegisterCallback<FocusOutEvent>(
                evt => submits.Add(FiberEventBindingManager.SubmitsOnFocusOut(field, evt, UnityEngine.TouchScreenKeyboard.Status.Done)),
                TrickleDown.TrickleDown);

            // Act
            DispatchFocus<FocusOutEvent>(content);
            DispatchFocus<FocusOutEvent>(field);

            // Assert
            Assert.That(string.Join(",", submits), Is.EqualTo("False,True"));
        }

        // GREEN_ON_BASE(characterization): the base runs an element above both the target and the call site once.
        // This pins that the native capture registration still reaches such an element for a wheel.
        [Test]
        public void Given_AnElementHoldingBothThePortalTargetAndTheCallSite_When_AWheelReachesTheContent_Then_ItsCaptureHandlerRunsOnce()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Motion(name: "shared", events: Wheel("shared", true), children: new VNode[]
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
            Dispatch(WheelEvent.GetPooled(), target.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("shared"));
        }

        [Component]
        private static VNode WheelCaptureToggleRender()
        {
            var (on, setOn) = Hooks.UseState(true);
            s_showButton = setOn;
            return V.Motion(name: "shared", events: on ? Wheel("shared", true) : null, children: new VNode[] { V.Div(name: "inner") });
        }

        // GREEN_ON_BASE(characterization): the base unregisters a capture handler the render dropped.
        // This pins that the guard added around it leaves the unregistration in place.
        [Test]
        public void Given_AWheelCaptureHandlerThatARenderDrops_When_AWheelReachesTheElement_Then_ItDoesNotRun()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(WheelCaptureToggleRender));
            s_showButton.Invoke(false);
            _mounted.FlushStateForTest();

            // Act
            Dispatch(WheelEvent.GetPooled(), _root.Q<VisualElement>("inner"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo(""));
        }

        [Component]
        private static VNode TwoPortalsRender()
        {
            var (first, setFirst) = Hooks.UseState(true);
            var (second, setSecond) = Hooks.UseState(true);
            s_showFirst = setFirst;
            s_showSecond = setSecond;
            return V.Fragment(children: new VNode[]
            {
                V.When(first, () => V.Portal("off-chain-first", children: new VNode[] { V.Div(name: "first") })),
                V.When(second, () => V.Portal("off-chain-second", children: new VNode[] { V.Div(name: "second") })),
            });
        }

        private static int BridgedContexts()
        {
            var field = typeof(FiberCrossPanelEventDispatcher).GetField("s_bridged", BindingFlags.Static | BindingFlags.NonPublic);
            return (field?.GetValue(null) as IEnumerable)?.Cast<object>().Count() ?? 0;
        }

        [Test]
        public void Given_TwoPortalsInOneMount_When_TheyUnmountOneAfterTheOther_Then_TheMountIsHeldUntilTheLastOneGoes()
        {
            // Arrange
            var before = BridgedContexts();
            var firstTarget = new VisualElement();
            var secondTarget = new VisualElement();
            _window.rootVisualElement.Add(firstTarget);
            _window.rootVisualElement.Add(secondTarget);
            FiberPortalRegistry.Register("off-chain-first", firstTarget);
            FiberPortalRegistry.Register("off-chain-second", secondTarget);
            _mounted = V.Mount(_root, V.Component(TwoPortalsRender));
            var held = BridgedContexts() - before;

            // Act
            s_showFirst.Invoke(false);
            _mounted.FlushStateForTest();
            var afterFirst = BridgedContexts() - before;
            s_showSecond.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((held, afterFirst, BridgedContexts() - before), Is.EqualTo((1, 1, 0)));
        }

        private static readonly FieldInfo s_gatesField =
            typeof(FiberEventBindingManager).GetField("_clickGates", BindingFlags.Instance | BindingFlags.NonPublic);

        private FiberEventBindingManager EventManager => _mounted.Root.Reconciler.Context.EventManager;

        private static bool OffChainOf(object gate) => (bool)gate.GetType().GetField("OffChain").GetValue(gate);

        private static void SetOffChain(object gate, bool value) => gate.GetType().GetField("OffChain").SetValue(gate, value);

        // What a Button's press-origin callbacks still do after unwire ran: a callback left registered writes
        // the origin it was given, so each of the four is read apart from the others.
        private (bool AfterUp, bool AfterCancel, bool AfterDown, bool AfterSubmit) ReadingsAfter(
            Button button, System.Action unwire)
        {
            var gates = s_gatesField?.GetValue(EventManager) as IDictionary;
            if (gates == null || !gates.Contains(button)) return default;
            var gate = gates[button];
            var content = button.Q<VisualElement>("content");
            unwire();
            SetOffChain(gate, true);
            Dispatch(PointerUpEvent.GetPooled(), button);
            var afterUp = OffChainOf(gate);
            SetOffChain(gate, true);
            Dispatch(EventBase<PointerCancelEvent>.GetPooled(), button);
            var afterCancel = OffChainOf(gate);
            SetOffChain(gate, false);
            Dispatch(PointerDownEvent.GetPooled(), content);
            var afterDown = OffChainOf(gate);
            SetOffChain(gate, false);
            Dispatch(EventBase<NavigationSubmitEvent>.GetPooled(), content);
            return (afterUp, afterCancel, afterDown, OffChainOf(gate));
        }

        [Test]
        public void Given_AButtonWithAnOnClickThatTheManagerForgets_When_PointerEventsReachItAfterwards_Then_NoneOfItsPressCallbacksStillWritesTheOrigin()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();

            var forget = typeof(FiberEventBindingManager).GetMethod("Forget", BindingFlags.Instance | BindingFlags.NonPublic);
            var manager = EventManager;

            // Act
            var readings = ReadingsAfter(button, () => forget.Invoke(manager, new object[] { button }));

            // Assert
            Assert.That(readings, Is.EqualTo((true, true, false, false)));
        }

        [Test]
        public void Given_AButtonWithAnOnClickThatTheManagerClears_When_PointerEventsReachItAfterwards_Then_NoneOfItsPressCallbacksStillWritesTheOrigin()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            var manager = EventManager;

            // Act
            var readings = ReadingsAfter(button, manager.Clear);

            // Assert
            Assert.That((readings, ClickGates(manager)), Is.EqualTo(((true, true, false, false), 0)));
        }
    }
}
