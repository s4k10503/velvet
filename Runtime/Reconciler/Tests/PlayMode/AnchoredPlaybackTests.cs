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
    /// performs internally) independently says it should be — including when nested inside an offset parent
    /// container, and that <c>Props.Visible = false</c> still hides an Anchored element despite the driver's
    /// own inline display writes. Complements <see cref="AnchoredTests"/> (EditMode, wiring + the behind-camera/
    /// no-camera/unsupported-panel hide paths only — an editor-simulated panel cannot exercise the real
    /// runtime-panel projection call).
    /// </summary>
    internal sealed class AnchoredPlaybackTests
    {
        private static Transform s_target;
        private static Camera s_camera;
        private static bool s_nestInOffsetContainer;
        private static bool s_visible = true;

        private GameObject _panelGo;
        private GameObject _cameraGo;
        private GameObject _targetGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        [Component]
        private static VNode AnchoredHost()
        {
            var anchored = V.Anchored(s_target, camera: s_camera, name: "anchored", className: "w-[10px] h-[10px]",
                props: new FiberElementProps { Visible = s_visible });
            // Margin, not padding: position:absolute's containing block is the parent's PADDING edge (CSS/UI
            // Toolkit semantics), so padding alone wouldn't actually move an absolute child's origin — margin
            // shifts the parent's own border box (and everything inside it) away from panel (0,0) instead.
            return s_nestInOffsetContainer
                ? V.Div(className: "m-[40px]", children: new VNode[] { anchored })
                : anchored;
        }

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            s_nestInOffsetContainer = false;
            s_visible = true;

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
        }

        // Mounts AnchoredHost against the CURRENT static flags — separated from UnitySetUp so a test can set
        // s_nestInOffsetContainer / s_visible before the tree is first built.
        private IEnumerator Mount()
        {
            _mounted = V.Mount(_panelGo.GetComponent<UIDocument>().rootVisualElement, V.Component(AnchoredHost, key: "root"));
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
            yield return Mount();
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

            // Assert — worldBound reports the element's ACTUAL rect in panel-root space (unlike
            // resolvedStyle.left/top, which merely echoes the parent-relative offset that was written to
            // style.left/top — for a direct child of the panel root the two coincide, but only there).
            var actualLeft = element.worldBound.x;
            var actualTop = element.worldBound.y;
            Assert.That((Mathf.Abs(actualLeft - expectedLeft) < 5f, Mathf.Abs(actualTop - expectedTop) < 5f),
                Is.EqualTo((true, true)));
        }

        [UnityTest]
        public IEnumerator Given_ATargetInFrontOfTheCamera_When_TheElementIsNestedInAnOffsetParent_Then_ItStillLandsNearTheIndependentlyComputedScreenPoint()
        {
            // Arrange — the Anchored element is no longer a direct child of the panel root: its parent Div
            // sits 40px away from panel (0,0), offset by margin.
            s_nestInOffsetContainer = true;
            yield return Mount();
            var camera = _cameraGo.GetComponent<Camera>();
            var element = _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("anchored");
            Assume.That(element, Is.Not.Null, "Precondition: the Anchored element mounted");
            yield return null;
            yield return null;

            // Act — same independent cross-check as the direct-child test: the expected on-screen point does
            // NOT depend on how the element is nested, only on the target/camera.
            var screenPoint = camera.WorldToScreenPoint(_targetGo.transform.position);
            var expectedLeft = screenPoint.x;
            var expectedTop = Screen.height - screenPoint.y;

            // Assert — worldBound (panel-root space, see the direct-child test's own note) must land at the
            // SAME on-screen point regardless of the parent's own offset from panel (0,0): this is what fails
            // if the driver writes raw panel-space coordinates into a parent-relative style property instead
            // of converting first (the bug this test guards).
            var actualLeft = element.worldBound.x;
            var actualTop = element.worldBound.y;
            Assert.That((Mathf.Abs(actualLeft - expectedLeft) < 5f, Mathf.Abs(actualTop - expectedTop) < 5f),
                Is.EqualTo((true, true)));
        }

        [UnityTest]
        public IEnumerator Given_VisibleIsFalse_When_ATargetInFrontOfTheCameraTicks_Then_TheElementStaysHidden()
        {
            // Arrange
            s_visible = false;
            yield return Mount();
            var element = _panelGo.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("anchored");
            Assume.That(element, Is.Not.Null, "Precondition: the Anchored element mounted");

            // Act
            yield return null;
            yield return null;

            // Assert — the driver's own inline display write must not outrank Props.Visible = false's "hidden"
            // USS class (element.style.display should stay StyleKeyword.Null, not an inline override).
            Assert.That(element.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }
    }
}
#endif
