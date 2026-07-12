using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the runtime-panel halves of the layer-portal contract: a layer host inherits the main
    /// panel's theme (so layer UI resolves the same styles), and a world-space panel actually RENDERS
    /// where a scene camera can see it — the depth-tested path screen-space layers cannot take.
    /// </summary>
    internal sealed class PortalLayersPlaybackTests
    {
        private GameObject _docGo;
        private GameObject _cameraGo;
        private PanelSettings _settings;
        private ThemeStyleSheet _theme;
        private RenderTexture _cameraRt;
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
            if (_theme != null) Object.Destroy(_theme);
            if (_cameraRt != null) { _cameraRt.Release(); Object.Destroy(_cameraRt); }
            yield return null;
        }

        private VisualElement CreateMainPanel(bool withTheme)
        {
            _docGo = new GameObject("MainPanel");
            var doc = _docGo.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            if (withTheme)
            {
                _theme = ScriptableObject.CreateInstance<ThemeStyleSheet>();
                _settings.themeStyleSheet = _theme;
            }
            doc.panelSettings = _settings;
            return doc.rootVisualElement;
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
        public IEnumerator Given_AMainPanelWithATheme_When_ALayerPortalMounts_Then_TheHostInheritsIt()
        {
            // Arrange
            var root = CreateMainPanel(withTheme: true);
            yield return null;

            // Act
            _mounted = V.Mount(root, V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[] { V.Div(name: "inside") }),
            }));
            yield return null;
            yield return null;

            // Assert — the layer host resolves the same theme as the panel the portal was declared on.
            PanelSettings hostSettings = null;
            foreach (var doc in Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                if (doc.rootVisualElement?.Q<VisualElement>("inside") != null)
                {
                    hostSettings = doc.panelSettings;
                }
            }
            Assume.That(hostSettings, Is.Not.Null, "Precondition: the layer host exists");
            Assert.That(hostSettings.themeStyleSheet, Is.SameAs(_theme));
        }

        [UnityTest]
        public IEnumerator Given_AWorldSpacePanel_When_ASceneCameraLooksAtIt_Then_ItsContentRenders()
        {
            // Arrange — a camera facing the world-space panel; screen-space layers are invisible to
            // cameras, world-space panels are drawn in them (the depth-tested path).
            var root = CreateMainPanel(withTheme: false);
            yield return null;
            _cameraGo = new GameObject("cam");
            var cam = _cameraGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            _cameraGo.transform.position = new Vector3(0f, 0f, -3f);
            _cameraRt = new RenderTexture(200, 200, 24);
            cam.targetTexture = _cameraRt;

            // Act — a red-filled world-space panel at the origin, sized small so it fits the view.
            _mounted = V.Mount(root, V.Div(children: new VNode[]
            {
                V.WorldSpace(Vector3.zero, panelSize: new Vector2(400f, 400f), children: new VNode[]
                {
                    // Inline arbitrary color: the test attaches no USS, so a palette class (bg-red-500)
                    // would resolve to transparent on every panel here.
                    V.Div(name: "fill", className: "w-[400px] h-[400px] bg-[#ef4444]"),
                }),
            }));
            yield return WaitRealtime(0.6);

            // Assert — the camera's output carries the panel's red pixels.
            var prev = RenderTexture.active;
            RenderTexture.active = _cameraRt;
            var tex = new Texture2D(200, 200, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 200, 200), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var redPixels = 0;
            foreach (var p in tex.GetPixels32())
            {
                if (p.r > 140 && p.g < 90 && p.b < 90) redPixels++;
            }
            Object.Destroy(tex);
            Assert.That(redPixels, Is.GreaterThan(50));
        }
    }
}
