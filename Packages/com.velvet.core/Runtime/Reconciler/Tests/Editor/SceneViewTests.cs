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
    /// framework creates a RenderTexture sized to the element's content box in pixels (times the
    /// resolution scale), assigns it to <c>camera.targetTexture</c>, and shows it through a child laid
    /// over that content box, above the element's own background, with each corner rounded along the
    /// content edge.</item>
    /// <item>The texture follows the element: a geometry change re-sizes it (rounding the larger axis
    /// up to a shared quantization step and rescaling the other axis by the same factor, so its
    /// aspect ratio keeps matching the element's, so a minor resize reuses the existing texture
    /// instead of reallocating), a camera swap releases the old camera and targets the new one, and
    /// removing the camera (or the element, or disposing the tree) releases both the camera's target
    /// and the texture.</item>
    /// <item>Release is polite: a camera whose targetTexture was reassigned by user code after mount is
    /// left untouched on unmount.</item>
    /// <item>A null camera mounts an inert element; a zero-sized element creates no texture.</item>
    /// <item>Several elements can show one camera: each one the camera does not target is rendered into
    /// by its own tick, and one targeting nothing is claimed back. A positive-priority UseFrame callback
    /// in the panel takes rendering over, holding the camera disabled until it is gone.</item>
    /// </list>
    /// The EditMode panel comes from <see cref="HeadlessEditorPanelHost"/> and layout is driven
    /// explicitly (the scheduler does not tick in EditMode).
    /// </summary>
    internal sealed class SceneViewTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private MountedTree _portalTree;
        private readonly List<Object> _spawned = new();

        private static StateUpdater<bool> s_setFlag;
        private static StateUpdater<int> s_setStep;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
        }

        [TearDown]
        public void TearDown()
        {
            _portalTree?.Dispose();
            _portalTree = null;
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

        // Null where the element shows no camera output.
        private static VisualElement Feed(VisualElement element) => element.Q(className: "velvet-scene-view-feed");

        // Read through resolvedStyle: this editor's INLINE backgroundImage getter reconstructs the value
        // without a RenderTexture case (a stored RT reads back as Null), while the computed style
        // round-trips it.
        private static RenderTexture FeedTexture(VisualElement element) => Feed(element)?.resolvedStyle.backgroundImage.renderTexture;

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
        public void Given_AMountedSceneView_When_LayoutResolves_Then_TheFeedCarriesTheSameTexture()
        {
            // Arrange
            var cam = CreateCamera("cam");

            // Act
            MountAndLayout(V.SceneView(cam, className: "w-[128px] h-[64px]", name: "sv"));

            // Assert — the element displays the exact texture the camera renders into.
            var el = _host.Root.Q<VisualElement>("sv");
            Assert.That(FeedTexture(el), Is.Not.Null.And.SameAs(cam.targetTexture));
        }

        [Test]
        public void Given_ALiveFeedOverAPoster_When_LayoutResolves_Then_TheElementKeepsThePoster()
        {
            // Arrange — as a canvas's CSS background stays beneath its bitmap.
            var cam = CreateCamera("cam");
            var poster = new Texture2D(4, 4);
            _spawned.Add(poster);

            // Act
            MountAndLayout(V.SceneView(cam, className: "w-[128px] h-[64px]", name: "sv",
                styles: new StyleOverrides { BackgroundImage = new StyleBackground(poster) }));

            // Assert — read only while the camera holds a texture, so a feed that never went live reads null.
            var el = _host.Root.Q<VisualElement>("sv");
            Assert.That(cam.targetTexture != null ? el.resolvedStyle.backgroundImage.texture : null, Is.SameAs(poster));
        }

        [Test]
        public void Given_ABorderedPaddedSceneView_When_LayoutResolves_Then_TheTextureMatchesTheContentBox()
        {
            // Arrange — a 2px border and 2px padding on each side leave a 128x64 content box.
            var cam = CreateCamera("cam");

            // Act
            MountAndLayout(V.SceneView(cam, className: "w-[136px] h-[72px] border-[2px] p-[2px]"));

            // Assert
            Assert.That((cam.targetTexture.width, cam.targetTexture.height), Is.EqualTo((128, 64)));
        }

        [Test]
        public void Given_ABorderedPaddedSceneView_When_LayoutResolves_Then_TheFeedCoversTheContentBox()
        {
            // Arrange — uneven sides, so a feed placed from the border box, or from the wrong side, lands
            // elsewhere. SceneViewDriver.SyncFeed relies on this for absolute offsets being taken from the
            // padding box.
            var cam = CreateCamera("cam");
            MountAndLayout(V.SceneView(cam, name: "sv",
                className: "w-[128px] h-[64px] border-[2px] pl-[1px] pr-[7px] pt-[13px] pb-[4px]"));
            var el = _host.Root.Q<VisualElement>("sv");

            // Act — the feed joins during the first layout pass and is laid out by the next.
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That(Feed(el)?.layout ?? Rect.zero, Is.EqualTo(el.contentRect));
        }

        // Top-left first and clockwise; -1 each where there is no feed.
        private (float, float, float, float) FeedRadii(string className)
        {
            MountAndLayout(V.SceneView(CreateCamera("cam"), name: "sv", className: className));
            var feed = Feed(_host.Root.Q<VisualElement>("sv"));
            return feed == null
                ? (-1f, -1f, -1f, -1f)
                : (feed.resolvedStyle.borderTopLeftRadius, feed.resolvedStyle.borderTopRightRadius,
                    feed.resolvedStyle.borderBottomRightRadius, feed.resolvedStyle.borderBottomLeftRadius);
        }

        [Test]
        public void Given_ARoundedSceneViewDeeperAtItsSides_When_LayoutResolves_Then_EachFeedCornerLosesItsSidesInset()
        {
            // Arrange — insets of 10 left, 8 right, 3 top and 4 bottom beside a 20px radius.
            const string className = "w-[128px] h-[64px] rounded-[20px] border-[2px] pl-[8px] pr-[6px] pt-[1px] pb-[2px]";

            // Act
            var radii = FeedRadii(className);

            // Assert
            Assert.That(radii, Is.EqualTo((10f, 12f, 12f, 10f)));
        }

        [Test]
        public void Given_ARoundedSceneViewDeeperAtTopAndBottom_When_LayoutResolves_Then_EachFeedCornerLosesThatInset()
        {
            // Arrange — insets of 3 left, 4 right, 10 top and 8 bottom beside a 20px radius.
            const string className = "w-[128px] h-[64px] rounded-[20px] border-[2px] pl-[1px] pr-[2px] pt-[8px] pb-[6px]";

            // Act
            var radii = FeedRadii(className);

            // Assert
            Assert.That(radii, Is.EqualTo((10f, 10f, 12f, 12f)));
        }

        [Component]
        private static VNode ShiftingPaddingHost()
        {
            var (shifted, setShifted) = Hooks.UseState(false);
            s_setFlag = setShifted;
            return V.SceneView(s_camera, key: "sv", name: "sv", className: shifted
                ? "w-[128px] h-[64px] pl-[5px] pr-[3px]"
                : "w-[128px] h-[64px] pl-[1px] pr-[7px]");
        }

        [Test]
        public void Given_APaddingShiftThatKeepsTheContentSize_When_TheTickFires_Then_TheFeedFollowsTheContentBox()
        {
            // Arrange — the content box moves 4px right at the same size, which re-derives no texture.
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(ShiftingPaddingHost, key: "root"));
            var el = _host.Root.Q<VisualElement>("sv");
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That(Feed(el)?.layout ?? Rect.zero, Is.EqualTo(el.contentRect));
        }

        [Test]
        public void Given_ALiveSceneView_When_APortalRendersRowsIntoIt_Then_FirstLandsOnTheFirstRow()
        {
            // Arrange — the feed is a child the framework put there, which takes no place in the rows' order.
            var cam = CreateCamera("cam");
            MountAndLayout(V.SceneView(cam, name: "sv", className: "w-[128px] h-[64px]"));
            var el = _host.Root.Q<VisualElement>("sv");

            // Act
            _portalTree = V.Mount(new VisualElement(), V.Portal(el, children: new VNode[]
            {
                V.Div(name: "row0", className: "first:w-[10px]"),
                V.Div(name: "row1", className: "first:w-[10px]"),
            }));

            // Assert — the feed is read too, since without one the rows are the only children anyway.
            Assert.That((Feed(el) != null, el.Q<VisualElement>("row0")?.style.width.value.value ?? -1f),
                Is.EqualTo((true, 10f)));
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

            // Assert — the larger axis (200) rounds up to the next 16px quantization step (208); the
            // unchanged 64px axis is rescaled by the same factor (208/200) to keep the texture's
            // aspect equal to the element's, landing at round(208 * 64 / 200) = 67.
            Assert.That((s_camera.targetTexture.width, s_camera.targetTexture.height), Is.EqualTo((208, 67)));
        }

        [Test]
        public void Given_AQuantizedResize_When_TheTextureIsCreated_Then_TheAspectMatchesTheElement()
        {
            // Arrange — SceneViewDriver never pins camera.aspect, so Unity derives the camera's
            // render aspect straight from the texture's own width/height; quantizing width and
            // height independently would drift the texture's aspect from the element's true 200:64
            // aspect and visibly skew the rendered scene.
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(ResizingHost, key: "root"));

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — allows the rounding slack RoundToInt introduces when rescaling the shorter axis
            // (a fraction of a pixel), while still failing hard against independent-axis quantization,
            // which would land at aspect 208/64 = 3.25 — an 0.125 gap, over 5x this tolerance.
            var rt = s_camera.targetTexture;
            var textureAspect = (float)rt.width / rt.height;
            var elementAspect = 200f / 64f;
            Assert.That(Mathf.Abs(textureAspect - elementAspect), Is.LessThan(0.05f));
        }

        [Component]
        private static VNode NearSquareJiggleHost()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var sizes = new[]
            {
                "w-[100px] h-[100px]",
                "w-[99px] h-[100px]",
                "w-[100px] h-[99px]",
            };
            return V.SceneView(s_camera, className: sizes[step]);
        }

        [Test]
        public void Given_ANearSquareElement_When_TheDominantAxisAlternates_Then_TheWidthBasisStaysStable()
        {
            // Arrange — a 1px jiggle around a 1:1 aspect ratio flips which axis is momentarily
            // longer (100x100 -> 99x100 -> 100x99). Without a sticky basis-axis, re-deriving "is
            // width >= height" from scratch each time would flip which axis gets quantized and
            // rescale the OTHER axis to match it, changing the width bucket on every step even
            // though the element barely moved.
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(NearSquareJiggleHost, key: "root"));
            var widthAtStep0 = s_camera.targetTexture.width;

            // Act
            s_setStep.Invoke(1);
            FlushAndLayout();
            var widthAtStep1 = s_camera.targetTexture.width;
            s_setStep.Invoke(2);
            FlushAndLayout();
            var widthAtStep2 = s_camera.targetTexture.width;

            // Assert
            Assert.That((widthAtStep0, widthAtStep1, widthAtStep2), Is.EqualTo((112, 112, 112)));
        }

        [Test]
        public void Given_ADiscardedDerivation_When_ItWouldFlipTheBasis_Then_TheBindingIsNotMutated()
        {
            // Arrange — mount width-basis (100x100). The geometry-changed callback is unregistered
            // BEFORE growing the live layout's height, so growing it does not itself trigger the
            // driver's normal SyncTexture (which would legitimately commit the flip) — this isolates
            // the one call this test cares about: TryComputePixelSize's own out parameter reports a
            // derivation that would flip to height-basis, and that call alone must not write the
            // flip into binding.WidthWasQuantizedAxis — only a caller that actually applies the
            // derived size (SyncTexture, on either its exact-match or fresh-texture path) may commit
            // it. This is what keeps a call whose result is discarded (SyncTexture's borrowed-camera
            // bail-out, OnRepaintTick's staleness probe) from silently steering the basis of whatever
            // texture a later confirmed call actually produces.
            s_camera = CreateCamera("cam");
            MountAndLayout(V.SceneView(s_camera, className: "w-[100px] h-[100px]", name: "sv"));
            var element = _host.Root.Q<VisualElement>("sv");
            var binding = _mounted.Root.Reconciler.Context.SceneViewBindings[element];
            Assume.That(binding.WidthWasQuantizedAxis, Is.EqualTo(true),
                "Precondition: width-basis committed by the initial mount");
            element.UnregisterCallback(binding.OnGeometryChanged);
            element.style.height = 200;
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Act
            var method = typeof(SceneViewDriver).GetMethod("TryComputePixelSize",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var args = new object[] { element, binding, 0, 0, false };
            method.Invoke(null, args);
            Assume.That(args[4], Is.EqualTo(false),
                "Precondition: this call's fresh derivation would flip to height-basis");

            // Assert
            Assert.That(binding.WidthWasQuantizedAxis, Is.EqualTo(true));
        }

        [Component]
        private static VNode SubStepResizeHost()
        {
            var (grown, setGrown) = Hooks.UseState(false);
            s_setFlag = setGrown;
            return V.SceneView(s_camera, className: grown ? "w-[103px] h-[66px]" : "w-[100px] h-[64px]");
        }

        [Test]
        public void Given_ASubStepSizeDelta_When_TheElementResizes_Then_TheRenderTextureIsNotReallocated()
        {
            // Arrange — 100x64 and 103x66 are nearly the same aspect ratio (1.5625 vs. 1.5606), so
            // both quantize their larger (width) axis to the same 112px bucket and rescale height to
            // the same 72px, landing on the identical texture size; this resize must reuse the
            // existing RenderTexture instead of reallocating a new one for a size delta smaller than
            // the step.
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
            // diff and the re-render must leave the camera output in place.
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
            Assert.That(FeedTexture(el), Is.SameAs(s_camera.targetTexture));
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

        // Draws nothing but its red clear color, so a render into the texture reads red.
        private Camera CreateRedCamera()
        {
            var cam = CreateCamera("cam");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.red;
            cam.cullingMask = 0;
            return cam;
        }

        private static void Fill(RenderTexture texture, Color color)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(true, true, color);
            RenderTexture.active = previous;
        }

        private static bool IsRed(RenderTexture texture) =>
            RenderTexturePixelReader.IsRedPixel(RenderTexturePixelReader.ReadPixels(texture, new RectInt(0, 0, 1, 1))[0]);

        [Test]
        public void Given_AnEnabledCameraOutsidePlayMode_When_TheTickFires_Then_TheCameraRendersIntoTheTexture()
        {
            // Arrange — the texture starts blue, so only a render of the red-clearing camera turns it red.
            TestGraphics.IgnoreIfHeadless("a camera render");
            var cam = CreateRedCamera();
            MountAndLayout(V.SceneView(cam, className: "w-[64px] h-[64px]"));
            Assume.That(cam.targetTexture, Is.Not.Null, "Precondition: the camera targets the texture");
            Fill(cam.targetTexture, Color.blue);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(IsRed(cam.targetTexture), Is.True);
        }

        [Test]
        public void Given_AResizedSceneViewOutsidePlayMode_When_TheTickFiresOnce_Then_TheCameraRendersOnce()
        {
            // Arrange — the resize re-derives the texture and re-syncs the tick, which must leave one
            // tick running rather than a second beside the first.
            TestGraphics.IgnoreIfHeadless("a camera render");
            s_camera = CreateRedCamera();
            MountAndLayout(V.Component(ResizingHost, key: "root"));
            s_setFlag.Invoke(true);
            FlushAndLayout();
            var renders = 0;
            void Count(UnityEngine.Rendering.ScriptableRenderContext _, Camera rendered)
            {
                if (rendered == s_camera) renders++;
            }
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += Count;

            // Act
            try
            {
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }
            finally
            {
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= Count;
            }

            // Assert
            Assert.That(renders, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): an editor panel's live SceneView repaints on each tick, as it did.
        [Test]
        public void Given_ALiveSceneViewOnAnEditorPanel_When_TheTickFires_Then_TheElementRepaints()
        {
            // Arrange — an editor panel regenerates only what is marked dirty, and nothing but the tick
            // marks this element once its texture is in place.
            var cam = CreateCamera("cam");
            MountAndLayout(V.SceneView(cam, className: "w-[64px] h-[64px]", name: "sv"));
            var element = _host.Root.Q<VisualElement>("sv");
            var repaints = 0;
            element.generateVisualContent += _ => repaints++;
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            repaints = 0;

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That(repaints, Is.GreaterThan(0));
        }

        // GREEN_ON_BASE(characterization): the tick leaves a disabled camera's texture holding what it held before.
        [Test]
        public void Given_ADisabledCameraOutsidePlayMode_When_TheTickFires_Then_TheCameraDoesNotRender()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("a camera render");
            var cam = CreateRedCamera();
            MountAndLayout(V.SceneView(cam, className: "w-[64px] h-[64px]"));
            Assume.That(cam.targetTexture, Is.Not.Null, "Precondition: the camera targets the texture");
            cam.enabled = false;
            Fill(cam.targetTexture, Color.blue);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(IsRed(cam.targetTexture), Is.False);
        }

        // GREEN_ON_BASE(characterization): a texture user code gave the camera is never rendered into by the tick.
        [Test]
        public void Given_AUserReassignedTargetOutsidePlayMode_When_TheTickFires_Then_TheCameraDoesNotRenderIntoIt()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("a camera render");
            var cam = CreateRedCamera();
            MountAndLayout(V.SceneView(cam, className: "w-[64px] h-[64px]"));
            Assume.That(cam.targetTexture, Is.Not.Null, "Precondition: the camera targets the texture");
            var userTexture = new RenderTexture(64, 64, 24);
            _spawned.Add(userTexture);
            cam.targetTexture = userTexture;
            Fill(userTexture, Color.blue);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(IsRed(userTexture), Is.False);
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
            // Arrange — user code borrowed the camera; a later RESIZE must not swap the feed to a
            // fresh texture nothing renders into, nor destroy the frame being shown.
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

            // Assert — the feed still shows the framework texture holding the last frame.
            Assert.That(FeedTexture(el), Is.SameAs(shown));
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
            Assert.That(cam.targetTexture, Is.SameAs(FeedTexture(el)));
        }

        #endregion

        #region The element's own background

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
            // Arrange — a gradient class beneath a live camera feed.
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(GradientSwapHost, key: "root"));
            var el = _host.Root.Q<VisualElement>("sv");
            Assume.That(el, Is.Not.Null, "Precondition: the element mounted");
            Assume.That(s_camera.targetTexture, Is.Not.Null, "Precondition: the camera targets the texture");

            // Act — an ordinary state-driven restyle changes the gradient spec.
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — the feed still samples the live camera texture.
            Assert.That(FeedTexture(el), Is.SameAs(s_camera.targetTexture));
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
        private static VNode ReleasingHost()
        {
            var (released, setReleased) = Hooks.UseState(false);
            s_setFlag = setReleased;
            return V.SceneView(released ? null : s_camera, key: "sv", name: "sv", className: "w-[128px] h-[64px]");
        }

        [Test]
        public void Given_ALiveFeed_When_TheCameraIsRemoved_Then_TheFeedLeavesTheElement()
        {
            // Arrange
            s_camera = CreateCamera("cam");
            MountAndLayout(V.Component(ReleasingHost, key: "root"));
            var el = _host.Root.Q<VisualElement>("sv");
            var fedWhileLive = Feed(el) != null;

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert — the feed is checked while the camera is live too, since a base with none passes
            // the removal alone.
            Assert.That((fedWhileLive, Feed(el) != null), Is.EqualTo((true, false)));
        }

        #endregion

        #region One camera across several SceneViews

        [Component]
        private static VNode SharedCameraHost()
        {
            var (second, setSecond) = Hooks.UseState(false);
            s_setFlag = setSecond;
            return V.Div(className: "flex-col", children: new VNode[]
            {
                V.SceneView(s_camera, key: "a", name: "a", className: "w-[64px] h-[64px]"),
                second ? V.SceneView(s_camera, key: "b", name: "b", className: "w-[64px] h-[64px]") : null,
            });
        }

        // Mounts "a", then adds "b", whose explicit pass claims the shared camera last.
        private void MountSharedCamera()
        {
            s_camera = CreateRedCamera();
            MountAndLayout(V.Component(SharedCameraHost, key: "root"));
            s_setFlag.Invoke(true);
            FlushAndLayout();
        }

        private RenderTexture TextureOf(string name) =>
            _mounted.Root.Reconciler.Context.SceneViewBindings[_host.Root.Q<VisualElement>(name)].Texture;

        [Test]
        public void Given_TwoSceneViewsSharingACamera_When_TheTickFires_Then_TheOneItDoesNotTargetIsRenderedInto()
        {
            // Arrange — the texture of the element the camera does not target starts blue, so only a render
            // turns it red.
            TestGraphics.IgnoreIfHeadless("a camera render");
            MountSharedCamera();
            var first = s_camera.targetTexture == TextureOf("b") ? TextureOf("a") : TextureOf("b");
            Fill(first, Color.blue);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(IsRed(first), Is.True);
        }

        // GREEN_ON_BASE(characterization): the element that claimed the shared camera last keeps it, as before.
        [Test]
        public void Given_TwoSceneViewsSharingACamera_When_TheTickFires_Then_TheCameraStillTargetsTheLaterOne()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("a camera render");
            MountSharedCamera();

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(s_camera.targetTexture, Is.SameAs(TextureOf("b")));
        }

        [Test]
        public void Given_TwoSceneViewsSharingACamera_When_TheHolderUnmounts_Then_TheOtherClaimsTheCamera()
        {
            // Arrange — "a" sits first in a column, so removing "b" leaves its geometry, and with it the
            // geometry-driven resync, alone.
            MountSharedCamera();
            var first = TextureOf("a");
            s_setFlag.Invoke(false);
            FlushAndLayout();

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(s_camera.targetTexture, Is.SameAs(first));
        }

        #endregion

        #region Rendering taken over by a positive-priority UseFrame

        private static int s_framePriority;

        [Component]
        private static VNode FrameTaker()
        {
            Hooks.UseFrame(_ => { }, s_framePriority);
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode TakeoverHost()
        {
            var (taking, setTaking) = Hooks.UseState(true);
            s_setFlag = setTaking;
            return V.Div(children: new VNode[]
            {
                V.SceneView(s_camera, key: "sv", name: "sv", className: "w-[64px] h-[64px]"),
                taking ? V.Component(FrameTaker, key: "taker") : null,
            });
        }

        [Component]
        private static VNode TakeoverSwapHost()
        {
            var (swapped, setSwapped) = Hooks.UseState(false);
            s_setFlag = setSwapped;
            return V.Div(children: new VNode[]
            {
                V.SceneView(swapped ? s_cameraB : s_camera, key: "sv", name: "sv", className: "w-[64px] h-[64px]"),
                V.Component(FrameTaker, key: "taker"),
            });
        }

        [Component]
        private static VNode TakeoverUnmountHost()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setFlag = setShown;
            return V.Div(children: new VNode[]
            {
                shown ? V.SceneView(s_camera, key: "sv", name: "sv", className: "w-[64px] h-[64px]") : null,
                V.Component(FrameTaker, key: "taker"),
            });
        }

        private void MountTakeover(int priority) => MountTakeover(priority, TakeoverHost);

        private void MountTakeover(int priority, System.Func<VNode> host)
        {
            s_framePriority = priority;
            s_camera = CreateCamera("cam");
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            MountAndLayout(V.Component(host, key: "root"));
            _mounted.FlushEffectsForTest();
        }

        // Flips the host's flag, lets the effects it unmounts or mounts run, and fires the tick past its interval.
        private void FlipAndTick(bool value)
        {
            s_setFlag.Invoke(value);
            FlushAndLayout();
            _mounted.FlushEffectsForTest();
            UseFrameFakeClockHost.Ms += 100;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        [Test]
        public void Given_APositivePriorityFrameCallbackInThePanel_When_TheTickFires_Then_TheCameraIsHeldDisabled()
        {
            // Arrange
            MountTakeover(1);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(s_camera.enabled, Is.False);
        }

        // GREEN_ON_BASE(characterization): a zero priority orders callbacks and leaves the camera alone, as before.
        [Test]
        public void Given_AZeroPriorityFrameCallbackInThePanel_When_TheTickFires_Then_TheCameraStaysEnabled()
        {
            // Arrange
            MountTakeover(0);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(s_camera.enabled, Is.True);
        }

        [Test]
        public void Given_ATakeoverThatEnds_When_TheTickFires_Then_TheCameraIsEnabledAgain()
        {
            // Arrange
            MountTakeover(1);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            var enabledWhileTaken = s_camera.enabled;

            // Act
            FlipAndTick(false);

            // Assert — read while taken over too, since a base that never disables it passes the end alone.
            Assert.That((enabledWhileTaken, s_camera.enabled), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ATakeoverThatEndedAndStartsAgain_When_TheTickFires_Then_TheCameraIsHeldDisabledAgain()
        {
            // Arrange
            MountTakeover(1);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            FlipAndTick(false);
            var enabledBetween = s_camera.enabled;

            // Act
            FlipAndTick(true);

            // Assert — read between the two too, since a base that never disables it is enabled there.
            Assert.That((enabledBetween, s_camera.enabled), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ATakenOverSceneView_When_ItsCameraIsSwapped_Then_TheOldCameraIsEnabledAgain()
        {
            // Arrange
            s_cameraB = CreateCamera("camB");
            MountTakeover(1, TakeoverSwapHost);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            var enabledWhileTaken = s_camera.enabled;

            // Act
            s_setFlag.Invoke(true);
            FlushAndLayout();

            // Assert
            Assert.That((enabledWhileTaken, s_camera.enabled), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ATakenOverSceneView_When_ItUnmounts_Then_ItsCameraIsEnabledAgain()
        {
            // Arrange
            MountTakeover(1, TakeoverUnmountHost);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            var enabledWhileTaken = s_camera.enabled;

            // Act
            s_setFlag.Invoke(false);
            FlushAndLayout();

            // Assert
            Assert.That((enabledWhileTaken, s_camera.enabled), Is.EqualTo((false, true)));
        }

        #endregion
    }
}
