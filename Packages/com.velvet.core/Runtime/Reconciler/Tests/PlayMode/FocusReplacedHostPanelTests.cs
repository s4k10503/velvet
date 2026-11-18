using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

#if UNITY_EDITOR
namespace Velvet.Tests
{
    /// <summary>
    /// A host document that stays disabled through a panel update comes back with a new root in a new panel, and
    /// nothing patches a portal into it on the way. The focus navigator follows the host onto that panel whichever
    /// <c>PanelFocusOrder</c> the portal declares.
    /// </summary>
    internal sealed class FocusReplacedHostPanelTests
    {
        private static PanelFocusOrder s_order;

        private GameObject _panelGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        [Component]
        private static VNode LayerHost() => V.Div(children: new VNode[]
        {
            V.Button(name: "main1"),
            V.Portal(UILayer.Overlay, key: "portal", focusOrder: s_order, children: new VNode[]
            {
                V.Button(name: "p1"),
                V.Button(name: "p2"),
            }),
        });

        [Component]
        private static VNode WorldSpaceHost() => V.WorldSpace(Vector3.zero, panelSize: new Vector2(400f, 400f), children: new VNode[]
        {
            V.Button(name: "ws1"),
        });

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _panelGo = new GameObject("ReplacedHostMainPanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
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

        private IEnumerator MountLayerHost(PanelFocusOrder order)
        {
            s_order = order;
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement, V.Component(LayerHost, key: "root"));
            yield return null;
            yield return null;
        }

        private ReconcilerContext Context => _mounted.Root.Reconciler.Context;

        private UIDocument LayerDocument => Context.LayerHosts[UILayer.Overlay].Document;

        private UIDocument WorldSpaceDocument => Context.WorldSpaceBindings.Values.Single().Document;

        // Disables the document for two frames, enough for the engine to drop its panel, and enables it again.
        private static IEnumerator ReplaceRoot(UIDocument document)
        {
            document.enabled = false;
            yield return null;
            yield return null;
            document.enabled = true;
            yield return null;
            yield return null;
        }

        private bool NavigatorIsOn(VisualElement root)
            => root.panel != null && Context.NavigatorAttachments.ContainsKey(root.panel.visualTree);

        // GREEN_ON_BASE(characterization): the base already attaches the navigator to a host's first panel.
        [UnityTest]
        public IEnumerator Given_AnIsolatedLayerHost_When_Mounted_Then_TheNavigatorIsAttachedToItsPanel()
        {
            // Arrange
            yield return MountLayerHost(PanelFocusOrder.Isolated);

            // Act
            var attached = NavigatorIsOn(LayerDocument.rootVisualElement);

            // Assert
            Assert.That(attached, Is.True);
        }

        [UnityTest]
        public IEnumerator Given_AnIsolatedLayerHostWhosePanelWasReplaced_When_NothingPatchesThePortal_Then_TheNavigatorIsAttachedToTheNewPanel()
        {
            // Arrange
            yield return MountLayerHost(PanelFocusOrder.Isolated);
            var document = LayerDocument;
            var oldRoot = document.rootVisualElement;
            var oldPanel = oldRoot.panel;

            // Act
            yield return ReplaceRoot(document);
            var root = document.rootVisualElement;
            var replaced = !ReferenceEquals(root, oldRoot) && root.panel != null && !ReferenceEquals(root.panel, oldPanel);

            // Assert — false where the host kept its panel, which the navigator then already holds
            Assert.That(replaced && NavigatorIsOn(root), Is.True);
        }

        [UnityTest]
        public IEnumerator Given_AWorldSpaceHostWhosePanelWasReplaced_When_NothingPatchesIt_Then_TheNavigatorIsAttachedToTheNewPanel()
        {
            // Arrange
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement, V.Component(WorldSpaceHost, key: "root"));
            yield return null;
            yield return null;
            var document = WorldSpaceDocument;
            var oldRoot = document.rootVisualElement;
            var oldPanel = oldRoot.panel;

            // Act
            yield return ReplaceRoot(document);
            var root = document.rootVisualElement;
            var replaced = !ReferenceEquals(root, oldRoot) && root.panel != null && !ReferenceEquals(root.panel, oldPanel);

            // Assert
            Assert.That(replaced && NavigatorIsOn(root), Is.True);
        }

        [UnityTest]
        public IEnumerator Given_AnAppendedLayerHostWhosePanelWasReplaced_When_TabDispatchesFromTheHostsLastFocusable_Then_FocusReturnsToTheDeclaringPanel()
        {
            // Arrange
            yield return MountLayerHost(PanelFocusOrder.Appended);
            var document = LayerDocument;
            var oldRoot = document.rootVisualElement;
            var oldPanel = oldRoot.panel;
            yield return ReplaceRoot(document);
            var root = document.rootVisualElement;
            var replaced = !ReferenceEquals(root, oldRoot) && root.panel != null && !ReferenceEquals(root.panel, oldPanel);
            var p2 = root.Q<VisualElement>("p2");
            var main1 = _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("main1");
            p2.Focus();
            var held = p2.panel.focusController.focusedElement == p2;

            // Act
            using (var move = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Next))
            {
                move.target = p2;
                p2.SendEvent(move);
            }
            yield return null;
            yield return null;

            // Assert — null where the host kept its panel or p2 never took focus, which main1 can never be
            Assert.That(replaced && held ? main1.panel.focusController.focusedElement : null, Is.EqualTo(main1));
        }
    }
}
#endif
