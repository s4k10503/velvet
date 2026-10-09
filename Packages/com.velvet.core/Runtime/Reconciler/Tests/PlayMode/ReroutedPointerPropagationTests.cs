using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A pointer-down the layer router takes from the main panel to a layer portal's content runs the
    /// content's logical ancestors as React does: their capture handlers before the content's, and their
    /// bubble handlers after it. <see cref="CrossPanelPointerRoutingTests"/> owns which panel the router
    /// picks; this reads what the picked element's ancestors receive.
    /// </summary>
    internal sealed class ReroutedPointerPropagationTests
    {
        private GameObject _docGo;
        private PanelSettings _settings;
        private MountedTree _mounted;
        private TargetFrameRateScope _frameRateScope;
        private static readonly List<string> s_log = new();

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            s_log.Clear();
            s_stopAt = null;
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

        // Which handler stops propagation, in the scene the cases share.
        private static string s_stopAt;

        private static FiberEventBinding[] Logged(string name) => new FiberEventBinding[]
        {
            new PointerDownBinding { Handler = evt => Log(name + "<", evt) },
            new PointerDownBinding { Handler = evt => Log(name + ">", evt), Capture = true },
        };

        private static void Log(string entry, PointerDownEvent evt)
        {
            s_log.Add(entry);
            if (entry == s_stopAt) evt.StopPropagation();
        }

        [Component]
        private static VNode SceneRender() => V.Div(events: Logged("outer"), children: new VNode[]
        {
            V.Portal(UILayer.Overlay, children: new VNode[]
            {
                V.Div(
                    name: "overlay-target",
                    events: Logged("content"),
                    className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]"),
            }),
        });

        private IEnumerator MountAndPress()
        {
            _docGo = new GameObject("MainPanel");
            var doc = _docGo.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            _mounted = V.Mount(doc.rootVisualElement, V.Component(SceneRender, key: "root"));
            yield return null;
            yield return null;

            // Through the main panel's own dispatch, where the router takes the event.
            var underlyingEvent = new Event { type = EventType.MouseDown, mousePosition = new Vector2(50, 50), button = 0 };
            using (var evt = PointerDownEvent.GetPooled(underlyingEvent))
            {
                doc.rootVisualElement.panel.visualTree.SendEvent(evt);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Given_ALayerPortalsContentUnderACapturingAndBubblingAncestor_When_ARoutedPointerDownHitsTheContent_Then_TheAncestorWrapsTheContent()
        {
            // Arrange + Act
            yield return MountAndPress();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("outer>,content>,content<,outer<"));
        }

        [UnityTest]
        public IEnumerator Given_AnAncestorCaptureThatStopsPropagation_When_ARoutedPointerDownHitsTheContent_Then_NothingAfterItRuns()
        {
            // Arrange
            s_stopAt = "outer>";

            // Act
            yield return MountAndPress();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("outer>"));
        }

        [UnityTest]
        public IEnumerator Given_AContentCaptureThatStopsPropagation_When_ARoutedPointerDownHitsTheContent_Then_ItsOwnBubbleHandlerDoesNotRun()
        {
            // Arrange
            s_stopAt = "content>";

            // Act
            yield return MountAndPress();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("outer>,content>"));
        }

        [UnityTest]
        public IEnumerator Given_AContentHandlerThatStopsPropagation_When_ARoutedPointerDownHitsTheContent_Then_TheAncestorDoesNotBubble()
        {
            // Arrange
            s_stopAt = "content<";

            // Act
            yield return MountAndPress();

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("outer>,content>,content<"));
        }
    }
}
