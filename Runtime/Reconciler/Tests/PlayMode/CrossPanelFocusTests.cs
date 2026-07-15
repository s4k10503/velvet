using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the minimal, defensible reading of #30's "focus... relative to the app's main panel"
    /// scope: (1) a focusable element inside a framework host panel (V.Portal(layer:) / V.WorldSpace)
    /// actually receives focus on that panel's own FocusController when focused, and (2) a host panel
    /// torn down while it holds focus hands focus back to the main panel instead of leaving it
    /// dangling on a defunct FocusController. Automatic Tab/Shift-Tab wrap-around chaining ACROSS panel
    /// boundaries is explicitly out of scope — no web precedent (iframe boundaries, Shadow DOM's
    /// default containment, React Aria's FocusScope) auto-chains independent focus scopes; every one
    /// of them requires explicit author opt-in to cross a scope boundary, which Velvet does not
    /// currently expose (tracked separately, gated on confirming a TrickleDown key-event listener can
    /// reliably preempt FocusController's own wrap action before building on it).
    /// </summary>
    internal sealed class CrossPanelFocusTests
    {
        private GameObject _docGo;
        private PanelSettings _settings;
        private MountedTree _mounted;
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _frameRateScope.Dispose();
            _mounted?.Dispose();
            _mounted = null;
            if (_docGo != null) Object.Destroy(_docGo);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        private VisualElement CreateMainPanel()
        {
            _docGo = new GameObject("MainPanel");
            var doc = _docGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            return doc.rootVisualElement;
        }

        private static UIDocument FindHostDocumentContaining(string elementName)
        {
            foreach (var doc in Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                if (doc.rootVisualElement?.Q<VisualElement>(elementName) != null)
                {
                    return doc;
                }
            }
            return null;
        }

        [UnityTest]
        public IEnumerator Given_AFocusableElementInsideALayerPortal_When_Focused_Then_TheHostPanelsFocusControllerTracksIt()
        {
            // Arrange
            var root = CreateMainPanel();
            yield return null;
            _mounted = V.Mount(root, V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[]
                {
                    V.Div(name: "focus-target", props: new FiberElementProps { Focusable = true }),
                }),
            }));
            yield return null;
            yield return null;

            var hostDoc = FindHostDocumentContaining("focus-target");
            Assume.That(hostDoc, Is.Not.Null, "Precondition: the layer host panel exists");
            var target = hostDoc.rootVisualElement.Q<VisualElement>("focus-target");
            Assume.That(target, Is.Not.Null, "Precondition: the portal's child mounted under the host");

            // Act
            target.Focus();
            yield return null;

            // Assert
            Assert.That(hostDoc.rootVisualElement.panel.focusController.focusedElement, Is.SameAs(target));
        }

        [Component]
        private static VNode ToggleableWorldSpaceHost()
        {
            var (mounted, setMounted) = Hooks.UseState(true);
            s_setWorldSpaceMounted = setMounted;
            return mounted
                ? V.WorldSpace(Vector3.zero, panelSize: new Vector2(400f, 400f), children: new VNode[]
                {
                    V.Div(name: "ws-focus-target", props: new FiberElementProps { Focusable = true }),
                })
                : V.Div();
        }

        private static StateUpdater<bool> s_setWorldSpaceMounted;

        [UnityTest]
        public IEnumerator Given_AWorldSpacePanelHoldingFocus_When_ItUnmounts_Then_FocusReturnsToTheMainPanel()
        {
            // Arrange — focus the world-space panel's own element while it's still alive.
            var root = CreateMainPanel();
            root.focusable = true;
            yield return null;
            _mounted = V.Mount(root, V.Component(ToggleableWorldSpaceHost, key: "root"));
            yield return null;
            yield return null;

            var hostDoc = FindHostDocumentContaining("ws-focus-target");
            Assume.That(hostDoc, Is.Not.Null, "Precondition: the world-space host exists");
            var target = hostDoc.rootVisualElement.Q<VisualElement>("ws-focus-target");
            Assume.That(target, Is.Not.Null, "Precondition: the world-space child mounted under the host");
            target.Focus();
            yield return null;
            Assume.That(hostDoc.rootVisualElement.panel.focusController.focusedElement, Is.SameAs(target),
                "Precondition: the world-space panel's own FocusController holds focus on the target");

            // Act — unmount the world-space panel while it still holds focus.
            s_setWorldSpaceMounted.Invoke(false);
            yield return null;
            yield return null;

            // Assert — focus returned to the main panel's own root instead of dangling on the
            // destroyed panel (which no longer exists to observe directly).
            Assert.That(root.panel.focusController.focusedElement, Is.SameAs(root));
        }
    }
}
