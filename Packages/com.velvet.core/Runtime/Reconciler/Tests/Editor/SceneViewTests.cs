using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <c>V.SceneView</c> — a Camera's output as an element:
    /// <list type="bullet">
    /// <item>Mounting creates the dedicated <see cref="SceneViewElement"/>; once layout resolves, the
    /// framework creates a RenderTexture at the element's laid-out pixel size (times the resolution
    /// scale), assigns it to <c>camera.targetTexture</c>, and shows it through the element's background
    /// image — so background utilities and rounded corners apply to the camera output.</item>
    /// <item>The texture follows the element: a geometry change re-sizes it, a camera swap releases the
    /// old camera and targets the new one, and removing the camera (or the element, or disposing the
    /// tree) releases both the camera's target and the texture.</item>
    /// <item>Release is polite: a camera whose targetTexture was reassigned by user code after mount is
    /// left untouched on unmount.</item>
    /// <item>A null camera mounts an inert element; a zero-sized element creates no texture.</item>
    /// </list>
    /// The EditMode panel comes from <see cref="HeadlessEditorPanelHost"/> and layout is driven
    /// explicitly (the scheduler does not tick in EditMode).
    /// </summary>
    internal sealed class SceneViewTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private readonly List<Object> _spawned = new();

        private static StateUpdater<bool> s_setFlag;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            foreach (var obj in _spawned)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _spawned.Clear();
        }

        private Camera CreateCamera(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<Camera>();
        }

        private void MountAndLayout(VNode node)
        {
            _mounted = V.Mount(_host.Root, node);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private void FlushAndLayout()
        {
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        #region Mount and texture creation

        [Test]
        public void Given_ASceneViewNode_When_Mounted_Then_ItCreatesTheDedicatedElement()
        {
            // Arrange
            var cam = CreateCamera("cam");

            // Act
            MountAndLayout(V.SceneView(cam, className: "w-[128px] h-[64px]", name: "sv"));

            // Assert
            Assert.That(_host.Root.Q<VisualElement>("sv"), Is.InstanceOf<SceneViewElement>());
        }

        [Test]
        public void Given_AMountedSceneView_When_LayoutResolves_Then_TheCameraTargetsAFrameworkTexture()
        {
            // Arrange
            var cam = CreateCamera("cam");

            // Act
            MountAndLayout(V.SceneView(cam, className: "w-[128px] h-[64px]"));

            // Assert
            Assert.That(cam.targetTexture, Is.Not.Null);
        }

        [Test]
        public void Given_AMountedSceneView_When_LayoutResolves_Then_TheTextureMatchesTheLaidOutSize()
        {
            // Arrange
            var cam = CreateCamera("cam");

            // Act
            MountAndLayout(V.SceneView(cam, className: "w-[128px] h-[64px]"));

            // Assert
            Assume.That(cam.targetTexture, Is.Not.Null, "Precondition: the camera received a texture");
            Assert.That((cam.targetTexture.width, cam.targetTexture.height), Is.EqualTo((128, 64)));
        }

        [Test]
        public void Given_AResolutionScale_When_LayoutResolves_Then_TheTextureIsScaled()
        {
            // Arrange
            var cam = CreateCamera("cam");

            // Act — half resolution over a 128x64 laid-out rect.
            MountAndLayout(V.SceneView(cam, className: "w-[128px] h-[64px]", resolutionScale: 0.5f));

            // Assert
            Assume.That(cam.targetTexture, Is.Not.Null, "Precondition: the camera received a texture");
            Assert.That((cam.targetTexture.width, cam.targetTexture.height), Is.EqualTo((64, 32)));
        }

        [Test]
        public void Given_AMountedSceneView_When_LayoutResolves_Then_TheBackgroundCarriesTheSameTexture()
        {
            // Arrange
            var cam = CreateCamera("cam");

            // Act
            MountAndLayout(V.SceneView(cam, className: "w-[128px] h-[64px]", name: "sv"));

            // Assert — the element displays the exact texture the camera renders into. Read through
            // resolvedStyle: this editor's INLINE backgroundImage getter reconstructs the value without a
            // RenderTexture case (a stored RT reads back as Null), while the computed style round-trips it.
            Assume.That(cam.targetTexture, Is.Not.Null, "Precondition: the camera received a texture");
            var el = _host.Root.Q<VisualElement>("sv");
            Assert.That(el.resolvedStyle.backgroundImage.renderTexture, Is.SameAs(cam.targetTexture));
        }

        [Test]
        public void Given_ANullCamera_When_Mounted_Then_TheElementMountsInert()
        {
            // Act — no camera yet: the element is an empty box until one is supplied.
            MountAndLayout(V.SceneView(null, className: "w-[128px] h-[64px]", name: "sv"));

            // Assert — read through resolvedStyle (the inline getter cannot round-trip a RenderTexture,
            // so it would pass vacuously here even if a texture had leaked in).
            var el = _host.Root.Q<VisualElement>("sv");
            Assert.That(el.resolvedStyle.backgroundImage.renderTexture, Is.Null);
        }

        [Test]
        public void Given_AZeroSizedElement_When_LayoutResolves_Then_NoTextureIsCreated()
        {
            // Arrange
            var cam = CreateCamera("cam");

            // Act — a zero-sized rect has no pixels to render into; creating a texture would throw.
            MountAndLayout(V.SceneView(cam, className: "w-[0px] h-[0px]"));

            // Assert
            Assert.That(cam.targetTexture, Is.Null);
        }

        #endregion

        #region Updates

        [Component]
        private static VNode ResizingHost()
        {
            var (wide, setWide) = Hooks.UseState(false);
            s_setFlag = setWide;
            return V.SceneView(s_camera, className: wide ? "w-[200px] h-[64px]" : "w-[128px] h-[64px]");
        }

        private static Camera s_camera;

        [Test]
        public void Given_AGeometryChange_When_TheElementResizes_Then_TheTextureFollowsTheNewSize()
        {
            // Arrange
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(ResizingHost, key: "root"));
            Assume.That((s_camera.targetTexture?.width ?? 0), Is.EqualTo(128),
                "Precondition: the initial texture matches the initial layout");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That((s_camera.targetTexture.width, s_camera.targetTexture.height), Is.EqualTo((200, 64)));
        }

        [Component]
        private static VNode SwappingHost()
        {
            var (useB, setUseB) = Hooks.UseState(false);
            s_setFlag = setUseB;
            return V.SceneView(useB ? s_cameraB : s_camera, className: "w-[128px] h-[64px]");
        }

        private static Camera s_cameraB;

        [Test]
        public void Given_ACameraSwap_When_Repatched_Then_TheOldCameraIsReleasedAndTheNewOneTargets()
        {
            // Arrange
            s_camera = CreateCamera("camA");
            s_cameraB = CreateCamera("camB");
            MountAndLayout(V.Component(SwappingHost, key: "root"));
            Assume.That(s_camera.targetTexture, Is.Not.Null, "Precondition: camera A targets the texture");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — A released, B targeting.
            Assert.That((s_camera.targetTexture == null, s_cameraB.targetTexture != null),
                Is.EqualTo((true, true)));
        }

        [Component]
        private static VNode CameraToNullHost()
        {
            var (removed, setRemoved) = Hooks.UseState(false);
            s_setFlag = setRemoved;
            return V.SceneView(removed ? null : s_camera, className: "w-[128px] h-[64px]");
        }

        [Test]
        public void Given_ACameraRemoved_When_RepatchedToNull_Then_TheCameraAndTextureAreReleased()
        {
            // Arrange
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(CameraToNullHost, key: "root"));
            var rt = s_camera.targetTexture;
            Assume.That(rt, Is.Not.Null, "Precondition: the camera targets the texture");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — the camera is untargeted and the framework texture is destroyed.
            Assert.That((s_camera.targetTexture == null, rt == null), Is.EqualTo((true, true)));
        }

        #endregion

        #region Teardown

        [Test]
        public void Given_AnUnmount_When_TheTreeDisposes_Then_TheCameraAndTextureAreReleased()
        {
            // Arrange
            var cam = CreateCamera("cam");
            MountAndLayout(V.SceneView(cam, className: "w-[128px] h-[64px]"));
            var rt = cam.targetTexture;
            Assume.That(rt, Is.Not.Null, "Precondition: the camera targets the texture");

            // Act
            _mounted.Dispose();
            _mounted = null;

            // Assert
            Assert.That((cam.targetTexture == null, rt == null), Is.EqualTo((true, true)));
        }

        [Component]
        private static VNode ConditionalHost()
        {
            var (removed, setRemoved) = Hooks.UseState(false);
            s_setFlag = setRemoved;
            return V.Div(children: new VNode[]
            {
                removed ? null : V.SceneView(s_camera, key: "sv", className: "w-[128px] h-[64px]"),
            });
        }

        [Test]
        public void Given_AConditionalRemoval_When_TheSceneViewLeavesTheTree_Then_TeardownRuns()
        {
            // Arrange
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(ConditionalHost, key: "root"));
            var rt = s_camera.targetTexture;
            Assume.That(rt, Is.Not.Null, "Precondition: the camera targets the texture");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That((s_camera.targetTexture == null, rt == null), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AUserReassignedTarget_When_Unmounted_Then_TheForeignTargetIsLeftIntact()
        {
            // Arrange — user code re-pointed the camera after mount; the framework must not stomp it.
            var cam = CreateCamera("cam");
            MountAndLayout(V.SceneView(cam, className: "w-[128px] h-[64px]"));
            Assume.That(cam.targetTexture, Is.Not.Null, "Precondition: the camera targets the texture");
            var foreign = new RenderTexture(16, 16, 0);
            _spawned.Add(foreign);
            cam.targetTexture = foreign;

            // Act
            _mounted.Dispose();
            _mounted = null;

            // Assert
            Assert.That(cam.targetTexture, Is.SameAs(foreign));
        }

        #endregion
    }
}
