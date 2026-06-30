using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pure phase math for <see cref="StyleAnimateDriver"/>: the loop position, the per-mode background-position
    /// offset, the hue angle, and the pan-axis decision. These are geometry-free and deterministic, so they are
    /// driven directly at explicit phases (the EditMode PlayerLoop never ticks the scheduler). The applied-style
    /// path is covered by the panel fixture. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleAnimateDriverTests
    {
        [Test]
        public void Given_StartOfLoop_When_PhaseComputed_Then_Zero()
        {
            Assert.That(StyleAnimateDriver.Phase(0d, 3f), Is.EqualTo(0f));
        }

        [Test]
        public void Given_HalfElapsed_When_PhaseComputed_Then_Half()
        {
            Assert.That(StyleAnimateDriver.Phase(1.5d, 3f), Is.EqualTo(0.5f));
        }

        [Test]
        public void Given_FullDurationElapsed_When_PhaseComputed_Then_WrapsToZero()
        {
            // The phase is time-derived modulo the duration, so a full loop wraps back to 0 (no drift).
            Assert.That(StyleAnimateDriver.Phase(3d, 3f), Is.EqualTo(0f));
        }

        [Test]
        public void Given_OneAndAHalfLoops_When_PhaseComputed_Then_Half()
        {
            Assert.That(StyleAnimateDriver.Phase(4.5d, 3f), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Given_GradientAtStart_When_PanOffset_Then_Zero()
        {
            // Triangle wave: t=0 sits at the un-panned end.
            Assert.That(StyleAnimateDriver.PanOffsetPx(AnimateMode.Gradient, 0f, 100f), Is.EqualTo(0f));
        }

        [Test]
        public void Given_GradientAtMidLoop_When_PanOffset_Then_FullBoxOffset()
        {
            // Triangle peak at t=0.5: panned by one full box extent (negative = leftward/upward).
            Assert.That(StyleAnimateDriver.PanOffsetPx(AnimateMode.Gradient, 0.5f, 100f), Is.EqualTo(-100f));
        }

        [Test]
        public void Given_GradientAtLoopEnd_When_PanOffset_Then_BackToZero()
        {
            Assert.That(StyleAnimateDriver.PanOffsetPx(AnimateMode.Gradient, 1f, 100f), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Given_ShimmerAtStart_When_PanOffset_Then_OffLeadingEdge()
        {
            // Sawtooth: t=0 the band sits one box extent off the leading edge.
            Assert.That(StyleAnimateDriver.PanOffsetPx(AnimateMode.Shimmer, 0f, 100f), Is.EqualTo(-100f));
        }

        [Test]
        public void Given_ShimmerAtMidLoop_When_PanOffset_Then_Centered()
        {
            Assert.That(StyleAnimateDriver.PanOffsetPx(AnimateMode.Shimmer, 0.5f, 100f), Is.EqualTo(0f));
        }

        [Test]
        public void Given_ShimmerAtLoopEnd_When_PanOffset_Then_OffTrailingEdge()
        {
            Assert.That(StyleAnimateDriver.PanOffsetPx(AnimateMode.Shimmer, 1f, 100f), Is.EqualTo(100f));
        }

        [Test]
        public void Given_MidLoop_When_HueAngle_Then_HalfRotation()
        {
            Assert.That(StyleAnimateDriver.HueAngleDeg(0.5f), Is.EqualTo(180f));
        }

        [Test]
        public void Given_PulseAtStart_When_OpacityComputed_Then_FullyOpaque()
        {
            // Cosine pulse: t=0 sits at the full-opacity peak.
            Assert.That(StyleAnimateDriver.PulseOpacity(0f), Is.EqualTo(1f));
        }

        [Test]
        public void Given_PulseAtMidLoop_When_OpacityComputed_Then_HalfOpacity()
        {
            // The trough at t=0.5 is the minimum (half opacity).
            Assert.That(StyleAnimateDriver.PulseOpacity(0.5f), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Given_PulseAtLoopEnd_When_OpacityComputed_Then_BackToFullyOpaque()
        {
            Assert.That(StyleAnimateDriver.PulseOpacity(1f), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Given_PulseAtEighthPhase_When_OpacityComputed_Then_FollowsCosineEaseNotLinearRamp()
        {
            // The keyframe vertices (t=0,0.5,1) and even t=0.25 coincide for a cosine ease and a linear triangle,
            // so they cannot pin the SHAPE. t=0.125 separates them: cosine = 0.75 + 0.25*cos(pi/4) ≈ 0.9268,
            // whereas a linear ramp would give 0.875. This locks the documented smooth ease (RED on a triangle).
            var expectedCosine = 0.75f + (0.25f * UnityEngine.Mathf.Cos(UnityEngine.Mathf.PI / 4f));
            Assert.That(StyleAnimateDriver.PulseOpacity(0.125f), Is.EqualTo(expectedCosine).Within(1e-4f));
        }

        [Test]
        public void Given_ToRightGradient_When_PanAxisResolved_Then_Horizontal()
        {
            // 90deg (to right) flows left-right, so the pan axis is horizontal (not vertical).
            Assert.That(StyleAnimateDriver.PanVerticalForAngle(90f), Is.False);
        }

        [Test]
        public void Given_ToBottomGradient_When_PanAxisResolved_Then_Vertical()
        {
            // 180deg (to bottom) flows up-down, so the pan axis is vertical.
            Assert.That(StyleAnimateDriver.PanVerticalForAngle(180f), Is.True);
        }

        [Test]
        public void Given_OffPanelAttach_When_Attached_Then_TickIsDeferredNotScheduled()
        {
            // A host (panel root) only exists once attached; attaching off-panel must defer the tick rather
            // than schedule on a detached element (whose scheduled items UI Toolkit would drop).
            var element = new VisualElement();
            var binding = StyleAnimateDriver.Attach(element, new AnimateSpec(AnimateMode.Hue, 4f), false);

            Assert.That(binding.Scheduled, Is.Null);
        }

        [Test]
        public void Given_OffPanelDeferredAttach_When_DetachedBeforeAttach_Then_PendingCallbackCleared()
        {
            // Detaching before the element ever attaches must unregister the deferred-attach callback so it
            // (and the binding it captures) does not linger on the element across pool reuse.
            var element = new VisualElement();
            var binding = StyleAnimateDriver.Attach(element, new AnimateSpec(AnimateMode.Hue, 4f), false);
            Assume.That(binding.PendingAttach, Is.Not.Null, "Precondition: an off-panel attach registers a deferred callback");

            StyleAnimateDriver.Detach(element, binding);

            Assert.That(binding.PendingAttach, Is.Null);
        }
    }
}
