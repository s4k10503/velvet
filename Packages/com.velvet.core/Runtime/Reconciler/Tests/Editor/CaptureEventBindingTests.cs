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
    /// content and bubbles after it, in the order of the logical chain where one portal renders into
    /// another's content, and once however many bridges the content's physical path crosses.
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
            s_setContainer = default;
            s_setMoveBindings = default;
            s_setRender = default;
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
            yield return Kind<WheelEvent>("Wheel", () => new WheelBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<KeyDownEvent>("KeyDown", () => new KeyDownBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<KeyUpEvent>("KeyUp", () => new KeyUpBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<FocusInEvent>("FocusIn", () => new FocusInBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<FocusOutEvent>("FocusOut", () => new FocusOutBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<FocusEvent>("Focus", () => new FocusBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
            yield return Kind<BlurEvent>("Blur", () => new BlurBinding { Handler = _ => s_log.Add("ancestor"), Capture = true });
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

        private static FiberEventBinding[] Logged(string name, bool capture, bool bubble)
        {
            var events = new List<FiberEventBinding>();
            if (bubble) events.Add(Log(name + "<", false));
            if (capture) events.Add(Log(name + ">", true));
            return events.ToArray();
        }

        // A portal into the registered target holds a second portal, declared beside an element that is not
        // a Velvet node, whose content renders into that element: so the second portal's target sits inside
        // the first portal's content, and its content's physical path crosses both bridges.
        private VisualElement MountNestedPortals(bool capture, bool bubble, out VisualElement inner)
        {
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            inner = new VisualElement();
            _mounted = V.Mount(host, V.Div(events: Logged("A", capture, bubble), children: new VNode[]
            {
                V.Portal(PortalTargetId, children: new VNode[]
                {
                    V.Div(name: "C1", events: Logged("C1", capture, bubble), children: new VNode[]
                    {
                        V.Div(events: Logged("B", capture, bubble), children: new VNode[]
                        {
                            V.Portal(inner, children: new VNode[]
                            {
                                V.Div(events: Logged("C2", capture, bubble), children: new VNode[]
                                {
                                    V.Div(name: "L", events: Logged("L", capture, bubble)),
                                }),
                            }),
                        }),
                    }),
                }),
            }));
            _portalTarget.Q<VisualElement>("C1").Add(inner);
            return inner.Q<VisualElement>("L");
        }

        [Test]
        public void Given_APortalRenderingIntoAnotherPortalsContent_When_APointerDownReachesItsContent_Then_CaptureRunsDownTheLogicalChain()
        {
            // Arrange
            var target = MountNestedPortals(capture: true, bubble: false, out _);

            // Act
            Dispatch(target);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("A>,C1>,B>,C2>,L>"));
        }

        [Test]
        public void Given_APortalRenderingIntoAnotherPortalsContent_When_APointerDownReachesItsContent_Then_BubbleRunsUpTheLogicalChain()
        {
            // Arrange
            var target = MountNestedPortals(capture: false, bubble: true, out _);

            // Act
            Dispatch(target);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("L<,C2<,B<,C1<,A<"));
        }

        // Two portals declared side by side under Q: the first renders into the registered target, and the
        // second into an element that is not a Velvet node, put inside the first portal's content. The second
        // portal's content crosses both bridges on its physical path, while its logical chain runs through
        // B and Q only — A, the first portal's call site, is no ancestor of it.
        private VisualElement MountPortalIntoASiblingPortalsContent(bool capture, bool bubble)
        {
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            var inner = new VisualElement();
            _mounted = V.Mount(host, V.Div(events: Logged("Q", capture, bubble), children: new VNode[]
            {
                V.Div(events: Logged("A", capture, bubble), children: new VNode[]
                {
                    V.Portal(PortalTargetId, children: new VNode[] { V.Div(name: "C1") }),
                }),
                V.Div(events: Logged("B", capture, bubble), children: new VNode[]
                {
                    V.Portal(inner, children: new VNode[]
                    {
                        V.Div(events: Logged("C2", capture, bubble), children: new VNode[]
                        {
                            V.Div(name: "L", events: Logged("L", capture, bubble)),
                        }),
                    }),
                }),
            }));
            _portalTarget.Q<VisualElement>("C1").Add(inner);
            return inner.Q<VisualElement>("L");
        }

        [Test]
        public void Given_APortalRenderingIntoASiblingPortalsContent_When_APointerDownReachesItsContent_Then_CaptureRunsItsLogicalChainOnce()
        {
            // Arrange
            var target = MountPortalIntoASiblingPortalsContent(capture: true, bubble: false);

            // Act
            Dispatch(target);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("Q>,B>,C2>,L>"));
        }

        [Test]
        public void Given_APortalRenderingIntoASiblingPortalsContent_When_APointerDownReachesItsContent_Then_BubbleRunsItsLogicalChainOnce()
        {
            // Arrange
            var target = MountPortalIntoASiblingPortalsContent(capture: false, bubble: true);

            // Act
            Dispatch(target);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("L<,C2<,B<,Q<"));
        }

        [Component]
        private static VNode StoppingCallSiteInsideItsTarget()
        {
            var (container, setContainer) = Hooks.UseState((VisualElement)null);
            var (show, setShow) = Hooks.UseState(true);
            s_setContainer = setContainer;
            s_showPortal = setShow;
            return V.Div(
                events: Logged("Z", capture: false, bubble: true),
                refCallback: element =>
                {
                    s_setContainer.Invoke(element);
                    return () => { };
                },
                children: new VNode[]
                {
                    V.Div(name: "plain"),
                    V.Div(
                        events: new FiberEventBinding[]
                        {
                            new PointerDownBinding
                            {
                                Handler = evt =>
                                {
                                    s_log.Add("B<");
                                    evt.StopPropagation();
                                },
                            },
                        },
                        children: new VNode[]
                        {
                            container == null || !show ? null : V.Portal(container, children: new VNode[] { V.Div(name: "L") }),
                        }),
                });
        }

        [Test]
        public void Given_ACallSiteThatStoppedABubbleAtItsTarget_When_APointerDownReachesAnotherChild_Then_TheTargetBubbles()
        {
            // Arrange — the second dispatch carries no segment through the bridge that answered the first.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(StoppingCallSiteInsideItsTarget));
            _mounted.FlushStateForTest();
            Dispatch(_window.rootVisualElement.Q<VisualElement>("L"));

            // Act
            Dispatch(_window.rootVisualElement.Q<VisualElement>("plain"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("B<,Z<"));
        }

        [Test]
        public void Given_ACallSiteThatStoppedABubbleAtItsTarget_When_ThePortalUnmountsAndAPointerDownReachesAnotherChild_Then_TheTargetBubbles()
        {
            // Arrange — the second dispatch can take the pooled event the first gave back, which a prelude
            // left behind on the target would answer from the first dispatch's stop.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(StoppingCallSiteInsideItsTarget));
            _mounted.FlushStateForTest();
            Dispatch(_window.rootVisualElement.Q<VisualElement>("L"));
            s_showPortal.Invoke(false);
            _mounted.FlushStateForTest();

            // Act
            Dispatch(_window.rootVisualElement.Q<VisualElement>("plain"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("B<,Z<"));
        }

        private static StateUpdater<VisualElement> s_setContainer;

        [Component]
        private static VNode PortalIntoItsOwnAncestor()
        {
            var (container, setContainer) = Hooks.UseState((VisualElement)null);
            s_setContainer = setContainer;
            return V.Div(
                events: Logged("Z", capture: false, bubble: true),
                refCallback: element =>
                {
                    s_setContainer.Invoke(element);
                    return () => { };
                },
                children: new VNode[]
                {
                    V.Div(events: Logged("B", capture: false, bubble: true), children: new VNode[]
                    {
                        container == null ? null : V.Portal(container, children: new VNode[]
                        {
                            V.Div(name: "L", events: Logged("L", capture: false, bubble: true)),
                        }),
                    }),
                });
        }

        [Test]
        public void Given_APortalIntoAVelvetElementAboveItsCallSite_When_APointerDownBubblesFromItsContent_Then_TheCallSiteRunsBeforeThatElement()
        {
            // Arrange — the container's own bubble handler was bound before the portal attached its bridge,
            // and the call site sits between the two in the logical chain.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(PortalIntoItsOwnAncestor));
            _mounted.FlushStateForTest();

            // Act
            Dispatch(_window.rootVisualElement.Q<VisualElement>("L"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("L<,B<,Z<"));
        }

        private static void Ignore(PointerMoveEvent _) { }

        [Test]
        public void Given_ACapturingAncestorOfAPortal_When_PointerMovesReachItsContent_Then_TheBridgeAllocatesNothingAnElementBesideTheContentDoesNot()
        {
            // Arrange — the plain element shares the content's depth under the target but is no portal row,
            // so both dispatches cross the anchor and only the content's carries a segment.
            var plain = new VisualElement();
            _portalTarget.Add(plain);
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            _mounted = V.Mount(host, V.Div(
                events: new FiberEventBinding[]
                {
                    new PointerMoveBinding { Handler = Ignore, Capture = true },
                    new PointerMoveBinding { Handler = Ignore },
                },
                children: new VNode[] { V.Portal(PortalTargetId, children: new VNode[] { V.Div(name: "content") }) }));
            var content = _portalTarget.Q<VisualElement>("content");
            Action toContent = () =>
            {
                using var evt = PointerMoveEvent.GetPooled();
                evt.target = content;
                content.SendEvent(evt);
            };
            Action toPlain = () =>
            {
                using var evt = PointerMoveEvent.GetPooled();
                evt.target = plain;
                plain.SendEvent(evt);
            };
            Action canary = () => GC.KeepAlive(new byte[16]);
            toContent();
            toPlain();
            canary();

            // Act
            var cost = (GCAllocationProbe.MedianBlocksDuring(toContent) - GCAllocationProbe.MedianBlocksDuring(toPlain),
                GCAllocationProbe.MedianBlocksDuring(canary) > 0);

            // Assert
            Assert.That(cost, Is.EqualTo((0, true)));
        }

        [Test]
        public void Given_ABubblingLogicalAncestorOfAPortal_When_TwoPointerDownsReachTheContentInTurn_Then_ItRunsForEach()
        {
            // Arrange — the second dispatch can take the pooled event the first gave back, which the bridge
            // must read as a new dispatch.
            MountCaptureAroundPortal(Log("ancestor", false), V.Portal(PortalTargetId, children: new VNode[] { V.Div(name: "content") }));
            var content = _portalTarget.Q<VisualElement>("content");

            // Act
            Dispatch(content);
            Dispatch(content);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("ancestor,ancestor"));
        }

        [Test]
        public void Given_APortalUnmountedAndMountedAgainUnderABubblingAncestor_When_APointerDownReachesItsContent_Then_TheAncestorRunsOnce()
        {
            // Arrange — the bubble counterpart of the remount cases above.
            MountCaptureAroundPortal(Log("ancestor", false), V.Component(TogglingPortalCaller));
            s_showPortal.Invoke(false);
            _mounted.FlushStateForTest();
            s_showPortal.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            Dispatch(_portalTarget.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("ancestor"));
        }

        [Test]
        public void Given_APortalIntoAScrollViewsContent_When_APointerDownReachesThePortalsContent_Then_TheLogicalAncestorCapturesAndBubbles()
        {
            // Arrange — the bridge listens on the ScrollView, so the content sits several elements below its
            // anchor rather than directly under it.
            var scrollView = new ScrollView();
            _window.rootVisualElement.Add(scrollView);
            FiberPortalRegistry.Register("capture-scroll", scrollView.contentContainer);
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            _mounted = V.Mount(host, V.Div(events: Logged("A", capture: true, bubble: true), children: new VNode[]
            {
                V.Portal("capture-scroll", children: new VNode[] { V.Div(name: "content") }),
            }));

            // Act
            Dispatch(scrollView.Q<VisualElement>("content"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("A>,A<"));
        }

        private static StateUpdater<bool> s_setMoveBindings;

        private static void LogMoveCapture(PointerMoveEvent _) => s_log.Add("capture");

        private static void LogMoveBubble(PointerMoveEvent _) => s_log.Add("bubble");

        [Component]
        private static VNode MoveBindingsARenderDrops()
        {
            var (bound, setBound) = Hooks.UseState(true);
            s_setMoveBindings = setBound;
            return V.Div(
                events: bound
                    ? new FiberEventBinding[]
                    {
                        new PointerMoveBinding { Handler = LogMoveBubble },
                        new PointerMoveBinding { Handler = LogMoveCapture, Capture = true },
                    }
                    : null,
                children: new VNode[] { V.Div(name: "target") });
        }

        private static void DispatchMove(VisualElement target)
        {
            using var evt = PointerMoveEvent.GetPooled();
            evt.target = target;
            target.SendEvent(evt);
        }

        [Test]
        public void Given_PointerMoveBindingsInBothPhasesThatARenderDrops_When_PointerMovesReachADescendantBeforeAndAfter_Then_OnlyTheFirstRunsThem()
        {
            // Arrange — pointer move binds without the discrete bracket, through its own registration path.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(MoveBindingsARenderDrops));
            var target = _window.rootVisualElement.Q<VisualElement>("target");
            DispatchMove(target);

            // Act
            s_setMoveBindings.Invoke(false);
            _mounted.FlushStateForTest();
            DispatchMove(target);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("capture,bubble"));
        }

        [Component]
        private static VNode MovePortalIntoItsOwnAncestor()
        {
            var (container, setContainer) = Hooks.UseState((VisualElement)null);
            s_setContainer = setContainer;
            return V.Div(
                events: new FiberEventBinding[] { new PointerMoveBinding { Handler = _ => s_log.Add("Z<") } },
                refCallback: element =>
                {
                    s_setContainer.Invoke(element);
                    return () => { };
                },
                children: new VNode[]
                {
                    V.Div(events: new FiberEventBinding[] { new PointerMoveBinding { Handler = _ => s_log.Add("B<") } },
                        children: new VNode[]
                        {
                            container == null ? null : V.Portal(container, children: new VNode[] { V.Div(name: "L") }),
                        }),
                });
        }

        [Test]
        public void Given_APortalIntoAVelvetElementAboveItsCallSite_When_APointerMoveBubblesFromItsContent_Then_TheCallSiteRunsBeforeThatElement()
        {
            // Arrange — the pointer-down case above, through the registration path pointer move takes.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(MovePortalIntoItsOwnAncestor));
            _mounted.FlushStateForTest();

            // Act
            DispatchMove(_window.rootVisualElement.Q<VisualElement>("L"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("B<,Z<"));
        }

        private static StateUpdater<int> s_setRender;

        [Component]
        private static VNode CapturingContainerItsOwnPortalRendersInto()
        {
            var (container, setContainer) = Hooks.UseState((VisualElement)null);
            var (render, setRender) = Hooks.UseState(0);
            var (show, setShow) = Hooks.UseState(true);
            s_setContainer = setContainer;
            s_setRender = setRender;
            s_showPortal = setShow;
            return V.Div(
                // A handler of its own on every render, so each render binds the container's bindings again.
                events: new FiberEventBinding[] { new PointerDownBinding { Handler = _ => s_log.Add("Z" + render + ">"), Capture = true } },
                refCallback: element =>
                {
                    s_setContainer.Invoke(element);
                    return () => { };
                },
                children: new VNode[]
                {
                    V.Div(events: new FiberEventBinding[] { Log("B>", true) }, children: new VNode[]
                    {
                        container == null || !show ? null : V.Portal(container, children: new VNode[] { V.Div(name: "L") }),
                    }),
                });
        }

        [Test]
        public void Given_ACapturingContainerWhosePortalLeavesAndComesBack_When_ARenderInBetweenBindsItsHandlerAgain_Then_TheCallSiteCapturesOnce()
        {
            // Arrange — the rebind happens while no portal holds a bridge on the container, which a bridge
            // that detached must not answer.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(CapturingContainerItsOwnPortalRendersInto));
            _mounted.FlushStateForTest();
            s_showPortal.Invoke(false);
            _mounted.FlushStateForTest();
            s_setRender.Invoke(1);
            _mounted.FlushStateForTest();
            s_showPortal.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            Dispatch(_window.rootVisualElement.Q<VisualElement>("L"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("Z1>,B>"));
        }

        [Test]
        public void Given_ACapturingContainerAPortalAboveItsCallSiteRendersInto_When_ARenderBindsItsHandlerAgain_Then_ItStillCapturesBeforeTheCallSite()
        {
            // Arrange — the render after the portal attached its bridge registers the container's capture
            // binding again, behind the bridge's listener unless the bridge moves it.
            _mounted = V.Mount(_window.rootVisualElement, V.Component(CapturingContainerItsOwnPortalRendersInto));
            _mounted.FlushStateForTest();
            s_setRender.Invoke(1);
            _mounted.FlushStateForTest();

            // Act
            Dispatch(_window.rootVisualElement.Q<VisualElement>("L"));

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("Z1>,B>"));
        }
    }
}
