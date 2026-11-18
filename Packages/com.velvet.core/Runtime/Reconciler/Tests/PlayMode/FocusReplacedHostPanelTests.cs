using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

#if UNITY_EDITOR
namespace Velvet.Tests
{
    /// <summary>
    /// A host document that stays disabled through a panel update comes back with a new root in a new panel. What the
    /// host's first root carried (the focus navigator, the chained ring, the event bridge, the default family and the
    /// viewport of the declaring panel) is on the new root and panel too, with no patch of a portal into it. The
    /// portal's children stay on the old root until a patch moves them.
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

        private static StateUpdater<int> s_setPass;
        private static bool s_pointerDownReached;

        // Re-renders on demand, which is the patch that moves the portal's children into the host's new root.
        [Component(Compiler = false)]
        private static VNode RerenderableLayerHost()
        {
            var (_, setPass) = Hooks.UseState(0);
            s_setPass = setPass;
            return LayerHost();
        }

        [Component]
        private static VNode WorldSpaceHost() => V.WorldSpace(Vector3.zero, panelSize: new Vector2(400f, 400f), children: new VNode[]
        {
            V.Button(name: "ws1"),
        });

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            s_pointerDownReached = false;
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

        private static bool Replaced(VisualElement root, VisualElement oldRoot, IPanel oldPanel)
            => !ReferenceEquals(root, oldRoot) && root.panel != null && !ReferenceEquals(root.panel, oldPanel);

        private static bool TableHolds(System.Type owner, string field, VisualElement key)
        {
            var table = owner.GetField(field, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            return (bool)table.GetType().GetMethod("TryGetValue").Invoke(table, new object[] { key, null });
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
            var replaced = Replaced(root, oldRoot, oldPanel);

            // Assert — false where the host kept its panel, which the navigator then already holds
            Assert.That(replaced && NavigatorIsOn(root), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base already keys a chained host's root when the portal mounts.
        [UnityTest]
        public IEnumerator Given_AChainedLayerHost_When_Mounted_Then_ItsRootOwnsTheChainedRing()
        {
            // Arrange
            yield return MountLayerHost(PanelFocusOrder.Chained);

            // Act
            var owned = Context.ChainedHostRoots.ContainsKey(LayerDocument.rootVisualElement);

            // Assert
            Assert.That(owned, Is.True);
        }

        [UnityTest]
        public IEnumerator Given_AChainedLayerHostWhosePanelWasReplaced_When_NothingPatchesThePortal_Then_ItsNewRootOwnsTheChainedRing()
        {
            // Arrange
            yield return MountLayerHost(PanelFocusOrder.Chained);
            var document = LayerDocument;
            var oldRoot = document.rootVisualElement;
            var oldPanel = oldRoot.panel;

            // Act
            yield return ReplaceRoot(document);
            var root = document.rootVisualElement;

            // Assert — false where the host kept its panel
            Assert.That(Replaced(root, oldRoot, oldPanel) && Context.ChainedHostRoots.ContainsKey(root), Is.True);
        }

        [UnityTest]
        public IEnumerator Given_ALayerHostWhosePanelWasReplaced_When_NothingPatchesThePortal_Then_ItsNewRootCarriesTheEventBridge()
        {
            // Arrange
            yield return MountLayerHost(PanelFocusOrder.Isolated);
            var document = LayerDocument;
            var oldRoot = document.rootVisualElement;
            var oldPanel = oldRoot.panel;

            // Act
            yield return ReplaceRoot(document);
            var root = document.rootVisualElement;

            // Assert
            Assert.That(Replaced(root, oldRoot, oldPanel) && Context.EventManager.IsBridgeAnchor(root), Is.True);
        }

        [UnityTest]
        public IEnumerator Given_ALayerHostWhosePanelWasReplaced_When_NothingPatchesThePortal_Then_ItsOldRootCarriesNoEventBridge()
        {
            // Arrange
            yield return MountLayerHost(PanelFocusOrder.Isolated);
            var document = LayerDocument;
            var oldRoot = document.rootVisualElement;
            var oldPanel = oldRoot.panel;

            // Act
            yield return ReplaceRoot(document);

            // Assert — true where the host kept its root, which then carries the bridge still
            Assert.That(Replaced(document.rootVisualElement, oldRoot, oldPanel) ? Context.EventManager.IsBridgeAnchor(oldRoot) : true, Is.False);
        }

        [UnityTest]
        public IEnumerator Given_ALayerHostWhosePanelWasReplaced_When_NothingPatchesThePortal_Then_ItsNewRootHoldsTheDefaultFamily()
        {
            // Arrange
            yield return MountLayerHost(PanelFocusOrder.Isolated);
            var document = LayerDocument;
            var oldRoot = document.rootVisualElement;
            var oldPanel = oldRoot.panel;

            // Act
            yield return ReplaceRoot(document);
            var root = document.rootVisualElement;

            // Assert
            Assert.That(Replaced(root, oldRoot, oldPanel) && TableHolds(typeof(DefaultFamilyRoots), "s_roots", root), Is.True);
        }

        [UnityTest]
        public IEnumerator Given_ALayerHostWhosePanelWasReplaced_When_NothingPatchesThePortal_Then_ItsNewPanelAnswersTheDeclaringPanelsViewport()
        {
            // Arrange
            yield return MountLayerHost(PanelFocusOrder.Isolated);
            var document = LayerDocument;
            var oldRoot = document.rootVisualElement;
            var oldPanel = oldRoot.panel;

            // Act
            yield return ReplaceRoot(document);
            var root = document.rootVisualElement;

            // Assert
            Assert.That(Replaced(root, oldRoot, oldPanel) && TableHolds(typeof(StyleResponsiveScope), "s_declaringRoots", root.panel.visualTree), Is.True);
        }

        [UnityTest]
        public IEnumerator Given_ALayerHostWhosePanelWasReplaced_When_ThePortalPatchesAndAPointerDownBubblesToTheNewRoot_Then_TheEnclosingHandlerFires()
        {
            // Arrange
            var binding = new PointerDownBinding { Handler = _ => s_pointerDownReached = true };
            s_order = PanelFocusOrder.Isolated;
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement, V.Motion(
                name: "enclosing",
                events: new FiberEventBinding[] { binding },
                children: new VNode[] { V.Component(RerenderableLayerHost, key: "host") }));
            yield return null;
            yield return null;
            var document = LayerDocument;
            var oldRoot = document.rootVisualElement;
            var oldPanel = oldRoot.panel;
            yield return ReplaceRoot(document);
            s_setPass.Invoke(1);
            yield return null;
            yield return null;
            var root = document.rootVisualElement;
            var replaced = Replaced(root, oldRoot, oldPanel);
            var target = root.Q<VisualElement>("p1");

            // Act
            using var down = PointerDownEvent.GetPooled();
            root.SimulateBubbledEvent(down, target);

            // Assert
            Assert.That(replaced && s_pointerDownReached, Is.True);
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
            var replaced = Replaced(root, oldRoot, oldPanel);

            // Assert
            Assert.That(replaced && NavigatorIsOn(root), Is.True);
        }
    }
}
#endif
