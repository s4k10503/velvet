using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a <c>pointer-events-none</c> overlay neither lights its own <c>hover:</c> payload nor keeps the
    /// element behind it from lighting its own, under a runtime panel's own pointer dispatch.
    /// </summary>
    internal sealed class PointerEventsHoverPlaybackTests
    {
        private GameObject _panelGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _panelGo = new GameObject("PointerEventsPanel");
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

        [UnityTest]
        public IEnumerator Given_APointerEventsNoneOverlayInFrontOfAnElement_When_ThePointerMovesOntoTheOverlap_Then_OnlyTheElementBehindLightsItsHoverPayload()
        {
            // Arrange — sizes and the overlap are arbitrary values, which resolve to inline style, so the panel
            // needs no stylesheet; the payloads are only read off the class list.
            var root = _panelGo.GetComponent<UIDocument>().rootVisualElement;
            _mounted = V.Mount(root, V.Div(children: new VNode?[]
            {
                V.Div(name: "behind", className: "w-[100px] h-[100px] hover:bg-red-500"),
                V.Div(name: "overlay", className: "w-[100px] h-[100px] mt-[-100px] pointer-events-none hover:bg-blue-500"),
            }));
            yield return null;
            yield return null;
            var behind = root.Q<VisualElement>("behind");
            var overlay = root.Q<VisualElement>("overlay");

            // Act
            using (var evt = PointerMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = new Vector2(50f, 50f) }))
            {
                root.panel.visualTree.SendEvent(evt);
            }

            // Assert — that the overlay covers the point rides along, since two elements laid out apart would
            // also leave the overlay unlit.
            Assert.That(
                (overlay.worldBound.Contains(new Vector2(50f, 50f)), behind.ClassListContains("bg-red-500"),
                    overlay.ClassListContains("bg-blue-500")),
                Is.EqualTo((true, true, false)));
        }
    }
}
