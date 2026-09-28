using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

#if UNITY_EDITOR
namespace Velvet.Tests
{
    /// <summary>
    /// A <c>contain</c> scope holds focus against every panel: focus that moves from its panel to another is
    /// pulled back on the scope's panel's next tick, unless it lands in a portal declared inside the scope or
    /// in another contained scope.
    /// </summary>
    internal sealed class FocusCrossPanelContainmentTests
    {
        private GameObject _panelGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _panelGo = new GameObject("CrossPanelContainmentPanel");
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

        // Resolved from this mount's own bookkeeping, for the reason FocusChainedPortalTests gives.
        private VisualElement Main(string name)
            => _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>(name);

        private VisualElement HostElement(string name)
            => _mounted.Root.Reconciler.Context.LayerHosts[UILayer.Overlay].Document.rootVisualElement.Q<VisualElement>(name);

        // Two portals share the Overlay host: the one declared inside the modal mounts its button beside
        // the one declared outside it, so only the slot range each placeholder owns tells them apart.
        [Component]
        private static VNode ModalBesideAPortalSharingItsLayer() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "modal", contain: true, children: new VNode[]
            {
                V.Button(name: "m1"),
                V.Portal(UILayer.Overlay, key: "inner", children: new VNode[]
                {
                    V.Button(name: "inner"),
                }),
            }),
            V.Portal(UILayer.Overlay, key: "outer", children: new VNode[]
            {
                V.Button(name: "outer"),
            }),
        });

        private IEnumerator MountModalBesideAPortalSharingItsLayer()
        {
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(ModalBesideAPortalSharingItsLayer, key: "root"));
            yield return null;
            yield return null;
            var m1 = Main("m1");
            m1.Focus();
            Assume.That(m1.panel.focusController.focusedElement, Is.EqualTo(m1),
                "Precondition: the modal holds focus");
        }

        // GREEN_ON_BASE(characterization): the base never pulls focus back from a panel it manages.
        // This case pins that the pull-back spares a portal declared inside the modal.
        [UnityTest]
        public IEnumerator Given_AContainedScope_When_FocusMovesToAPortalDeclaredInsideIt_Then_FocusStaysInThePortal()
        {
            // Arrange
            yield return MountModalBesideAPortalSharingItsLayer();
            var inner = HostElement("inner");

            // Act
            Main("m1").Blur();
            inner.Focus();
            yield return null;
            yield return null;

            // Assert
            Assert.That(inner.panel.focusController.focusedElement, Is.EqualTo(inner));
        }

        [UnityTest]
        public IEnumerator Given_AContainedScope_When_FocusMovesToAPortalDeclaredOutsideItOnTheSameLayer_Then_FocusReturnsToTheScope()
        {
            // Arrange
            yield return MountModalBesideAPortalSharingItsLayer();
            var m1 = Main("m1");

            // Act
            m1.Blur();
            HostElement("outer").Focus();
            yield return null;
            yield return null;

            // Assert
            Assert.That(m1.panel.focusController.focusedElement, Is.EqualTo(m1));
        }

        [Component]
        private static VNode ModalBesideAContainedLayerDialog() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "modal", contain: true, children: new VNode[]
            {
                V.Button(name: "m1"),
            }),
            V.Portal(UILayer.Overlay, key: "dialog", children: new VNode[]
            {
                V.FocusScope(name: "dialog", contain: true, children: new VNode[]
                {
                    V.Button(name: "d1"),
                }),
            }),
        });

        // GREEN_ON_BASE(characterization): the base never pulls focus back from a panel it manages.
        // This case pins that the pull-back spares a contained dialog on another layer.
        [UnityTest]
        public IEnumerator Given_AContainedScope_When_FocusMovesToAContainedScopeOnAnotherPanel_Then_TheNewScopeKeepsIt()
        {
            // Arrange
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(ModalBesideAContainedLayerDialog, key: "root"));
            yield return null;
            yield return null;
            var m1 = Main("m1");
            m1.Focus();
            Assume.That(m1.panel.focusController.focusedElement, Is.EqualTo(m1),
                "Precondition: the modal holds focus");
            var d1 = HostElement("d1");

            // Act
            m1.Blur();
            d1.Focus();
            yield return null;
            yield return null;

            // Assert
            Assert.That(d1.panel.focusController.focusedElement, Is.EqualTo(d1));
        }

        [Component]
        private static VNode LayerModalOverTheMainPanel() => V.Div(children: new VNode[]
        {
            V.Button(name: "outside"),
            V.Portal(UILayer.Overlay, key: "modal", children: new VNode[]
            {
                V.FocusScope(name: "modal", contain: true, children: new VNode[]
                {
                    V.Button(name: "d1"),
                }),
            }),
        });

        [UnityTest]
        public IEnumerator Given_AContainedScopeInALayerHost_When_FocusMovesToTheMainPanel_Then_FocusReturnsToTheScope()
        {
            // Arrange
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement,
                V.Component(LayerModalOverTheMainPanel, key: "root"));
            yield return null;
            yield return null;
            var d1 = HostElement("d1");
            d1.Focus();
            Assume.That(d1.panel.focusController.focusedElement, Is.EqualTo(d1),
                "Precondition: the layer modal holds focus");

            // Act
            d1.Blur();
            Main("outside").Focus();
            yield return null;
            yield return null;

            // Assert
            Assert.That(d1.panel.focusController.focusedElement, Is.EqualTo(d1));
        }
    }
}
#endif
