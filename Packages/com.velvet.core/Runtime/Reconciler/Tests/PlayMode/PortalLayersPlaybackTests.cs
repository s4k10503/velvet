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
        private ThemeStyleSheet _themeB;
        private RenderTexture _cameraRt;
        private MountedTree _mounted;
        private int _savedTargetFrameRate;

        private static StateUpdater<int> s_bump;

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
            if (_themeB != null) Object.Destroy(_themeB);
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

        // The framework host is whichever live UIDocument's tree contains the marker element.
        private static PanelSettings FindHostSettingsContaining(string elementName)
        {
            PanelSettings found = null;
            foreach (var doc in Resources.FindObjectsOfTypeAll<UIDocument>())
            {
                if (doc.rootVisualElement?.Q<VisualElement>(elementName) != null)
                {
                    found = doc.panelSettings;
                }
            }
            return found;
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
            var hostSettings = FindHostSettingsContaining("inside");
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

        [Component]
        private static VNode TogglingOverlayHost()
        {
            var (_, bump) = Hooks.UseState(0);
            s_bump = bump;
            return V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[] { V.Div(name: "inside") }),
            });
        }

        [UnityTest]
        public IEnumerator Given_ARuntimeThemeSwap_When_TheLayerPortalRerenders_Then_TheHostFollowsIt()
        {
            // Arrange — the host copies the declaring panel's settings (a snapshot, not a reference),
            // so a later runtime swap there must re-sync on the next pass that touches the portal.
            var root = CreateMainPanel(withTheme: true);
            yield return null;
            _mounted = V.Mount(root, V.Component(TogglingOverlayHost, key: "root"));
            yield return null;
            yield return null;
            var hostSettings = FindHostSettingsContaining("inside");
            Assume.That(hostSettings, Is.Not.Null, "Precondition: the layer host exists");
            Assume.That(hostSettings.themeStyleSheet, Is.SameAs(_theme),
                "Precondition: the host copied the initial theme");
            _themeB = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            _settings.themeStyleSheet = _themeB;

            // Act
            s_bump.Invoke(v => v + 1);
            yield return null;
            yield return null;

            // Assert
            Assert.That(hostSettings.themeStyleSheet, Is.SameAs(_themeB));
        }

        [Component]
        private static VNode TogglingWorldSpaceHost()
        {
            var (_, bump) = Hooks.UseState(0);
            s_bump = bump;
            return V.Div(children: new VNode[]
            {
                V.WorldSpace(Vector3.zero, panelSize: new Vector2(400f, 400f), children: new VNode[]
                {
                    V.Div(name: "ws-inside"),
                }),
            });
        }

        [UnityTest]
        public IEnumerator Given_ARuntimeThemeSwap_When_TheWorldSpaceRerenders_Then_TheHostFollowsIt()
        {
            // Arrange — same snapshot semantics as the layer host, re-synced through the world-space
            // patch path.
            var root = CreateMainPanel(withTheme: true);
            yield return null;
            _mounted = V.Mount(root, V.Component(TogglingWorldSpaceHost, key: "root"));
            yield return null;
            yield return null;
            var hostSettings = FindHostSettingsContaining("ws-inside");
            Assume.That(hostSettings, Is.Not.Null, "Precondition: the world-space host exists");
            Assume.That(hostSettings.themeStyleSheet, Is.SameAs(_theme),
                "Precondition: the host copied the initial theme");
            _themeB = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            _settings.themeStyleSheet = _themeB;

            // Act
            s_bump.Invoke(v => v + 1);
            yield return null;
            yield return null;

            // Assert
            Assert.That(hostSettings.themeStyleSheet, Is.SameAs(_themeB));
        }

        [UnityTest]
        public IEnumerator Given_ARuntimeThemeCleared_When_TheLayerPortalRerenders_Then_TheHostDropsTheStaleTheme()
        {
            // Arrange — clearing the declaring theme at runtime is drift like any other change: the
            // host must stop referencing the stale theme (falling back to the shared empty one).
            var root = CreateMainPanel(withTheme: true);
            yield return null;
            _mounted = V.Mount(root, V.Component(TogglingOverlayHost, key: "root"));
            yield return null;
            yield return null;
            var hostSettings = FindHostSettingsContaining("inside");
            Assume.That(hostSettings, Is.Not.Null, "Precondition: the layer host exists");
            Assume.That(hostSettings.themeStyleSheet, Is.SameAs(_theme),
                "Precondition: the host copied the initial theme");
            // Simulate a session where no themeless declaring panel ever forced the shared empty
            // theme into existence — the drift probe must not depend on that side effect (another
            // fixture running first would otherwise mask the regression).
            var sharedField = typeof(PanelHostFactory).GetField("s_sharedEmptyTheme",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assume.That(sharedField, Is.Not.Null, "Precondition: the shared-theme field exists");
            sharedField.SetValue(null, null);
            _settings.themeStyleSheet = null;

            // Act
            s_bump.Invoke(v => v + 1);
            yield return null;
            yield return null;

            // Assert — the stale reference is gone.
            Assert.That(hostSettings.themeStyleSheet, Is.Not.SameAs(_theme));
        }

        [UnityTest]
        public IEnumerator Given_ARuntimeSortingOrderChange_When_TheLayerPortalRerenders_Then_TheHostReanchors()
        {
            // Arrange — layer order anchors to the declaring panel's sortingOrder; a runtime change
            // there must re-anchor the host (base + layer offset), not stay frozen at first resolve.
            var root = CreateMainPanel(withTheme: false);
            yield return null;
            _mounted = V.Mount(root, V.Component(TogglingOverlayHost, key: "root"));
            yield return null;
            yield return null;
            var hostSettings = FindHostSettingsContaining("inside");
            Assume.That(hostSettings, Is.Not.Null, "Precondition: the layer host exists");
            Assume.That(hostSettings.sortingOrder, Is.EqualTo(100f),
                "Precondition: anchored to base 0 plus the overlay offset");
            _settings.sortingOrder = 7f;

            // Act
            s_bump.Invoke(v => v + 1);
            yield return null;
            yield return null;

            // Assert
            Assert.That(hostSettings.sortingOrder, Is.EqualTo(107f));
        }

        [UnityTest]
        public IEnumerator Given_APhysicalSizeDeclaringPanel_When_ALayerPortalMounts_Then_TheHostCopiesTheDpiPair()
        {
            // Arrange — ConstantPhysicalSize scaling is driven by the DPI pair, so a host that copies
            // the scale mode without them scales differently than the panel it was declared on.
            var root = CreateMainPanel(withTheme: false);
            _settings.scaleMode = PanelScaleMode.ConstantPhysicalSize;
            _settings.referenceDpi = 120f;
            _settings.fallbackDpi = 72f;
            yield return null;

            // Act
            _mounted = V.Mount(root, V.Div(children: new VNode[]
            {
                V.Portal(UILayer.Overlay, children: new VNode[] { V.Div(name: "dpi-inside") }),
            }));
            yield return null;
            yield return null;

            // Assert
            var hostSettings = FindHostSettingsContaining("dpi-inside");
            Assume.That(hostSettings, Is.Not.Null, "Precondition: the layer host exists");
            Assert.That((hostSettings.referenceDpi, hostSettings.fallbackDpi), Is.EqualTo((120f, 72f)));
        }
    }
}
