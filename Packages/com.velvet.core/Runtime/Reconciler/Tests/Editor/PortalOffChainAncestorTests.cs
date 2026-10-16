using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.Tests.PortalOffChainSupport;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that an element holding a <c>V.Portal</c>'s target without being a logical ancestor of the
    /// portal's call site runs none of its <c>events:</c> handlers for an event raised in the portal's
    /// content, in either phase, as React's synthetic dispatch walks the fiber tree and never the container
    /// the content is attached to. The logical ancestors keep their handlers, once in each phase.
    /// </summary>
    [TestFixture]
    internal sealed class PortalOffChainAncestorTests : PortalOffChainFixtureBase
    {
        private static StateUpdater<bool> s_showFirst;
        private static StateUpdater<bool> s_showSecond;
        private static StateUpdater<bool> s_showButton;

        [SetUp]
        public void ResetStateUpdaters()
        {
            s_showFirst = default;
            s_showSecond = default;
            s_showButton = default;
        }

        [Test]
        public void Given_APhysicalOnlyAncestorOfThePortalTarget_When_APointerDownReachesTheContent_Then_OnlyTheLogicalAncestorRunsItsBubbleHandler()
        {
            // Arrange
            var content = MountHolderBesideCaller(
                typeof(VisualElement), BindPointerDown("holder", false), typeof(VisualElement), BindPointerDown("caller", false));

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
                typeof(VisualElement), BindPointerDown("holder", true), typeof(VisualElement), BindPointerDown("caller", true));

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
                typeof(VisualElement), BindWheel("holder", true), typeof(VisualElement), BindWheel("caller", true));

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
                typeof(VisualElement), BindWheel("holder", false), typeof(VisualElement), BindWheel("caller", false));

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
                V.Motion(name: "container", events: BindPointerDown("container", false), refCallback: RegisterTarget),
                V.Motion(events: BindPointerDown("caller", false), children: new VNode[]
                {
                    V.Portal(TargetId, children: new VNode[] { V.Div(name: "content") }),
                }),
            }));

            _mounted.FlushStateForTest();

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
            _mounted.FlushStateForTest();
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

        // Raises the click between the gate's decision and its clearing, which is when a click arising from the
        // event being dispatched is raised.
        private static void ClickDuring<TEvent>(Button button, System.Action raise) where TEvent : EventBase<TEvent>, new()
        {
            button.RegisterCallback<TEvent>(_ => button.SimulateClick(), TrickleDown.TrickleDown);
            raise();
        }

        private static PointerDownEvent TouchDown(int fingerId) =>
            PointerDownEvent.GetPooled(new UnityEngine.Touch { fingerId = fingerId });

        private static PointerUpEvent TouchUp(int fingerId) =>
            PointerUpEvent.GetPooled(new UnityEngine.Touch { fingerId = fingerId });

        [Test]
        public void Given_APhysicalOnlyButtonAroundThePortalTarget_When_APressAndReleaseInTheContentRaiseAClick_Then_ItsOnClickDoesNotRun()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            var content = button.Q<VisualElement>("content");
            Dispatch(PointerDownEvent.GetPooled(), content);

            // Act
            ClickDuring<PointerUpEvent>(button, () => Dispatch(PointerUpEvent.GetPooled(), content));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo(""));
        }

        [Test]
        public void Given_APhysicalOnlyButtonAroundThePortalTarget_When_ASubmitInTheContentRaisesAClick_Then_ItsOnClickDoesNotRun()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            var content = button.Q<VisualElement>("content");

            // Act
            ClickDuring<NavigationSubmitEvent>(button, () => Dispatch(EventBase<NavigationSubmitEvent>.GetPooled(), content));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo(""));
        }

        [Test]
        public void Given_APhysicalOnlyButtonWithAClickedEventBinding_When_APressAndReleaseInTheContentRaiseAClick_Then_ItDoesNotRun()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller(new FiberEventBinding[]
            {
                new ClickedEventBinding { Handler = _ => s_log.Add("click-event") },
            });
            var content = button.Q<VisualElement>("content");
            Dispatch(PointerDownEvent.GetPooled(), content);

            // Act
            ClickDuring<PointerUpEvent>(button, () => Dispatch(PointerUpEvent.GetPooled(), content));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo(""));
        }

        // GREEN_ON_BASE(characterization): the base runs a Button's click whatever began it.
        // This pins that a press in the content released over the Button's own area clicks the Button, which is
        // the common ancestor of the two.
        [Test]
        public void Given_APressInTheContent_When_ItIsReleasedOverTheButtonsOwnArea_Then_ItsOnClickRuns()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            Dispatch(PointerDownEvent.GetPooled(), button.Q<VisualElement>("content"));

            // Act
            ClickDuring<PointerUpEvent>(button, () => Dispatch(PointerUpEvent.GetPooled(), button.Q<VisualElement>("slot")));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("click"));
        }

        // GREEN_ON_BASE(characterization): the base runs a Button's click whatever began it.
        // This pins that a press on the Button's own area released in the content clicks the Button as well.
        [Test]
        public void Given_APressOnTheButtonsOwnArea_When_ItIsReleasedInTheContent_Then_ItsOnClickRuns()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            Dispatch(PointerDownEvent.GetPooled(), button.Q<VisualElement>("slot"));

            // Act
            ClickDuring<PointerUpEvent>(button, () => Dispatch(PointerUpEvent.GetPooled(), button.Q<VisualElement>("content")));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("click"));
        }

        // GREEN_ON_BASE(characterization): the base runs a Button's click whatever began it.
        // This pins that a press cancelled in the content leaves a later click alone.
        [Test]
        public void Given_APressInTheContentThatWasCancelled_When_TheButtonIsClicked_Then_ItsOnClickRuns()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            var content = button.Q<VisualElement>("content");
            Dispatch(PointerDownEvent.GetPooled(), content);
            Dispatch(EventBase<PointerCancelEvent>.GetPooled(), button);

            // Act
            ClickDuring<PointerUpEvent>(button, () => Dispatch(PointerUpEvent.GetPooled(), content));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("click"));
        }

        // GREEN_ON_BASE(characterization): the base runs a Button's click whatever began it.
        // This pins that a press cancelled in the content leaves a click raised by other means alone.
        [Test]
        public void Given_APressInTheContentThatWasCancelled_When_TheButtonIsClickedByOtherMeans_Then_ItsOnClickRuns()
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
        // This pins that a release in the content that raised no click leaves a click raised by other means alone.
        [Test]
        public void Given_APressAndReleaseInTheContentThatRaisedNoClick_When_TheButtonIsClickedAfterwards_Then_ItsOnClickRuns()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            var content = button.Q<VisualElement>("content");
            Dispatch(PointerDownEvent.GetPooled(), content);
            Dispatch(PointerUpEvent.GetPooled(), content);

            // Act
            button.SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("click"));
        }

        // GREEN_ON_BASE(characterization): the base runs a Button's click whatever began it.
        // This pins that a submit in the content, once handled, leaves a click raised by other means alone.
        [Test]
        public void Given_ASubmitInTheContent_When_TheButtonIsClickedAfterwards_Then_ItsOnClickRuns()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            Dispatch(EventBase<NavigationSubmitEvent>.GetPooled(), button.Q<VisualElement>("content"));

            // Act
            button.SimulateClick();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("click"));
        }

        [Test]
        public void Given_APressInTheContentOfOnePointer_When_AnotherPointerPressesAndReleasesInTheContent_Then_ItsOnClickDoesNotRun()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            var content = button.Q<VisualElement>("content");
            Dispatch(TouchDown(1), content);
            Dispatch(TouchDown(2), button.Q<VisualElement>("slot"));

            // Act
            ClickDuring<PointerUpEvent>(button, () => Dispatch(TouchUp(1), content));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo(""));
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
        public void Given_APhysicalOnlyButtonBoundAgainByARender_When_APressInTheContentIsReleasedThere_Then_ItsOnClickDoesNotRun()
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
            var content = target.Q<VisualElement>("content");
            Dispatch(PointerDownEvent.GetPooled(), content);
            var rendersBefore = s_clickRenders;
            s_showButton.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            ClickDuring<PointerUpEvent>(_root.Q<Button>("holder"), () => Dispatch(PointerUpEvent.GetPooled(), content));

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
                name: "modal-root", events: BindPointerDown("modal-root", false), refCallback: RegisterTarget));
            _mounted = V.Mount(_root, V.Motion(events: BindPointerDown("caller", false), children: new VNode[]
            {
                V.Portal(TargetId, children: new VNode[] { V.Div(name: "content") }),
            }));

            // Act
            Dispatch(PointerDownEvent.GetPooled(), rootOfOther.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("caller"));
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

        // GREEN_ON_BASE(characterization): the base runs an element above both the target and the call site once.
        // This pins that the native capture registration still reaches such an element for a wheel.
        [Test]
        public void Given_AnElementHoldingBothThePortalTargetAndTheCallSite_When_AWheelReachesTheContent_Then_ItsCaptureHandlerRunsOnce()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Motion(name: "shared", events: BindWheel("shared", true), children: new VNode[]
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
            return V.Motion(name: "shared", events: on ? BindWheel("shared", true) : null, children: new VNode[] { V.Div(name: "inner") });
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

        [Component]
        private static VNode DetachingChildRender()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_showButton = setShow;
            return V.Motion(name: "ancestor", events: BindPointerDown("ancestor", false), children: new VNode[]
            {
                V.When(show, () => V.Motion(name: "child", events: new FiberEventBinding[]
                {
                    new PointerDownBinding { Handler = _ => { s_log.Add("child"); s_showButton.Invoke(false); } },
                })),
            });
        }

        // GREEN_ON_BASE(characterization): the base runs an ancestor's handler after a child's handler removed the child.
        // This pins that the off-chain check, once a portal is mounted, still runs that ancestor.
        [Test]
        public void Given_APortalIsMounted_When_AChildsPointerDownHandlerRemovesTheChild_Then_ItsLogicalAncestorStillRunsItsHandler()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Component(DetachingChildRender),
                V.Div(name: "slot"),
                V.Component(CallerRender),
            }));
            var target = new VisualElement();
            _root.Q<VisualElement>("slot").Add(target);
            FiberPortalRegistry.Register(TargetId, target);
            s_showPortal.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            Dispatch(PointerDownEvent.GetPooled(), _root.Q<VisualElement>("child"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("child,ancestor"));
        }

        [Test]
        public void Given_AMountHoldingAPortal_When_TheStaticStateIsReset_Then_NoContextIsHeldAnyMore()
        {
            // Arrange
            var before = BridgedContexts();
            var target = new VisualElement();
            _window.rootVisualElement.Add(target);
            FiberPortalRegistry.Register("off-chain-first", target);
            _mounted = V.Mount(_root, V.Portal("off-chain-first", children: new VNode[] { V.Div(name: "content") }));
            var held = BridgedContexts() - before;

            // Act
            typeof(FiberCrossPanelEventDispatcher)
                .GetMethod("ResetStaticFields", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

            // Assert
            Assert.That((held, BridgedContexts()), Is.EqualTo((1, 0)));
        }

        private static readonly FieldInfo s_gatesField =
            typeof(FiberEventBindingManager).GetField("_clickGates", BindingFlags.Instance | BindingFlags.NonPublic);

        private FiberEventBindingManager EventManager => _mounted.Root.Reconciler.Context.EventManager;

        private static object GateField(object gate, string name) => gate.GetType().GetField(name).GetValue(gate);

        private static void SetSuppress(object gate, bool value) => gate.GetType().GetField("Suppress").SetValue(gate, value);

        private static ISet<int> OffChainDowns(object gate) => (ISet<int>)GateField(gate, "OffChainDowns");

        // What a Button's gate callbacks still do after unwire ran. A callback left registered changes what it
        // was written to change, so each reading starts from a state that callback would move.
        private (int AfterDown, (bool, bool) AfterUp, (bool, bool) AfterCancel, bool AfterSubmit) ReadingsAfter(
            Button button, System.Action unwire)
        {
            var gates = s_gatesField?.GetValue(EventManager) as IDictionary;
            if (gates == null || !gates.Contains(button)) return default;
            var gate = gates[button];
            var content = button.Q<VisualElement>("content");
            unwire();
            Dispatch(PointerDownEvent.GetPooled(), content);
            var afterDown = OffChainDowns(gate).Count;
            OffChainDowns(gate).Clear();

            var up = PointerUpEvent.GetPooled();
            var upId = up.pointerId;
            OffChainDowns(gate).Add(upId);
            SetSuppress(gate, true);
            Dispatch(up, content);
            var afterUp = (OffChainDowns(gate).Contains(upId), (bool)GateField(gate, "Suppress"));

            var cancel = EventBase<PointerCancelEvent>.GetPooled();
            var cancelId = cancel.pointerId;
            OffChainDowns(gate).Add(cancelId);
            SetSuppress(gate, true);
            Dispatch(cancel, button);
            var afterCancel = (OffChainDowns(gate).Contains(cancelId), (bool)GateField(gate, "Suppress"));

            SetSuppress(gate, true);
            Dispatch(EventBase<NavigationSubmitEvent>.GetPooled(), button.Q<VisualElement>("slot"));
            return (afterDown, afterUp, afterCancel, (bool)GateField(gate, "Suppress"));
        }

        [Test]
        public void Given_AButtonWithAnOnClickThatTheManagerForgets_When_PointerEventsReachItAfterwards_Then_NoneOfItsGateCallbacksStillRuns()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();

            var forget = typeof(FiberEventBindingManager).GetMethod("Forget", BindingFlags.Instance | BindingFlags.NonPublic);
            var manager = EventManager;

            // Act
            var readings = ReadingsAfter(button, () => forget.Invoke(manager, new object[] { button }));

            // Assert
            Assert.That(readings, Is.EqualTo((0, (true, true), (true, true), true)));
        }

        [Test]
        public void Given_AButtonWithAnOnClickThatTheManagerClears_When_PointerEventsReachItAfterwards_Then_NoneOfItsGateCallbacksStillRuns()
        {
            // Arrange
            var button = MountButtonHolderBesideCaller();
            var manager = EventManager;

            // Act
            var readings = ReadingsAfter(button, manager.Clear);

            // Assert
            Assert.That((readings, ClickGates(manager)), Is.EqualTo(((0, (true, true), (true, true), true), 0)));
        }
    }
}
