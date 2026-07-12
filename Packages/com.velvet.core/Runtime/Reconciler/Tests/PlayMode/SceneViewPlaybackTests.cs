using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that V.SceneView actually SHOWS the camera's rendered output on a real runtime panel:
    /// a solid-color camera fills the element with that color (including the sRGB/linear round-trip
    /// this URP project renders with), and a later camera color change appears WITHOUT any Velvet
    /// re-render — the element samples the live RenderTexture, it does not copy it.
    /// </summary>
    internal sealed class SceneViewPlaybackTests
    {
        private GameObject _cameraGo;
        private GameObject _docGo;
        private PanelSettings _settings;
        private RenderTexture _panelRt;
        private MountedTree _mounted;
        private int _savedTargetFrameRate;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _savedTargetFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = 120;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Application.targetFrameRate = _savedTargetFrameRate;
            _mounted?.Dispose();
            _mounted = null;
            if (_docGo != null) Object.Destroy(_docGo);
            if (_cameraGo != null) Object.Destroy(_cameraGo);
            if (_settings != null) Object.Destroy(_settings);
            if (_panelRt != null) { _panelRt.Release(); Object.Destroy(_panelRt); }
            yield return null;
        }

        private Camera CreateSolidColorCamera(Color color)
        {
            _cameraGo = new GameObject("SceneViewCam");
            var cam = _cameraGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = color;
            cam.cullingMask = 0; // nothing but the clear color
            return cam;
        }

        private VisualElement MountPanelWithSceneView(Camera cam)
        {
            _panelRt = new RenderTexture(400, 300, 32);
            _docGo = new GameObject("SceneViewPanel");
            var doc = _docGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.targetTexture = _panelRt;
            doc.panelSettings = _settings;
            _mounted = V.Mount(doc.rootVisualElement,
                V.SceneView(cam, className: "w-[200px] h-[150px]", name: "sv"));
            return doc.rootVisualElement;
        }

        private Color32 SamplePanelCenterOfElement()
        {
            var prev = RenderTexture.active;
            RenderTexture.active = _panelRt;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            // The element sits at the panel's top-left; sample its center (ReadPixels is bottom-origin).
            tex.ReadPixels(new Rect(100, 300 - 75, 1, 1), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var c = tex.GetPixel(0, 0);
            Object.Destroy(tex);
            return c;
        }

        private static IEnumerator WaitRealtime(double seconds)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Given_ASolidColorCamera_When_Rendered_Then_TheElementShowsTheCameraColor()
        {
            // Arrange
            var cam = CreateSolidColorCamera(Color.red);

            // Act
            MountPanelWithSceneView(cam);
            yield return WaitRealtime(0.5);

            // Assert — the element's pixels carry the camera's clear color (loose bounds absorb the
            // sRGB/linear round-trip; a missing wire-up leaves the panel's default background instead).
            var c = SamplePanelCenterOfElement();
            Assert.That(c.r > 180 && c.g < 80 && c.b < 80, Is.True,
                $"Expected a red pixel from the camera output, got ({c.r},{c.g},{c.b})");
        }

        [UnityTest]
        public IEnumerator Given_ACameraColorChange_When_FramesAdvance_Then_TheElementFollowsWithoutARerender()
        {
            // Arrange
            var cam = CreateSolidColorCamera(Color.red);
            MountPanelWithSceneView(cam);
            yield return WaitRealtime(0.5);
            var before = SamplePanelCenterOfElement();
            Assume.That(before.r > 180, Is.True, "Precondition: the initial camera color rendered");

            // Act — no Velvet re-render: the element must sample the live texture.
            cam.backgroundColor = Color.blue;
            yield return WaitRealtime(0.5);

            // Assert
            var c = SamplePanelCenterOfElement();
            Assert.That(c.b > 180 && c.r < 80, Is.True,
                $"Expected the element to follow the camera to blue, got ({c.r},{c.g},{c.b})");
        }
    }
}
