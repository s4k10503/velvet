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
