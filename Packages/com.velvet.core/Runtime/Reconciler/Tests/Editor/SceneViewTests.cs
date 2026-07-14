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
            // Arrange & Act — no camera yet: the element is an empty box until one is supplied.
            MountAndLayout(V.SceneView(null, className: "w-[128px] h-[64px]", name: "sv"));

            // Assert — read through resolvedStyle (the inline getter cannot round-trip a RenderTexture,
            // so it would pass vacuously here even if a texture had leaked in).
            var el = _host.Root.Q<VisualElement>("sv");
            Assert.That(el.resolvedStyle.backgroundImage.renderTexture, Is.Null);
        }

        [Test]
        public void Given_ASceneViewMountedThroughAMotion_When_LayoutResolves_Then_TheCameraTargetsAFrameworkTexture()
        {
            // Arrange — a Motion node can host any element type; a SceneView mounted through one must
            // bind its camera exactly like the plain element path.
            var cam = CreateCamera("cam");
            var props = new FiberElementProps { SceneView = new SceneViewSettings(cam) };

            // Act
            MountAndLayout(V.Motion(elementType: typeof(SceneViewElement), props: props,
                className: "w-[128px] h-[64px]"));

            // Assert
            Assert.That(cam.targetTexture, Is.Not.Null);
        }

        [Test]
        public void Given_AnInvalidResolutionScale_When_TheFactoryRuns_Then_ItThrows()
        {
            // Arrange
            var cam = CreateCamera("cam");

            // Act & Assert — an invalid required numeric factory argument fails fast at the call site,
            // like the other factories with numeric preconditions, instead of silently rendering at 1x.
            Assert.Throws<System.ArgumentOutOfRangeException>(() => V.SceneView(cam, resolutionScale: 0f));
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

            // Assert — 200 rounds up to the next 16px quantization step (208); 64 already lands on one.
            Assert.That((s_camera.targetTexture.width, s_camera.targetTexture.height), Is.EqualTo((208, 64)));
        }

        [Component]
        private static VNode SubStepResizeHost()
        {
            var (grown, setGrown) = Hooks.UseState(false);
            s_setFlag = setGrown;
            return V.SceneView(s_camera, className: grown ? "w-[105px] h-[64px]" : "w-[100px] h-[64px]");
        }

        [Test]
        public void Given_ASubStepSizeDelta_When_TheElementResizes_Then_TheRenderTextureIsNotReallocated()
        {
            // Arrange — 100px and 105px both round up to the same 112px bucket at the 16px
            // quantization step, so this resize must reuse the existing RenderTexture instead of
            // reallocating a new one for a size delta smaller than the step.
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(SubStepResizeHost, key: "root"));
            var initial = s_camera.targetTexture;
            Assume.That(initial, Is.Not.Null, "Precondition: the camera targets a texture");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That(s_camera.targetTexture, Is.SameAs(initial));
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

        [Component]
        private static VNode StylesFlipHost()
        {
            var (cleared, setCleared) = Hooks.UseState(false);
            s_setFlag = setCleared;
            return V.SceneView(s_camera, className: "w-[128px] h-[64px]", name: "sv",
                styles: cleared ? null : new StyleOverrides { BackgroundImage = new StyleBackground(s_poster) });
        }

        private static Texture2D s_poster;

        [Test]
        public void Given_AStyleOverrideBackgroundChange_When_Repatched_Then_TheLiveCameraTextureSurvives()
        {
            // Arrange — a poster placeholder passed through styles, cleared on a later render. The style
            // diff must not blank the camera output the driver owns on the same property.
            s_camera = CreateCamera("cam");
            s_poster = new Texture2D(4, 4);
            _spawned.Add(s_poster);
            MountAndLayout(V.Component(StylesFlipHost, key: "root"));
            Assume.That(s_camera.targetTexture, Is.Not.Null, "Precondition: the camera targets the texture");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            var el = _host.Root.Q<VisualElement>("sv");
            Assert.That(el.resolvedStyle.backgroundImage.renderTexture, Is.SameAs(s_camera.targetTexture));
        }

        #endregion

        #region Teardown

        [Component]
        private static VNode TypeSwapHost()
        {
            var (swapped, setSwapped) = Hooks.UseState(false);
            s_setFlag = setSwapped;
            return swapped
                ? V.Div(key: "x", className: "w-[128px] h-[64px]")
                : V.SceneView(s_camera, key: "x", className: "w-[128px] h-[64px]");
        }

        [Test]
        public void Given_ASameKeyTypeSwap_When_TheSceneViewBecomesADiv_Then_TeardownRuns()
        {
            // Arrange — a same-key element-type change remounts (never patches across types), and the
            // outgoing SceneView must release its camera and texture through that path too.
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(TypeSwapHost, key: "root"));
            var rt = s_camera.targetTexture;
            Assume.That(rt, Is.Not.Null, "Precondition: the camera targets the texture");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That((s_camera.targetTexture == null, rt == null), Is.EqualTo((true, true)));
        }

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

        #region Driver hardening

        [Component]
        private static VNode ShiftingColumnHost()
        {
            var (shifted, setShifted) = Hooks.UseState(false);
            s_setFlag = setShifted;
            return V.Div(className: "flex-col", children: new VNode[]
            {
                V.Div(key: "spacer", className: shifted ? "w-[10px] h-[40px]" : "w-[10px] h-[10px]"),
                V.SceneView(s_camera, key: "sv", className: "w-[128px] h-[64px]"),
            });
        }

        [Test]
        public void Given_AUserReassignedTarget_When_AnUnrelatedGeometryEventFires_Then_TheForeignTargetSurvives()
        {
            // Arrange — user code borrowed the camera while the element stays mounted; a sibling's
            // resize then moves the element (same size, new position), which fires a geometry event.
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(ShiftingColumnHost, key: "root"));
            Assume.That(s_camera.targetTexture, Is.Not.Null, "Precondition: the camera targets the texture");
            var foreign = new RenderTexture(16, 16, 0);
            _spawned.Add(foreign);
            s_camera.targetTexture = foreign;

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — a layout-driven resync must not claw the camera back from user code; only an
            // explicit camera/settings change may claim a foreign target.
            Assert.That(s_camera.targetTexture, Is.SameAs(foreign));
        }

        [Test]
        public void Given_AnElementWiderThanTheTextureCap_When_TheTextureIsCreated_Then_TheAspectIsPreserved()
        {
            // Arrange — 6000×300 points is 20:1; clamping each axis independently to the 4096 ceiling
            // would flatten the texture to ~13.7:1 and visibly distort the camera picture.
            var cam = CreateCamera("cam");

            // Act
            MountAndLayout(V.SceneView(cam, className: "shrink-0 w-[6000px] h-[300px]"));

            // Assert
            var rt = cam.targetTexture;
            Assume.That(rt, Is.Not.Null, "Precondition: the camera received a texture");
            Assume.That(rt.width, Is.EqualTo(4096), "Precondition: the width hit the ceiling");
            var aspect = (float)rt.width / rt.height;
            Assert.That((rt.height <= 4096, Mathf.Abs(aspect - 20f) < 0.5f), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AnElementFarPastTheTextureCap_When_TheSizeIsQuantized_Then_ItNeverExceedsTheCap()
        {
            // Arrange — a square element far past the 4096 ceiling forces the aspect-preserving
            // shrink to land both axes at the cap; quantizing that result up must not carry either
            // axis past it into the next 16px bucket.
            var cam = CreateCamera("cam");

            // Act
            MountAndLayout(V.SceneView(cam, className: "shrink-0 w-[5000px] h-[5000px]"));

            // Assert
            var rt = cam.targetTexture;
            Assume.That(rt, Is.Not.Null, "Precondition: the camera received a texture");
            Assert.That((rt.width <= 4096, rt.height <= 4096), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AStalePixelScale_When_TheEditorRepaintTickFires_Then_TheTextureIsRederived()
        {
            // Arrange — a pixel-density change (a monitor-DPI move) alters the derived pixel size with
            // no geometry event (points are unchanged) and no props pass, so only the recurring editor
            // repaint tick can notice. Staleness is simulated by doubling the scale on the live
            // binding directly — the same derivation input a density change moves.
            var cam = CreateCamera("cam");
            MountAndLayout(V.SceneView(cam, className: "w-[128px] h-[64px]", name: "sv"));
            Assume.That(cam.targetTexture, Is.Not.Null, "Precondition: the camera targets the texture");
            var element = _host.Root.Q<VisualElement>("sv");
            var binding = _mounted.Root.Reconciler.Context.SceneViewBindings[element];
            binding.Settings = new SceneViewSettings(cam, 2f);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That((cam.targetTexture.width, cam.targetTexture.height), Is.EqualTo((256, 128)));
        }

        [Test]
        public void Given_ADirectlyConstructedSettings_When_TheScaleIsInvalid_Then_ItThrows()
        {
            // Arrange — the factory's fail-fast guard must hold for every construction path (Motion
            // hosts and fixtures build the settings record directly), or an invalid scale silently
            // degrades to a degenerate near-1-pixel texture instead of the documented exception.
            // Act & Assert
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SceneViewSettings(null, 0f));
        }

        [Test]
        public void Given_AnInvalidScale_When_TheFactoryThrows_Then_NoPooledPropsLeak()
        {
            // Arrange — the factory rents a pooled props bag; a throwing validation must not leave
            // it stranded in the pool's ownership ledger (each failing render would leak one).
            var ledger = typeof(VNodePool).GetField("s_ownedProps",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assume.That(ledger, Is.Not.Null, "Precondition: the pool ownership ledger exists");
            var set = ledger.GetValue(null);
            var count = set.GetType().GetProperty("Count");
            Assume.That(count, Is.Not.Null, "Precondition: the ledger exposes a count");
            var before = (int)count.GetValue(set);

            // Act
            try
            {
                V.SceneView(null, resolutionScale: 0f);
            }
            catch (System.ArgumentOutOfRangeException)
            {
            }

            // Assert
            var after = (int)count.GetValue(set);
            Assert.That(after, Is.EqualTo(before));
        }

        [Component]
        private static VNode ResizingSceneHost()
        {
            var (wide, setWide) = Hooks.UseState(false);
            s_setFlag = setWide;
            return V.SceneView(s_camera, key: "sv", name: "sv",
                className: wide ? "w-[256px] h-[64px]" : "w-[128px] h-[64px]");
        }

        [Test]
        public void Given_AUserReassignedTarget_When_TheElementResizes_Then_TheLastFrameSurvives()
        {
            // Arrange — user code borrowed the camera; a later RESIZE must not swap the background
            // to a fresh texture nothing renders into, nor destroy the frame being shown.
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(ResizingSceneHost, key: "root"));
            var el = _host.Root.Q<VisualElement>("sv");
            var shown = s_camera.targetTexture;
            Assume.That(shown, Is.Not.Null, "Precondition: the camera targets the texture");
            var foreign = new RenderTexture(16, 16, 0);
            _spawned.Add(foreign);
            s_camera.targetTexture = foreign;

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — the background still shows the framework texture holding the last frame.
            Assert.That(el.resolvedStyle.backgroundImage.renderTexture, Is.SameAs(shown));
        }

        [Test]
        public void Given_ACameraAlreadyTargetingAUserTexture_When_Mounted_Then_TheExplicitPassClaimsIt()
        {
            // Arrange — passing a camera to V.SceneView is explicit intent: a target the user set
            // BEFORE mounting must not stop the mount-time claim (only borrowing that happens after
            // the claim is respected by layout-driven resyncs).
            var cam = CreateCamera("cam");
            var pre = new RenderTexture(16, 16, 0);
            _spawned.Add(pre);
            cam.targetTexture = pre;

            // Act
            MountAndLayout(V.SceneView(cam, className: "w-[128px] h-[64px]", name: "sv"));

            // Assert — the camera renders into the framework texture the element shows.
            var el = _host.Root.Q<VisualElement>("sv");
            Assert.That(cam.targetTexture, Is.SameAs(el.resolvedStyle.backgroundImage.renderTexture));
        }

        #endregion

        #region Background ownership

        private static Texture2D s_posterA;
        private static Texture2D s_posterB;

        [Component]
        private static VNode GradientSwapHost()
        {
            var (alt, setAlt) = Hooks.UseState(false);
            s_setFlag = setAlt;
            return V.SceneView(s_camera, key: "sv", name: "sv", className: alt
                ? "w-[128px] h-[64px] bg-gradient-to-r from-green-500 to-yellow-500"
                : "w-[128px] h-[64px] bg-gradient-to-r from-red-500 to-blue-500");
        }

        [Test]
        public void Given_ALiveCameraFeed_When_TheGradientClassChanges_Then_TheFeedIsNotClobbered()
        {
            // Arrange — a gradient class and a live camera compete for the one backgroundImage slot;
            // while the camera texture is live it owns the slot, and class-driven writers defer.
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(GradientSwapHost, key: "root"));
            var el = _host.Root.Q<VisualElement>("sv");
            Assume.That(el, Is.Not.Null, "Precondition: the element mounted");
            Assume.That(s_camera.targetTexture, Is.Not.Null, "Precondition: the camera targets the texture");

            // Act — an ordinary state-driven restyle changes the gradient spec.
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — the background still samples the live camera texture.
            Assert.That(el.resolvedStyle.backgroundImage.renderTexture, Is.SameAs(s_camera.targetTexture));
        }

        [Component]
        private static VNode PosterSwapHost()
        {
            var (alt, setAlt) = Hooks.UseState(false);
            s_setFlag = setAlt;
            return V.SceneView(null, key: "sv", name: "sv", className: "w-[128px] h-[64px]",
                styles: new StyleOverrides
                {
                    BackgroundImage = new StyleBackground(alt ? s_posterB : s_posterA),
                });
        }

        [Test]
        public void Given_ACameraLessSceneView_When_ThePosterStyleChanges_Then_TheNewPosterShows()
        {
            // Arrange — no camera ever arrives, so styles.BackgroundImage owns the slot outright; the
            // mere existence of a (textureless) binding must not freeze the poster forever.
            s_posterA = new Texture2D(2, 2);
            _spawned.Add(s_posterA);
            s_posterB = new Texture2D(2, 2);
            _spawned.Add(s_posterB);
            MountAndLayout(V.Component(PosterSwapHost, key: "root"));
            var el = _host.Root.Q<VisualElement>("sv");
            Assume.That(el?.resolvedStyle.backgroundImage.texture, Is.SameAs(s_posterA),
                "Precondition: the first poster shows");

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That(el.resolvedStyle.backgroundImage.texture, Is.SameAs(s_posterB));
        }

        [Component]
        private static VNode ReleasingGradientHost()
        {
            var (released, setReleased) = Hooks.UseState(false);
            s_setFlag = setReleased;
            return V.SceneView(released ? null : s_camera, key: "sv", name: "sv",
                className: "w-[128px] h-[64px] bg-gradient-to-r from-red-500 to-blue-500");
        }

        [Test]
        public void Given_ALiveFeedOverAGradient_When_TheCameraIsRemoved_Then_TheGradientIsRestored()
        {
            // Arrange
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(ReleasingGradientHost, key: "root"));
            var el = _host.Root.Q<VisualElement>("sv");
            Assume.That(el?.resolvedStyle.backgroundImage.renderTexture, Is.Not.Null,
                "Precondition: the camera feed owns the background");

            // Act — removing the camera releases the texture; the deferred class background returns.
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — a baked gradient texture, not a blank slot, fills the element again.
            Assert.That(el.resolvedStyle.backgroundImage.texture, Is.Not.Null);
        }

        #endregion
    }
}
