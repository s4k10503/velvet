#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>PanelFocusOrder</c> on <c>V.Portal(layer:)</c> against real runtime panels:
    /// <c>Chained</c> joins the declaring panel's Tab order at the portal's call site (Tab past the host
    /// ring's last element exits to the declaring panel; Tab reaching the placeholder enters the host at its
    /// ring-first), <c>Isolated</c> (the default) keeps the pre-existing internal wrap — the explicit-opt-in
    /// ruling of the cross-panel navigation decision, pinned here as a regression guard — and only
    /// <c>NavigationMoveEvent</c> is ever intercepted (a KeyDown reaching an element in a chained host is
    /// untouched, the two-event gotcha).
    /// </summary>
    internal sealed class FocusChainedPortalTests
    {
        private static PanelFocusOrder s_focusOrder;
        private static StateUpdater<bool> s_setShowFirstPortal;

        private GameObject _panelGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        [Component]
        private static VNode PortalHost() => V.Div(children: new VNode[]
        {
            V.Button(name: "main1"),
            V.Portal(UILayer.Overlay, key: "portal", focusOrder: s_focusOrder, children: new VNode[]
            {
                V.Button(name: "p1"),
                V.Button(name: "p2"),
            }),
        });

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            s_focusOrder = PanelFocusOrder.Isolated;
            s_setShowFirstPortal = default;
            _panelGo = new GameObject("ChainedPortalPanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_panelGo != null) Object.Destroy(_panelGo);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        // Mounts under the CURRENT s_focusOrder and waits out the portal drain plus both panels' first
        // style passes (focus-ring membership needs resolved display state on each panel).
        private IEnumerator Mount()
        {
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement, V.Component(PortalHost, key: "root"));
            yield return null;
            yield return null;
        }

        // Resolved from THIS mount's own bookkeeping, never a scene-wide UIDocument scan: a previous test's
        // host panel lingers as a destroy-pending object within the same frame, and a global scan can hand
        // back the stale twin of the element under test.
        private VisualElement Main(string name)
            => _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>(name);

        private VisualElement HostElement(string name)
            => _mounted.Root.Reconciler.Context.LayerHosts[UILayer.Overlay].Document.rootVisualElement.Q<VisualElement>(name);

        private static void SendMove(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            using var move = NavigationMoveEvent.GetPooled(direction);
            move.target = target;
            target.SendEvent(move);
        }

        [UnityTest]
        public IEnumerator Given_AChainedLayerPortal_When_TabDispatchesFromTheHostRingsLastFocusable_Then_FocusLandsOnTheDeclaringPanelElementAfterThePlaceholder()
        {
            // Arrange
            s_focusOrder = PanelFocusOrder.Chained;
            yield return Mount();
            var p2 = HostElement("p2");
            Assume.That(p2, Is.Not.Null, "Precondition: the portal children mounted in the host panel");
            p2.Focus();
            Assume.That(p2.panel.focusController.focusedElement, Is.EqualTo(p2),
                "Precondition: the host ring's last focusable holds focus");

            // Act — the escape's cross-panel Focus lands on the target panel's next scheduler tick (see
            // FiberFocusNavigator's own note on why it cannot be synchronous), so wait a few frames.
            SendMove(p2, NavigationMoveEvent.Direction.Next);
            yield return null;
            yield return null;
            yield return null;

            // Assert — the declaring panel's ring past the placeholder wraps to main1 (the only other
            // focusable there), instead of the host wrapping internally to p1.
            var main1 = Main("main1");
            Assert.That(main1.panel.focusController.focusedElement, Is.EqualTo(main1));
        }

        [UnityTest]
        public IEnumerator Given_AChainedLayerPortal_When_TabReachesThePlaceholder_Then_FocusEntersTheHostAtItsRingFirst()
        {
            // Arrange
            s_focusOrder = PanelFocusOrder.Chained;
            yield return Mount();
            var main1 = Main("main1");
            main1.Focus();
            Assume.That(main1.panel.focusController.focusedElement, Is.EqualTo(main1),
                "Precondition: the declaring panel's element holds focus");

            // Act — the engine's own move lands on the proxy placeholder; the FocusIn forwarding hands
            // focus into the host panel.
            SendMove(main1, NavigationMoveEvent.Direction.Next);
            yield return null;

            // Assert
            var p1 = HostElement("p1");
            Assert.That(p1.panel.focusController.focusedElement, Is.EqualTo(p1));
        }

        [UnityTest]
        public IEnumerator Given_AnIsolatedLayerPortal_When_TabDispatchesFromTheHostRingsLastFocusable_Then_FocusWrapsWithinTheHostPanel()
        {
            // Arrange — the default: no opt-in, the #54-adjacent status quo stays byte-for-byte.
            s_focusOrder = PanelFocusOrder.Isolated;
            yield return Mount();
            var p2 = HostElement("p2");
            Assume.That(p2, Is.Not.Null, "Precondition: the portal children mounted in the host panel");
            p2.Focus();

            // Act
            SendMove(p2, NavigationMoveEvent.Direction.Next);
            yield return null;

            // Assert — the host's own ring wraps internally.
            var p1 = HostElement("p1");
            Assert.That(p1.panel.focusController.focusedElement, Is.EqualTo(p1));
        }

        [Component]
        private static VNode StsPortalHost() => V.Div(children: new VNode[]
        {
            V.Button(name: "main1"),
            V.Portal(UILayer.Overlay, key: "portal", focusOrder: PanelFocusOrder.Chained, children: new VNode[]
            {
                V.FocusScope(name: "group", singleTabStop: true, children: new VNode[]
                {
                    V.Button(name: "p1"),
                    V.Button(name: "p2"),
                }),
            }),
        });

        [UnityTest]
        public IEnumerator Given_AChainedHostWhoseContentIsOneSingleTabStopGroup_When_TabDispatchesFromAMember_Then_FocusEscapesToTheDeclaringPanel()
        {
            // Arrange — the natural gamepad case: a portal'd roving toolbar. The group's one-stop exit
            // finds no candidate inside the host, so it must fall through to the chained boundary
            // escape rather than letting the engine cycle the group's own members.
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(StsPortalHost, key: "root"));
            yield return null;
            yield return null;
            var p1 = HostElement("p1");
            p1.Focus();
            Assume.That(p1.panel.focusController.focusedElement, Is.EqualTo(p1),
                "Precondition: a group member holds focus");

            // Act
            SendMove(p1, NavigationMoveEvent.Direction.Next);
            yield return null;
            yield return null;
            yield return null;

            // Assert
            var main1 = Main("main1");
            Assert.That(main1.panel.focusController.focusedElement, Is.EqualTo(main1));
        }

        [Component]
        private static VNode TwoChainedPortalsHost()
        {
            var (showFirst, setShowFirst) = Hooks.UseState(true);
            s_setShowFirstPortal = setShowFirst;
            return V.Div(children: new VNode[]
            {
                V.Button(name: "main1"),
                showFirst
                    ? V.Portal(UILayer.Overlay, key: "portalA", focusOrder: PanelFocusOrder.Chained, children: new VNode[]
                    {
                        V.Button(name: "a1"),
                    })
                    : null,
                V.Portal(UILayer.Overlay, key: "portalB", focusOrder: PanelFocusOrder.Chained, children: new VNode[]
                {
                    V.Button(name: "b1"),
                }),
            });
        }

        [UnityTest]
        public IEnumerator Given_TwoChainedPortalsSharingALayerHost_When_TheOwningPortalUnmounts_Then_TheSurvivorStillEscapesAtTheHostRingEdge()
        {
            // Arrange — portalA mounted first and owns the host's ring-edge escape; unmounting it must
            // hand the escape to portalB, or the surviving chained portal silently loses its exit.
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(TwoChainedPortalsHost, key: "root"));
            yield return null;
            yield return null;
            s_setShowFirstPortal.Invoke(false);
            yield return null;
            yield return null;
            var b1 = HostElement("b1");
            b1.Focus();
            Assume.That(b1.panel.focusController.focusedElement, Is.EqualTo(b1),
                "Precondition: the surviving portal's member holds focus");

            // Act
            SendMove(b1, NavigationMoveEvent.Direction.Next);
            yield return null;
            yield return null;
            yield return null;

            // Assert
            var main1 = Main("main1");
            Assert.That(main1.panel.focusController.focusedElement, Is.EqualTo(main1));
        }

        [Component]
        private static VNode ContainedStsPortalHost() => V.Div(children: new VNode[]
        {
            V.Button(name: "main1"),
            V.Portal(UILayer.Overlay, key: "portal", focusOrder: PanelFocusOrder.Chained, children: new VNode[]
            {
                V.FocusScope(name: "modal", contain: true, children: new VNode[]
                {
                    V.FocusScope(name: "group", singleTabStop: true, children: new VNode[]
                    {
                        V.Button(name: "p1"),
                        V.Button(name: "p2"),
                    }),
                }),
            }),
        });

        [UnityTest]
        public IEnumerator Given_AContainedGroupCoveringAChainedHost_When_TabDispatchesFromAMember_Then_FocusNeverEscapesTheModal()
        {
            // Arrange — a contain modal inside a chained host whose only focusables are one
            // singleTabStop group: the group's one-stop exit finds no candidate, and the chained
            // boundary escape must NOT override the modal's containment.
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(ContainedStsPortalHost, key: "root"));
            yield return null;
            yield return null;
            var p1 = HostElement("p1");
            p1.Focus();
            Assume.That(p1.panel.focusController.focusedElement, Is.EqualTo(p1),
                "Precondition: a modal group member holds focus");

            // Act
            SendMove(p1, NavigationMoveEvent.Direction.Next);
            yield return null;
            yield return null;
            yield return null;

            // Assert — the declaring panel must not have received focus.
            var main1 = Main("main1");
            Assert.That(main1.panel.focusController.focusedElement, Is.Not.EqualTo(main1));
        }

        [Component]
        private static VNode EdgeGroupPortalHost() => V.Div(children: new VNode[]
        {
            V.Button(name: "main1"),
            V.Portal(UILayer.Overlay, key: "portal", focusOrder: PanelFocusOrder.Chained, children: new VNode[]
            {
                V.Button(name: "x"),
                V.FocusScope(name: "group", singleTabStop: true, children: new VNode[]
                {
                    V.Button(name: "g1"),
                    V.Button(name: "g2"),
                }),
            }),
        });

        [UnityTest]
        public IEnumerator Given_ASingleTabStopGroupAtAChainedHostsRingEdge_When_TabExitsForward_Then_FocusEscapesToTheDeclaringPanel()
        {
            // Arrange — the group sits at the END of the host ring, with another focusable before it:
            // the group's exit walk WRAPS past the ring edge, and the pinned iframe contract says a
            // move past the host ring's end exits to the declaring panel — never wraps back to "x".
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(EdgeGroupPortalHost, key: "root"));
            yield return null;
            yield return null;
            var g2 = HostElement("g2");
            g2.Focus();
            Assume.That(g2.panel.focusController.focusedElement, Is.EqualTo(g2),
                "Precondition: the edge group's member holds focus");

            // Act
            SendMove(g2, NavigationMoveEvent.Direction.Next);
            yield return null;
            yield return null;
            yield return null;

            // Assert
            var main1 = Main("main1");
            Assert.That(main1.panel.focusController.focusedElement, Is.EqualTo(main1));
        }

        [Component]
        private static VNode ModalWithIsolatedPortalHost() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "modal", contain: true, children: new VNode[]
            {
                V.Button(name: "m1"),
            }),
            V.Portal(UILayer.Overlay, key: "portal", focusOrder: PanelFocusOrder.Isolated, children: new VNode[]
            {
                V.Button(name: "p1"),
            }),
        });

        [UnityTest]
        public IEnumerator Given_AContainedScopeInTheMainPanel_When_FocusLegitimatelyMovesToAnotherPanel_Then_TheModalDoesNotStealItBack()
        {
            // Arrange — a cross-panel focus move blurs the old panel to NOTHING (the same FocusOut
            // signature as a click on empty space), but here focus went somewhere: another managed
            // panel. The containment re-focus must recognize that and stand down.
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(ModalWithIsolatedPortalHost, key: "root"));
            yield return null;
            yield return null;
            var m1 = Main("m1");
            m1.Focus();
            Assume.That(m1.panel.focusController.focusedElement, Is.EqualTo(m1),
                "Precondition: the modal holds focus");

            // Act
            m1.Blur();
            HostElement("p1").Focus();
            yield return null;
            yield return null;

            // Assert — the main panel must stay unfocused instead of the modal yanking focus back.
            Assert.That(m1.panel.focusController.focusedElement, Is.Null);
        }

        [UnityTest]
        public IEnumerator Given_AChainedLayerPortal_When_AKeyDownDispatchesInsideTheHost_Then_ItStillReachesItsHandler()
        {
            // Arrange — only NavigationMoveEvent is intercepted; the co-arriving KeyDownEvent of a real Tab
            // press must stay untouched for user handlers and the cross-panel KeyDown bridge.
            s_focusOrder = PanelFocusOrder.Chained;
            yield return Mount();
            var p2 = HostElement("p2");
            var received = 0;
            p2.RegisterCallback<KeyDownEvent>(_ => received++);
            p2.Focus();

            // Act
            using (var key = KeyDownEvent.GetPooled('\t', KeyCode.Tab, EventModifiers.None))
            {
                key.target = p2;
                p2.SendEvent(key);
            }
            yield return null;

            // Assert
            Assert.That(received, Is.EqualTo(1));
        }
    }
}
#endif
