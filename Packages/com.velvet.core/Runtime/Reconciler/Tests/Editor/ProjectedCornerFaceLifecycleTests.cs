using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins how a layoutId Motion drawn scaled differently on each axis hands its face back: as it is torn down with a
    /// ring beside it, as its radius stops being one in pixels part way through the move, and as the move lands under
    /// a transition of its background.
    /// </summary>
    internal sealed class ProjectedCornerFaceLifecycleTests : MotionSimulatedPanelTestsBase
    {
        private static readonly StyleTransitionConfig s_layoutSpring =
            new() { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f };

        private static StateUpdater<int> s_setStep;
        private static string s_classes;
        private static string s_movedClasses;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_setStep = default;
            s_classes = null;
            s_movedClasses = null;
        }

        // Step 0 lays the box out at 100×100, step 1 at 400×200, step 2 at 400×200 with the moved classes, and step 3
        // removes it.
        private static VNode BoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            if (step == 3) return V.Div();
            var size = step == 0 ? "w-[100px] h-[100px]" : "w-[400px] h-[200px]";
            var classes = step == 2 ? s_movedClasses : s_classes;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "box", layoutId: "box", transition: s_layoutSpring,
                    className: $"left-[0px] top-[0px] {size} {classes}"),
            });
        }

        private MountedTree MountAndWiden()
        {
            VelvetStyleUtilities.AttachTo(Root);
            var mounted = V.Mount(Root, V.Component(BoxRender, key: "root"));
            Tick();
            Step(mounted, 1);
            return mounted;
        }

        private void Step(MountedTree mounted, int step)
        {
            s_setStep.Invoke(step);
            mounted.FlushStateForTest();
            Tick();
        }

        private static int FaceCallbacks(VisualElement element) => element.generateVisualContent == null ? 0
            : element.generateVisualContent.GetInvocationList()
                .Count(callback => callback.Method.DeclaringType?.FullName?.Contains("ProjectedCornerFace") == true);

        // GREEN_ON_BASE(characterization): the base paints no face of its own on a moving Motion or on its ring.
        // Dropping `ProjectedCornerFace.Forget` from MotionLayoutIdDriver.CancelForTeardown reddens this case.
        [Test]
        public void Given_ARingedLayoutIdMotionDrawnScaledDifferentlyOnEachAxis_When_ItIsTornDown_Then_NeitherItNorItsBandKeepsAFaceCallback()
        {
            // Arrange — ringed, drawn at a quarter across and a half down, for the frames its band follows it on.
            s_classes = "rounded-[20px] bg-blue-500 ring-2 ring-[#ff0000]";
            using var mounted = MountAndWiden();
            for (var i = 0; i < 3; i++) Tick();
            var box = Root.Q<VisualElement>("box");
            var band = box.parent.Q<VisualElement>(RingOverlay.OverlayName);

            // Act
            Step(mounted, 3);

            // Assert
            Assert.That(FaceCallbacks(box) + FaceCallbacks(band), Is.Zero);
        }

        [Test]
        public void Given_AnInsetRingedLayoutIdMotionDrawnScaledDifferentlyOnEachAxis_When_ItsBandIsRepainted_Then_ItsCornersAreTheElementsOwn()
        {
            // Arrange — drawn at a quarter across and a half down, its band inside it.
            s_classes = "rounded-[20px] bg-blue-500 ring-2 ring-inset ring-[#ff0000]";
            using var mounted = MountAndWiden();
            for (var i = 0; i < 3; i++) Tick();
            var box = Root.Q<VisualElement>("box");
            var binding = RingOverlay.TryGet(box);
            var own = ProjectedCornerFace.TryGetRadii(box, out var radii)
                ? BorderBandPainter.Fitted(radii, box.layout.width, box.layout.height).TopLeft.x
                : float.NaN;

            // Act — the radii the inset band is painted with.
            var drawn = binding is { Inside: true } ? binding.InsideRadii.TopLeft.x : float.PositiveInfinity;

            // Assert
            Assert.That(drawn, Is.EqualTo(own).Within(1e-3f));
        }

        [Test]
        public void Given_AShadowedLayoutIdMotionDrawnScaledDifferentlyOnEachAxis_When_ItsFaceIsRepainted_Then_ItsCornersTakeTheProjectedRadii()
        {
            // Arrange — shadowed, drawn at a quarter across and a half down.
            s_classes = "rounded-[20px] bg-blue-500 shadow-md";
            using var mounted = MountAndWiden();
            Tick();
            var box = Root.Q<VisualElement>("box");
            var projected = ProjectedCornerFace.TryGetRadii(box, out var radii) && radii.TopLeft.x != radii.TopLeft.y
                ? radii.TopLeft
                : new Vector2(float.NaN, float.NaN);

            // Act — the corners the shadow layer repaints the face with.
            var drawn = ShearedRoundedRect.ForElement(box, 0f, box.layout.width, box.layout.height, 0f, 0f).Radii.TopLeft;

            // Assert — NaN where the projection draws no uneven corner, which no reading matches.
            Assert.That(drawn, Is.EqualTo(projected));
        }

        private static readonly System.Reflection.MethodInfo s_shadowBox = typeof(BoxShadowSilhouette)
            .GetMethod("BoxOf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        [Test]
        public void Given_AShadowedRoundedLayoutIdMotionDrawnScaledDifferentlyOnEachAxis_When_ItsMoveLands_Then_ItsShadowCornersAreRoundAgain()
        {
            // Arrange — moving, drawn at a quarter across and a half down, with its radius slots held at none.
            s_classes = "rounded-[20px] bg-blue-500 shadow-md";
            using var mounted = MountAndWiden();
            Tick();
            var box = Root.Q<VisualElement>("box");
            var moved = ProjectedCornerFace.TryGetRadii(box, out _);

            // Act — until the move hands its face back.
            for (var i = 0; i < 600 && ProjectedCornerFace.TryGetRadii(box, out _); i++) Tick();
            Tick();
            var binding = BoxShadowSilhouette.TryGet(box)!;
            var shadow = (ShadowBox)s_shadowBox.Invoke(null, new object[] { box, binding, box.layout.width, box.layout.height });

            // Assert — NaN where the move never held the corners per axis.
            Assert.That(moved ? shadow.Radii.x : float.NaN, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void Given_ADashedLayoutIdMotionDrawnScaledDifferentlyOnEachAxis_When_ItsOutlineIsLaidOut_Then_ItsCornersTakeTheProjectedRadii()
        {
            // Arrange — dashed, drawn at a quarter across and a half down.
            s_classes = "rounded-[20px] bg-blue-500 border-4 border-dashed";
            using var mounted = MountAndWiden();
            Tick();
            var box = Root.Q<VisualElement>("box");
            var projected = ProjectedCornerFace.TryGetRadii(box, out var radii) && radii.TopLeft.x != radii.TopLeft.y
                ? radii.TopLeft
                : new Vector2(float.NaN, float.NaN);

            // Act
            var outline = BorderStyleSilhouette.BoxOf(box);

            // Assert — NaN where the projection draws no uneven corner, which no reading matches.
            Assert.That(outline?.Radii?.TopLeft, Is.EqualTo(projected));
        }

        // GREEN_ON_BASE(characterization): the base never writes the background-color slot of a moving Motion.
        // Dropping `ProjectedCornerFace.End` from LayoutIdLook.WriteRadii's path for a radius one length a corner draws
        // reddens this case.
        [Test]
        public void Given_ALayoutIdMotionDrawnScaledDifferentlyOnEachAxis_When_ItsRadiusTurnsToAPercentMidMove_Then_ItsBackgroundSlotIsHandedBack()
        {
            // Arrange — blue, drawn at a quarter across and a half down.
            s_classes = "rounded-[20px] bg-blue-500";
            s_movedClasses = "rounded-[10%] bg-blue-500";
            using var mounted = MountAndWiden();
            Tick();

            // Act — a percent radius, which scales with the box and is left to UI Toolkit.
            Step(mounted, 2);
            Tick();

            // Assert
            Assert.That(Root.Q<VisualElement>("box").style.backgroundColor.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base never hides the background of a moving Motion, so it is blue throughout.
        // Dropping the `NarrowIfIntercepted` call from ProjectedCornerFace.Begin, or releasing at once in
        // ProjectedCornerFace.ReleaseOnceRestored, reddens this case: the background then fades in from transparent.
        [Test]
        public void Given_ALayoutIdMotionTransitioningItsColoursDrawnScaledDifferentlyOnEachAxis_When_ItsFaceIsHandedBack_Then_ItsBackgroundDoesNotFadeIn()
        {
            // Arrange — blue, transitioning its colours over a second, drawn at a quarter across and a half down.
            s_classes = "rounded-[20px] bg-blue-500 transition-colors duration-1000";
            using var mounted = MountAndWiden();
            var box = Root.Q<VisualElement>("box");

            // Act — until the background slot is the element's own again, and two frames on.
            for (var i = 0; i < 600 && box.style.backgroundColor.keyword != StyleKeyword.Null; i++) Tick();
            Tick();
            Tick();

            // Assert — opaque, where a transition from the hidden slot would still be fading it in.
            Assert.That(box.resolvedStyle.backgroundColor.a, Is.EqualTo(1f).Within(0.01f));
        }
    }
}
