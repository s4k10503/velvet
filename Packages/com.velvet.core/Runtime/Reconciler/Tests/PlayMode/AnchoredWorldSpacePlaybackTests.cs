using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a <c>V.Anchored</c> in a world-space panel sits where the camera's ray to its target
    /// crosses that panel, and hides where the ray does not cross it or no document places the panel. The
    /// world-space host places its panel with the panel's centre on the node's position, 100 panel pixels
    /// to a world unit, so a target on the panel's plane one unit right and half a unit up of that position
    /// lands 100 px right and 50 px up of the centre of a 600x200 panel.
    /// </summary>
    internal sealed class AnchoredWorldSpacePlaybackTests
    {
        private GameObject _docGo;
        private GameObject _cameraGo;
        private GameObject _targetGo;
        private PanelSettings _settings;
        private MountedTree _mounted;
        private Camera _camera;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_docGo != null) Object.Destroy(_docGo);
            if (_cameraGo != null) Object.Destroy(_cameraGo);
            if (_targetGo != null) Object.Destroy(_targetGo);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        // A camera at z = -10 looking down +z, a target at targetPosition, and a document on a panel of the
        // given render mode.
        private UIDocument Arrange(Vector3 targetPosition, PanelRenderMode renderMode)
        {
            _docGo = new GameObject("AnchoredWorldSpaceDocument");
            var doc = _docGo.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.renderMode = renderMode;
            doc.panelSettings = _settings;
            _cameraGo = new GameObject("AnchoredWorldSpaceCamera");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            _targetGo = new GameObject("AnchoredWorldSpaceTarget");
            _targetGo.transform.position = targetPosition;
            return doc;
        }

        private VNode WorldSpaceWithAnchored(Vector3 panelPosition) => V.Div(children: new VNode[]
        {
            V.WorldSpace(panelPosition, panelSize: new Vector2(600f, 200f), children: new VNode[]
            {
                // Fills the panel, so the host's bounding box is the panel's own rect.
                V.Div(className: "w-[600px] h-[200px]"),
                V.Anchored(_targetGo.transform, camera: _camera, name: "anchored", className: "w-[10px] h-[10px]"),
            }),
        });

        private static IEnumerator Frames()
        {
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Given_AnAnchoredInsideAWorldSpacePanel_When_ItsTargetLiesOnThatPanel_Then_ItSitsOverTheTarget()
        {
            // Arrange — the panel sits away from the origin, so reading it off any other document's transform
            // lands elsewhere.
            var doc = Arrange(new Vector3(3f, 1.5f, 0f), PanelRenderMode.ScreenSpaceOverlay);
            yield return null;

            // Act
            _mounted = V.Mount(doc.rootVisualElement, WorldSpaceWithAnchored(new Vector3(2f, 1f, 0f)));
            yield return Frames();

            // Assert — a hidden element reads NaN, since an unwritten left would otherwise compare as 0.
            var element = FindElement("anchored");
            var shown = element.style.display.value != DisplayStyle.None;
            Assert.That(
                new[] { shown ? element.style.left.value.value : float.NaN, element.style.top.value.value },
                Is.EqualTo(new[] { 400f, 50f }).Within(1f));
        }

        [UnityTest]
        public IEnumerator Given_AWorldSpacePanelBehindTheCamera_When_ItsAnchoredTracksATargetInFront_Then_ItIsHidden()
        {
            // Arrange — the camera's ray to the target runs away from the panel, so it never crosses it.
            var doc = Arrange(new Vector3(0f, 0f, 0f), PanelRenderMode.ScreenSpaceOverlay);
            yield return null;

            // Act
            _mounted = V.Mount(doc.rootVisualElement, WorldSpaceWithAnchored(new Vector3(0f, 0f, -20f)));
            yield return Frames();

            // Assert
            Assert.That(FindElement("anchored").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator Given_AWorldSpacePanelThroughTheCamera_When_ItsAnchoredTracksATargetInFront_Then_ItIsHidden()
        {
            // Arrange — the panel's plane holds the camera, so the ray meets it only where it starts.
            var doc = Arrange(new Vector3(0f, 0f, 0f), PanelRenderMode.ScreenSpaceOverlay);
            yield return null;

            // Act
            _mounted = V.Mount(doc.rootVisualElement, WorldSpaceWithAnchored(new Vector3(0f, 0f, -10f)));
            yield return Frames();

            // Assert
            Assert.That(FindElement("anchored").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator Given_AnAnchoredUnderNoDocumentOfAWorldSpacePanel_When_Ticked_Then_ItIsHidden()
        {
            // Arrange — an element added straight to the panel's visual tree, so no document places it.
            var doc = Arrange(new Vector3(0f, 0f, 0f), PanelRenderMode.WorldSpace);
            yield return null;
            var loose = new VisualElement();
            doc.rootVisualElement.panel.visualTree.Add(loose);

            // Act
            _mounted = V.Mount(loose, V.Anchored(_targetGo.transform, camera: _camera, name: "anchored"));
            yield return Frames();

            // Assert
            Assert.That(loose.Q<VisualElement>("anchored").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        private static VisualElement FindElement(string name)
        {
            foreach (var document in Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                var found = document.rootVisualElement?.Q<VisualElement>(name);
                if (found != null)
                {
                    return found;
                }
            }
            throw new System.InvalidOperationException($"No document holds \"{name}\".");
        }
    }
}
