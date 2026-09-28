using System.Collections;
using System.Collections.Generic;
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
    /// in a newer contained scope — in this mounted tree or another.
    /// </summary>
    internal sealed class FocusCrossPanelContainmentTests
    {
        private static VisualElement s_portalTarget;

        private GameObject _panelGo;
        private PanelSettings _settings;
        private MountedTree _mounted;
        private GameObject _otherPanelGo;
        private PanelSettings _otherSettings;
        private MountedTree _otherMounted;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            s_portalTarget = null;
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
            BlurEveryPanel();
            _mounted?.Dispose();
            _mounted = null;
            _otherMounted?.Dispose();
            _otherMounted = null;
            if (_panelGo != null) Object.Destroy(_panelGo);
            if (_settings != null) Object.Destroy(_settings);
            if (_otherPanelGo != null) Object.Destroy(_otherPanelGo);
            if (_otherSettings != null) Object.Destroy(_otherSettings);
            s_portalTarget = null;
            yield return null;
        }

        // A second runtime panel of its own, which no tree is mounted on until a case mounts one.
        private IEnumerator CreateOtherPanel()
        {
            _otherPanelGo = new GameObject("CrossPanelContainmentOtherPanel");
            var doc = _otherPanelGo.AddComponent<UIDocument>();
            _otherSettings = TestPanelSettings.Create();
            _otherSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _otherSettings;
            yield return null;
        }

        private VisualElement MainRoot => _panelGo.GetComponent<UIDocument>().rootVisualElement;

        private VisualElement OtherRoot => _otherPanelGo.GetComponent<UIDocument>().rootVisualElement;

        // Resolved from this mount's own bookkeeping, for the reason FocusChainedPortalTests gives.
        private VisualElement Main(string name) => MainRoot.Q<VisualElement>(name);

        private VisualElement HostElement(string name)
            => _mounted.Root.Reconciler.Context.LayerHosts[UILayer.Overlay].Document.rootVisualElement.Q<VisualElement>(name);

        private IEnumerator MountOnMain(VNode root)
        {
            _mounted = V.Mount(MainRoot, root);
            yield return null;
            yield return null;
        }

        // Each case ends with none of its panels holding focus. Without this, the case run after the
        // element-portal one had its first Focus() dropped: its m1 was that case's p1, back from the pool and
        // still the element remembered by the panel UI Toolkit's event system had last focused.
        private void BlurEveryPanel()
        {
            var roots = new List<VisualElement>();
            if (_panelGo != null) roots.Add(MainRoot);
            if (_otherPanelGo != null) roots.Add(OtherRoot);
            foreach (var mounted in new[] { _mounted, _otherMounted })
            {
                if (mounted == null) continue;
                foreach (var host in mounted.Root.Reconciler.Context.LayerHosts.Values)
                {
                    if (host.Document != null) roots.Add(host.Document.rootVisualElement);
                }
            }
            foreach (var root in roots)
            {
                (root?.panel?.focusController?.focusedElement as VisualElement)?.Blur();
            }
        }

        private static Focusable FocusAndRead(VisualElement element)
        {
            element.Focus();
            return element.panel.focusController.focusedElement;
        }

        // Reads what `panel` holds focused on two consecutive frames once the pull-back tick has had its
        // frame, so two scopes pulling focus back from each other every tick fail the comparison instead of
        // matching it by phase. readings[0] is left to the arrangement's own reading.
        private static IEnumerator ReadTwoSettledFrames(VisualElement onPanel, Focusable[] readings)
        {
            yield return null;
            yield return null;
            readings[1] = onPanel.panel.focusController.focusedElement;
            yield return null;
            readings[2] = onPanel.panel.focusController.focusedElement;
        }

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

        // GREEN_ON_BASE(characterization): the base never pulls focus back from a panel it manages.
        // This case pins that the pull-back spares a portal declared inside the modal.
        [UnityTest]
        public IEnumerator Given_AContainedScope_When_FocusMovesToAPortalDeclaredInsideIt_Then_FocusStaysInThePortal()
        {
            // Arrange
            yield return MountOnMain(V.Component(ModalBesideAPortalSharingItsLayer, key: "root"));
            var m1 = Main("m1");
            var inner = HostElement("inner");
            var readings = new Focusable[3];
            readings[0] = FocusAndRead(m1);

            // Act
            m1.Blur();
            inner.Focus();
            yield return ReadTwoSettledFrames(inner, readings);

            // Assert
            Assert.That(readings, Is.EqualTo(new Focusable[] { m1, inner, inner }));
        }

        [UnityTest]
        public IEnumerator Given_AContainedScope_When_FocusMovesToAPortalDeclaredOutsideItOnTheSameLayer_Then_FocusReturnsToTheScope()
        {
            // Arrange
            yield return MountOnMain(V.Component(ModalBesideAPortalSharingItsLayer, key: "root"));
            var m1 = Main("m1");
            var readings = new Focusable[3];
            readings[0] = FocusAndRead(m1);

            // Act
            m1.Blur();
            HostElement("outer").Focus();
            yield return ReadTwoSettledFrames(m1, readings);

            // Assert
            Assert.That(readings, Is.EqualTo(new Focusable[] { m1, m1, m1 }));
        }

        // The dialog's scope is created when the portal drains, after the modal's.
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
        // This case pins that the pull-back spares a newer contained dialog on another layer.
        [UnityTest]
        public IEnumerator Given_AContainedScope_When_FocusMovesToANewerContainedScopeOnAnotherPanel_Then_TheNewerScopeKeepsIt()
        {
            // Arrange
            yield return MountOnMain(V.Component(ModalBesideAContainedLayerDialog, key: "root"));
            var m1 = Main("m1");
            var d1 = HostElement("d1");
            var readings = new Focusable[3];
            readings[0] = FocusAndRead(m1);

            // Act
            m1.Blur();
            d1.Focus();
            yield return ReadTwoSettledFrames(d1, readings);

            // Assert
            Assert.That(readings, Is.EqualTo(new Focusable[] { m1, d1, d1 }));
        }

        [UnityTest]
        public IEnumerator Given_ANewerContainedScopeOnAnotherPanel_When_FocusMovesIntoTheOlderScope_Then_TheNewerScopeTakesItBack()
        {
            // Arrange
            yield return MountOnMain(V.Component(ModalBesideAContainedLayerDialog, key: "root"));
            var d1 = HostElement("d1");
            var readings = new Focusable[3];
            readings[0] = FocusAndRead(d1);

            // Act
            d1.Blur();
            Main("m1").Focus();
            yield return ReadTwoSettledFrames(d1, readings);

            // Assert
            Assert.That(readings, Is.EqualTo(new Focusable[] { d1, d1, d1 }));
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
            yield return MountOnMain(V.Component(LayerModalOverTheMainPanel, key: "root"));
            var d1 = HostElement("d1");
            var readings = new Focusable[3];
            readings[0] = FocusAndRead(d1);

            // Act
            d1.Blur();
            Main("outside").Focus();
            yield return ReadTwoSettledFrames(d1, readings);

            // Assert
            Assert.That(readings, Is.EqualTo(new Focusable[] { d1, d1, d1 }));
        }

        [Component]
        private static VNode ModalDeclaringAZIndexedPortalChild() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "modal", contain: true, children: new VNode[]
            {
                V.Button(name: "m1"),
                V.Portal(UILayer.Overlay, key: "inner", children: new VNode[]
                {
                    V.Button(name: "zp", className: "absolute z-10"),
                }),
            }),
        });

        // GREEN_ON_BASE(characterization): the base never pulls focus back from a panel it manages.
        // This case pins that a z-indexed portal child, which leaves its slot for a layer container, still
        // counts as the portal's content.
        [UnityTest]
        public IEnumerator Given_AContainedScope_When_FocusMovesToAZIndexedChildOfAPortalDeclaredInsideIt_Then_FocusStaysThere()
        {
            // Arrange
            yield return MountOnMain(V.Component(ModalDeclaringAZIndexedPortalChild, key: "root"));
            var m1 = Main("m1");
            var zp = HostElement("zp");
            var readings = new Focusable[3];
            readings[0] = FocusAndRead(m1);

            // Act
            m1.Blur();
            zp.Focus();
            yield return ReadTwoSettledFrames(zp, readings);

            // Assert
            Assert.That(readings, Is.EqualTo(new Focusable[] { m1, zp, zp }));
        }

        [Component]
        private static VNode ModalDeclaringAnElementPortal() => V.Div(children: new VNode[]
        {
            V.FocusScope(name: "modal", contain: true, children: new VNode[]
            {
                V.Button(name: "m1"),
                V.Portal(s_portalTarget, key: "elsewhere", children: new VNode[]
                {
                    V.Button(name: "p1"),
                }),
            }),
        });

        [UnityTest]
        public IEnumerator Given_AContainedScope_When_FocusMovesToAnElementPortalItDeclaredIntoAPanelNoTreeMounts_Then_FocusStaysInThePortal()
        {
            // Arrange
            yield return CreateOtherPanel();
            s_portalTarget = OtherRoot;
            yield return MountOnMain(V.Component(ModalDeclaringAnElementPortal, key: "root"));
            var m1 = Main("m1");
            var p1 = OtherRoot.Q<VisualElement>("p1");
            var readings = new Focusable[3];
            readings[0] = FocusAndRead(m1);

            // Act
            m1.Blur();
            p1.Focus();
            yield return ReadTwoSettledFrames(p1, readings);

            // Assert
            Assert.That(readings, Is.EqualTo(new Focusable[] { m1, p1, p1 }));
        }

        [Component]
        private static VNode OlderTreeModal() => V.FocusScope(name: "olderModal", contain: true, children: new VNode[]
        {
            V.Button(name: "a1"),
        });

        [Component]
        private static VNode NewerTreeModal() => V.FocusScope(name: "newerModal", contain: true, children: new VNode[]
        {
            V.Button(name: "b1"),
        });

        [UnityTest]
        public IEnumerator Given_ContainedScopesInTwoMountedTrees_When_FocusMovesFromTheNewerIntoTheOlder_Then_TheNewerKeepsItWithoutAlternating()
        {
            // Arrange — each tree is its own mount on its own panel, the older mounted first.
            yield return CreateOtherPanel();
            yield return MountOnMain(V.Component(OlderTreeModal, key: "root"));
            _otherMounted = V.Mount(OtherRoot, V.Component(NewerTreeModal, key: "root"));
            yield return null;
            yield return null;
            var b1 = OtherRoot.Q<VisualElement>("b1");
            var readings = new Focusable[3];
            readings[0] = FocusAndRead(b1);

            // Act
            b1.Blur();
            Main("a1").Focus();
            yield return ReadTwoSettledFrames(b1, readings);

            // Assert
            Assert.That(readings, Is.EqualTo(new Focusable[] { b1, b1, b1 }));
        }
    }
}
#endif
