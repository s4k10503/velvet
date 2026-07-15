using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a pointer-down at a screen position where a Velvet-managed layer host panel
    /// (V.Portal(layer:)) visually overlaps the main panel is routed to the LAYER's content, not the
    /// main panel's content underneath it — FiberCrossPanelPointerRouter's own arbitration
    /// (independent of whatever Unity's own runtime input system would otherwise do), since the layer
    /// panel is drawn frontmost (higher sortingOrder, see PanelHostFactory).
    /// </summary>
    internal sealed class CrossPanelPointerRoutingTests
    {
        private GameObject _docGo;
        private PanelSettings _settings;
        private MountedTree _mounted;
        private TargetFrameRateScope _frameRateScope;
        private static bool s_mainHandlerFired;
        private static bool s_overlayHandlerFired;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            s_mainHandlerFired = false;
            s_overlayHandlerFired = false;
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

        [Component]
        private static VNode OverlayChildRender() => V.Motion(
            name: "overlay-target",
            events: new FiberEventBinding[] { new PointerDownBinding { Handler = _ => s_overlayHandlerFired = true } },
            className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]");

        [Component]
        private static VNode SceneRender()
        {
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "main-target",
                    events: new FiberEventBinding[] { new PointerDownBinding { Handler = _ => s_mainHandlerFired = true } },
                    className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]"),
                V.Portal(UILayer.Overlay, children: new VNode[] { V.Component(OverlayChildRender) }),
            });
        }

        [UnityTest]
        public IEnumerator Given_AnOverlayLayerOverlapsTheMainPanel_When_PointerDownHitsTheOverlap_Then_OnlyTheOverlaysHandlerFires()
        {
            // Arrange — main-target and overlay-target both occupy screen (0,0)-(100,100); the overlay
            // layer draws frontmost (PanelHostFactory's sortingOrder offset), so it should win.
            _docGo = new GameObject("MainPanel");
            var doc = _docGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            _mounted = V.Mount(doc.rootVisualElement, V.Component(SceneRender, key: "root"));
            yield return null;
            yield return null;

            // Act — a pointer-down at the overlap region, dispatched through the main panel's own
            // native SendEvent (full panel dispatch: TrickleDown -> Target -> BubbleUp), the same path
            // a real click takes.
            var underlyingEvent = new Event { type = EventType.MouseDown, mousePosition = new Vector2(50, 50), button = 0 };
            using (var evt = PointerDownEvent.GetPooled(underlyingEvent))
            {
                doc.rootVisualElement.panel.visualTree.SendEvent(evt);
            }
            yield return null;

            // Assert
            Assert.That((s_overlayHandlerFired, s_mainHandlerFired), Is.EqualTo((true, false)));
        }

        [Component]
        private static VNode WideMainRender()
        {
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "main-wide-target",
                    events: new FiberEventBinding[] { new PointerDownBinding { Handler = _ => s_mainHandlerFired = true } },
                    className: "absolute left-[0px] top-[0px] w-[300px] h-[300px]"),
                V.Portal(UILayer.Overlay, children: new VNode[] { V.Component(OverlayChildRender) }),
            });
        }

        [UnityTest]
        public IEnumerator Given_AnOverlayLayerOnlyCoversPartOfTheMainPanel_When_PointerDownHitsOutsideIt_Then_TheMainPanelsHandlerFires()
        {
            // Arrange — main-wide-target spans (0,0)-(300,300); the overlay layer's content only spans
            // (0,0)-(100,100) (OverlayChildRender). A point outside the overlay's own footprint but
            // still inside the main element's must fall through to the main panel — the router must not
            // treat "an overlay panel EXISTS" as "the overlay panel has content everywhere".
            _docGo = new GameObject("MainPanel");
            var doc = _docGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            _mounted = V.Mount(doc.rootVisualElement, V.Component(WideMainRender, key: "root"));
            yield return null;
            yield return null;

            // Act — (200, 200) is inside main-wide-target but outside overlay-target's 100x100 footprint.
            var underlyingEvent = new Event { type = EventType.MouseDown, mousePosition = new Vector2(200, 200), button = 0 };
            using (var evt = PointerDownEvent.GetPooled(underlyingEvent))
            {
                doc.rootVisualElement.panel.visualTree.SendEvent(evt);
            }
            yield return null;

            // Assert
            Assert.That((s_mainHandlerFired, s_overlayHandlerFired), Is.EqualTo((true, false)));
        }
    }
}
