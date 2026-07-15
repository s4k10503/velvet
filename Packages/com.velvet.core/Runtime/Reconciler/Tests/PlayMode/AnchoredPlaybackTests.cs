#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that <c>V.Anchored</c> actually projects onto a real runtime panel: on a real UIDocument, a target
    /// in front of the camera lands the element's inline left/top near where <c>Camera.WorldToScreenPoint</c>
    /// (Y-flipped for panel space, the same conversion <c>RuntimePanelUtils.CameraTransformWorldToPanel</c>
    /// performs internally) independently says it should be. Complements <see cref="AnchoredTests"/> (EditMode,
    /// wiring + the behind-camera path only — an editor-simulated panel cannot exercise the real runtime-panel
    /// projection call).
    /// </summary>
    internal sealed class AnchoredPlaybackTests
    {
        private static Transform s_target;
        private static Camera s_camera;

        private GameObject _panelGo;
        private GameObject _cameraGo;
        private GameObject _targetGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        [Component]
        private static VNode AnchoredHost()
            => V.Anchored(s_target, camera: s_camera, name: "anchored", className: "w-[10px] h-[10px]");

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _panelGo = new GameObject("AnchoredPlaybackPanel");
            var doc = _panelGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            doc.panelSettings = _settings;
            yield return null;
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss");
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            doc.rootVisualElement.styleSheets.Add(sheet);

            _cameraGo = new GameObject("AnchoredPlaybackCamera");
            s_camera = _cameraGo.AddComponent<Camera>();
            s_camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            s_camera.fieldOfView = 60f;
            _targetGo = new GameObject("AnchoredPlaybackTarget");
            _targetGo.transform.position = Vector3.zero;
            s_target = _targetGo.transform;

            _mounted = V.Mount(doc.rootVisualElement, V.Component(AnchoredHost, key: "root"));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            s_target = null;
            s_camera = null;
            if (_panelGo != null) Object.Destroy(_panelGo);
            if (_cameraGo != null) Object.Destroy(_cameraGo);
            if (_targetGo != null) Object.Destroy(_targetGo);
            if (_settings != null) Object.Destroy(_settings);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Given_ATargetInFrontOfTheCamera_When_ARealPanelTicks_Then_TheElementLandsNearTheIndependentlyComputedScreenPoint()
        {
            // Arrange
            var camera = _cameraGo.GetComponent<Camera>();
            var element = _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("anchored");
            Assume.That(element, Is.Not.Null, "Precondition: the Anchored element mounted");
            yield return null;
            yield return null;

            // Act — independently compute the expected panel-space point: WorldToScreenPoint, Y-flipped for
            // panel space (screen Y grows up, panel Y grows down) — the same conversion the driver's own call
            // to RuntimePanelUtils.CameraTransformWorldToPanel performs internally, recomputed here rather
            // than re-calling that same API, so this is a real cross-check and not a tautology.
            var screenPoint = camera.WorldToScreenPoint(_targetGo.transform.position);
            var expectedLeft = screenPoint.x;
            var expectedTop = Screen.height - screenPoint.y;

            // Assert — within a small pixel tolerance (DPI rounding / a frame's worth of camera settle).
            var actualLeft = element.resolvedStyle.left;
            var actualTop = element.resolvedStyle.top;
            Assert.That((Mathf.Abs(actualLeft - expectedLeft) < 5f, Mathf.Abs(actualTop - expectedTop) < 5f),
                Is.EqualTo((true, true)));
        }
    }
}
#endif
