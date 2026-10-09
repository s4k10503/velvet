using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="FiberDispatchedEventBinding.Capture"/>, React's <c>on…Capture</c>, against a live
    /// panel dispatch: capture handlers run outermost first and ahead of every bubble handler, the target's
    /// own included; one delegate bound in both phases runs in both; a render that moves a handler between
    /// phases rebinds it; and a logical ancestor of a portal's call site captures ahead of the portal's
    /// content.
    /// </summary>
    [TestFixture]
    internal sealed class CaptureEventBindingTests : PanelTestBase
    {
        private const string PortalTargetId = "capture-target";

        private static readonly List<string> s_log = new();
        private static StateUpdater<bool> s_setCapture;

        private VisualElement _portalTarget;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_log.Clear();
            s_setCapture = default;
            s_showPortal = default;
            RuntimeStateProbe.ClearPortalRegistry();
            _portalTarget = new VisualElement();
            _window.rootVisualElement.Add(_portalTarget);
            FiberPortalRegistry.Register(PortalTargetId, _portalTarget);
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            RuntimeStateProbe.ClearPortalRegistry();
        }

        private static void Dispatch(VisualElement target)
        {
            using var evt = PointerDownEvent.GetPooled();
            evt.target = target;
            target.SendEvent(evt);
        }

        private static PointerDownBinding Log(string entry, bool capture) =>
            new() { Handler = _ => s_log.Add(entry), Capture = capture };

        private static void LogTarget(PointerDownEvent _) => s_log.Add("handler");

        [Test]
        public void Given_AParentAndATargetEachWithACaptureAndABubbleHandler_When_APointerDownReachesTheTarget_Then_TheyRunCaptureDownThenBubbleUp()
        {
            // Arrange — each array lists its bubble handler first, so registration order alone would put
            // every bubble handler ahead of its element's capture handler.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                events: new FiberEventBinding[] { Log("parent-bubble", false), Log("parent-capture", true) },
                children: new VNode[]
                {
                    V.Div(name: "target", events: new FiberEventBinding[]
                    {
                        Log("target-bubble", false), Log("target-capture", true),
                    }),
                }));

            // Act
            Dispatch(_window.rootVisualElement.Q<VisualElement>("target"));

            // Assert
            Assert.That(string.Join(",", s_log),
                Is.EqualTo("parent-capture,target-capture,target-bubble,parent-bubble"));
        }

        [Test]
        public void Given_OneDelegateBoundForCaptureAndForBubble_When_APointerDownReachesTheElement_Then_ItRunsTwice()
        {
            // Arrange — React registers onPointerDown and onPointerDownCapture separately, the same function
            // in both included.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "target", events: new FiberEventBinding[]
            {
                new PointerDownBinding { Handler = LogTarget },
                new PointerDownBinding { Handler = LogTarget, Capture = true },
            }));

            // Act
            Dispatch(_window.rootVisualElement.Q<VisualElement>("target"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("handler,handler"));
        }

        private static void LogAncestor(PointerDownEvent _) => s_log.Add("ancestor");

        [Component]
        private static VNode PhaseSwitchingAncestor()
        {
            var (capture, setCapture) = Hooks.UseState(false);
            s_setCapture = setCapture;
            return V.Div(
                events: new FiberEventBinding[] { new PointerDownBinding { Handler = LogAncestor, Capture = capture } },
                children: new VNode[]
                {
                    V.Div(name: "target", events: new FiberEventBinding[] { Log("target", false) }),
                });
        }

        [Test]
        public void Given_AnAncestorHandlerARenderMovesToCapture_When_APointerDownReachesItsDescendant_Then_TheAncestorRunsFirst()
        {
            // Arrange — the delegate stays the same across the two renders; only its phase moves.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(PhaseSwitchingAncestor));
            s_setCapture.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            Dispatch(_window.rootVisualElement.Q<VisualElement>("target"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("ancestor,target"));
        }

        [Test]
        public void Given_ALogicalAncestorOfAPortalCapturingAndBubbling_When_APointerDownReachesThePortalsContent_Then_ItCapturesFirstAndBubblesLast()
        {
            // Arrange — the portal's content mounts under a target off the ancestor's physical chain, so
            // only the bridge can reach the ancestor at all.
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            _mounted = V.Mount(host, V.Div(
                events: new FiberEventBinding[] { Log("ancestor-bubble", false), Log("ancestor-capture", true) },
                children: new VNode[]
                {
                    V.Portal(PortalTargetId, children: new VNode[]
                    {
                        V.Div(name: "content", events: new FiberEventBinding[] { Log("content", false) }),
                    }),
                }));

            // Act
            Dispatch(_portalTarget.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("ancestor-capture,content,ancestor-bubble"));
        }

        // Each event the bridge carries, with a capture binding for it that logs "ancestor" and a dispatch
        // of it to an element.
        private static IEnumerable<(string Kind, Func<FiberEventBinding> Binding, Action<VisualElement> Dispatch)> BridgedCaptureKinds()
        {
            yield return Kind<PointerDownEvent>("PointerDown", () => new PointerDownBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<PointerUpEvent>("PointerUp", () => new PointerUpBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<PointerMoveEvent>("PointerMove", () => new PointerMoveBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<PointerEnterEvent>("PointerEnter", () => new PointerEnterBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<PointerLeaveEvent>("PointerLeave", () => new PointerLeaveBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<WheelEvent>("Wheel", () => new WheelBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<KeyDownEvent>("KeyDown", () => new KeyDownBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<KeyUpEvent>("KeyUp", () => new KeyUpBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<FocusInEvent>("FocusIn", () => new FocusInBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<FocusOutEvent>("FocusOut", () => new FocusOutBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
        }

        private static (string, Func<FiberEventBinding>, Action<VisualElement>) Kind<TEvent>(
            string name, Func<FiberEventBinding> binding)
            where TEvent : EventBase<TEvent>, new()
        {
            Action<VisualElement> dispatch = target =>
            {
                using var evt = EventBase<TEvent>.GetPooled();
                evt.target = target;
                target.SendEvent(evt);
            };
            return (name, binding, dispatch);
        }

        private static IEnumerable<TestCaseData> CapturedThroughThePortal() => BridgedCaptureKinds().Select(kind =>
            new TestCaseData(kind.Binding, kind.Dispatch).SetName(
                $"Given_ALogicalAncestorOfAPortalCapturing{kind.Kind}_When_ItReachesThePortalsContent_Then_TheAncestorCapturesIt"));

        private static IEnumerable<TestCaseData> CapturedAfterARemount() => BridgedCaptureKinds().Select(kind =>
            new TestCaseData(kind.Binding, kind.Dispatch).SetName(
                $"Given_APortalRemountedUnderAnAncestorCapturing{kind.Kind}_When_ItReachesTheContent_Then_TheAncestorCapturesItOnce"));

        private VisualElement MountCaptureAroundPortal(FiberEventBinding binding, VNode portalCaller)
        {
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            _mounted = V.Mount(host, V.Div(events: new FiberEventBinding[] { binding }, children: new[] { portalCaller }));
            return host;
        }

        [TestCaseSource(nameof(CapturedThroughThePortal))]
        public void Given_ALogicalAncestorOfAPortalCapturingAnEventKind_When_ThatEventReachesThePortalsContent_Then_TheAncestorCapturesIt(
            Func<FiberEventBinding> binding, Action<VisualElement> dispatch)
        {
            // Arrange
            MountCaptureAroundPortal(binding(), V.Portal(PortalTargetId, children: new VNode[] { V.Div(name: "content") }));

            // Act
            dispatch(_portalTarget.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("ancestor"));
        }

        private static StateUpdater<bool> s_showPortal;

        [Component]
        private static VNode TogglingPortalCaller()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_showPortal = setShow;
            return show ? V.Portal(PortalTargetId, children: new VNode[] { V.Div(name: "content") }) : null;
        }

        [TestCaseSource(nameof(CapturedAfterARemount))]
        public void Given_APortalUnmountedAndMountedAgainUnderACapturingAncestor_When_TheEventReachesItsContent_Then_TheAncestorCapturesItOnce(
            Func<FiberEventBinding> binding, Action<VisualElement> dispatch)
        {
            // Arrange — the unmount releases the target's bridge and the mount attaches it again, so a
            // release that left its capture listener behind would carry the event twice.
            MountCaptureAroundPortal(binding(), V.Component(TogglingPortalCaller));
            s_showPortal.Invoke(false);
            _mounted.FlushStateForTest();
            s_showPortal.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            dispatch(_portalTarget.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("ancestor"));
        }

        [Test]
        public void Given_APortalTargetInsideAnotherPortalsTarget_When_APointerDownReachesTheInnerContent_Then_TheCapturingAncestorRunsOnce()
        {
            // Arrange — the inner target is a physical descendant of the outer one, so the event trickles
            // through both bridges.
            var inner = new VisualElement();
            _portalTarget.Add(inner);
            FiberPortalRegistry.Register("capture-inner", inner);
            MountCaptureAroundPortal(Log("ancestor", true), V.Portal(PortalTargetId, children: new VNode[]
            {
                V.Portal("capture-inner", children: new VNode[] { V.Div(name: "content") }),
            }));

            // Act
            Dispatch(inner.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("ancestor"));
        }

        [Test]
        public void Given_ACapturingElementThatIsBothAPhysicalAndALogicalAncestorOfAPortal_When_APointerDownReachesTheContent_Then_ItRunsOnce()
        {
            // Arrange — the portal renders into a container held directly, which is then put under the
            // element holding the call site: native dispatch reaches that element, and the bridge must not.
            var container = new VisualElement();
            _window.rootVisualElement.Add(container);
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            _mounted = V.Mount(host, V.Div(name: "outer", events: new FiberEventBinding[] { Log("outer", true) },
                children: new VNode[] { V.Portal(container, children: new VNode[] { V.Div(name: "content") }) }));
            host.Q<VisualElement>("outer").Add(container);

            // Act
            Dispatch(container.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("outer"));
        }

        [Test]
        public void Given_TwoCapturingLogicalAncestorsOfAPortalTheOuterOfWhichStopsPropagation_When_APointerDownReachesTheContent_Then_OnlyTheOuterRuns()
        {
            // Arrange
            var outer = new PointerDownBinding
            {
                Handler = evt =>
                {
                    s_log.Add("outer");
                    evt.StopPropagation();
                },
                Capture = true,
            };
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            _mounted = V.Mount(host, V.Div(events: new FiberEventBinding[] { outer }, children: new VNode[]
            {
                V.Div(events: new FiberEventBinding[] { Log("inner", true) }, children: new VNode[]
                {
                    V.Portal(PortalTargetId, children: new VNode[] { V.Div(name: "content") }),
                }),
            }));

            // Act
            Dispatch(_portalTarget.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("outer"));
        }
    }
}
