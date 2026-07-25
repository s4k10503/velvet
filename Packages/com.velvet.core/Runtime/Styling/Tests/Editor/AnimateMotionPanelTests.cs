using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Applied-frame coverage for the animate-* motions on a real panel. The pan modes read the element's
    /// resolved box, so they need a laid-out <see cref="UnityEditor.EditorWindow"/> panel; Hue is geometry-free.
    /// The scheduler never ticks in EditMode, so each frame is driven explicitly via
    /// <see cref="StyleAnimateDriver.ApplyFrame"/> at a chosen phase — the same pure path the runtime tick
    /// calls. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class AnimateMotionPanelTests : PanelTestBase
    {
        // Mounts a single Div and returns (element, its animation binding or null). animate-* is wrapper-less,
        // so the element is the mounted root's first child.
        private (VisualElement element, StyleAnimateBinding binding) Mount(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: className, name: "card"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var element = _window.rootVisualElement[0];
            _mounted.Root.Reconciler.Context.AnimationBindings.TryGetValue(element, out var binding);
            return (element, binding);
        }

        [Test]
        public void Given_GradientPanAtMidLoop_When_FrameApplied_Then_BackgroundPannedByBoxWidth()
        {
            // bg-gradient-to-r flows horizontally → the 100px box pans X; the triangle peak at t=0.5 offsets
            // by one full box width (negative = leftward).
            var (element, binding) = Mount("w-[100px] h-[40px] bg-gradient-to-r from-red-500 to-blue-500 animate-gradient");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            Assert.That(element.style.backgroundPositionX.value.offset.value, Is.EqualTo(-100f));
        }

        [Test]
        public void Given_GradientPan_When_Attached_Then_BackgroundIsOversizedOnPanAxis()
        {
            // The Gradient pan oversizes the pan axis to 200% so the box window never reveals a transparent edge.
            var (element, _) = Mount("w-[100px] h-[40px] bg-gradient-to-r from-red-500 to-blue-500 animate-gradient");

            Assert.That(element.style.backgroundSize.value.x.value, Is.EqualTo(200f));
        }

        [Test]
        public void Given_VerticalGradientPanAtMidLoop_When_FrameApplied_Then_PansYByBoxHeight()
        {
            // bg-gradient-to-b flows vertically → the pan axis is Y; t=0.5 offsets by the 40px box height.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-gradient-to-b from-red-500 to-blue-500 animate-gradient");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            Assert.That(element.style.backgroundPositionY.value.offset.value, Is.EqualTo(-40f));
        }

        [Test]
        public void Given_Hue_When_FrameAppliedAtMidLoop_Then_HueRotateFilterAtHalfTurn()
        {
            // animate-hue cycles the hue-rotate filter; t=0.5 is a 180deg rotation.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-red-500 animate-hue");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);
            var f = element.style.filter.value[0];

            Assert.That((f.type, f.GetParameter(0).floatValue), Is.EqualTo((FilterFunctionType.HueRotate, 180f)));
        }

        [Test]
        public void Given_HueSecondFrame_When_FrameApplied_Then_AFreshFilterReferenceIsWritten()
        {
            // UI Toolkit dirties an element's filter for repaint only when the backing list REFERENCE changes
            // (it ref-compares, not content-compares). So a continuous hue MUST write a fresh list each frame;
            // reusing one mutated list would repaint frame 1 then freeze. Asserting the second frame's list is a
            // distinct reference pins that (RED if the driver reuses one list, GREEN with a fresh list per frame).
            var (element, binding) = Mount("w-[100px] h-[40px] bg-red-500 animate-hue");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.25f);
            var first = element.style.filter.value;
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);
            var second = element.style.filter.value;

            Assert.That(ReferenceEquals(first, second), Is.False);
        }

        [Test]
        public void Given_ShimmerAtLoopStart_When_FrameApplied_Then_BandSitsOffTheLeadingEdge()
        {
            // Shimmer sweeps one-way; t=0 the transparent-ended band sits one box width off the leading edge.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-slate-700 bg-gradient-to-r from-transparent via-white to-transparent animate-shimmer");
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            Assert.That(element.style.backgroundPositionX.value.offset.value, Is.EqualTo(-100f));
        }

        [Test]
        public void Given_StaticFilterAndHue_When_FrameApplied_Then_HueOwnsTheFilterSlot()
        {
            // Documented limitation: animate-hue OWNS style.filter while active — it does not compose with a
            // static filter-* on the same element. After a hue frame the slot holds only the hue rotation.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-red-500 grayscale-[.5] animate-hue");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            Assert.That(element.style.filter.value.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_HueWithNoGradient_When_Mounted_Then_BindingStillAttaches()
        {
            // Hue is not a pan, so it does not require a gradient — it attaches on any element.
            var (_, binding) = Mount("w-[100px] h-[40px] bg-red-500 animate-hue");

            Assert.That(binding, Is.Not.Null);
        }

        [Test]
        public void Given_PulseAtMidLoop_When_FrameApplied_Then_OpacityAtHalf()
        {
            // animate-pulse oscillates opacity; the trough at t=0.5 is the half-opacity minimum.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-red-500 animate-pulse");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);

            Assert.That(element.style.opacity.value, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Given_PulseSecondFrame_When_FrameApplied_Then_OpacityReflectsNewPhase()
        {
            // The applied path must re-derive opacity from the phase each frame (the value-compare analog of the
            // Hue fresh-reference test). Driving t=0.5 then t=0 must land at full opacity — a frozen / hardcoded
            // value in the Pulse arm of ApplyFrame would not track the second phase. RED if it ignores t.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-red-500 animate-pulse");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);
            StyleAnimateDriver.ApplyFrame(element, binding, 0f);

            Assert.That(element.style.opacity.value, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Given_RunningPulse_When_Detached_Then_InlineOpacityCleared()
        {
            // Detach drops the per-frame inline opacity so the element returns to its class-driven value.
            var (element, binding) = Mount("w-[100px] h-[40px] bg-red-500 animate-pulse");
            StyleAnimateDriver.ApplyFrame(element, binding, 0.5f);
            StyleAnimateDriver.Detach(element, binding);

            Assert.That(element.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_PanModeWithNoGradient_When_Mounted_Then_NoBinding()
        {
            // animate-gradient with nothing to pan is inert (parity with a lone gradient stop).
            var (_, binding) = Mount("w-[100px] h-[40px] animate-gradient");

            Assert.That(binding, Is.Null);
        }

        [Test]
        public void Given_RunningPan_When_Detached_Then_StretchToFillRestored()
        {
            // Detach restores the gradient's 100% stretch-to-fill (the gradient itself may still be bound).
            var (element, binding) = Mount("w-[100px] h-[40px] bg-gradient-to-r from-red-500 to-blue-500 animate-gradient");
            StyleAnimateDriver.Detach(element, binding);

            Assert.That(element.style.backgroundSize.value.x.value, Is.EqualTo(100f));
        }
    }

    /// <summary>
    /// Pure phase math for <see cref="StyleAnimateDriver"/> — the loop position, the per-mode background-position
    /// offset, the hue angle, and the pan-axis decision — plus parser coverage for <see cref="StyleAnimateClass"/>,
    /// which resolves the animate-* motion utilities into an <see cref="AnimateSpec"/>. Both are geometry-free and
    /// deterministic (the EditMode PlayerLoop never ticks the scheduler), so they are driven directly at explicit
    /// phases / class-name lists; the applied-style path is covered by the panel fixture above. GWT, one assert
    /// per case.
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

        private static AnimateSpec Extract(params string[] classNames)
        {
            Assume.That(StyleAnimateClass.TryExtract(classNames, out var spec), Is.True,
                "Precondition: the class list resolves to an animation");
            return spec;
        }

        // (token, expected mode) — one row per recognized animate-* motion. Verbatim from the per-mode tests.
        private static readonly TestCaseData[] ModeCases =
        {
            new TestCaseData("animate-gradient", AnimateMode.Gradient).SetName("Given_AnimateGradient_When_Extracted_Then_ModeIsGradient"),
            new TestCaseData("animate-shimmer", AnimateMode.Shimmer).SetName("Given_AnimateShimmer_When_Extracted_Then_ModeIsShimmer"),
            new TestCaseData("animate-hue", AnimateMode.Hue).SetName("Given_AnimateHue_When_Extracted_Then_ModeIsHue"),
            new TestCaseData("animate-pulse", AnimateMode.Pulse).SetName("Given_AnimatePulse_When_Extracted_Then_ModeIsPulse"),
        };

        [TestCaseSource(nameof(ModeCases))]
        public void Mode_FromToken(string token, object expected)
        {
            // Given the animate-* token / When extracted / Then the mode resolves.
            // expected is typed object because AnimateMode is internal (a public test method cannot take it directly).
            Assert.That(Extract(token).Mode, Is.EqualTo((AnimateMode)expected));
        }

        // (token, expected default seconds) — verbatim from the per-mode default-duration tests. animate-shimmer
        // is omitted: it carries a distinct default (no default-duration test existed for it), kept out of the table.
        private static readonly TestCaseData[] DefaultDurationCases =
        {
            new TestCaseData("animate-gradient", 3f).SetName("Given_AnimateGradient_When_Extracted_Then_UsesDefaultDuration"),
            new TestCaseData("animate-hue", 4f).SetName("Given_AnimateHue_When_Extracted_Then_UsesDefaultFourSeconds"),
            new TestCaseData("animate-pulse", 2f).SetName("Given_AnimatePulse_When_Extracted_Then_UsesDefaultTwoSeconds"),
        };

        [TestCaseSource(nameof(DefaultDurationCases))]
        public void DefaultDuration_FromToken(string token, float expectedSec)
        {
            // Given the animate-* token with no -[<time>] override / When extracted / Then the per-mode default applies.
            Assert.That(Extract(token).DurationSec, Is.EqualTo(expectedSec));
        }

        [Test]
        public void Given_DurationSuffixInSeconds_When_Extracted_Then_OverridesDefault()
        {
            Assert.That(Extract("animate-gradient-[2s]").DurationSec, Is.EqualTo(2f));
        }

        [Test]
        public void Given_DurationSuffixInMilliseconds_When_Extracted_Then_ResolvesToSeconds()
        {
            Assert.That(Extract("animate-hue-[500ms]").DurationSec, Is.EqualTo(0.5f));
        }

        [Test]
        public void Given_AnimateNone_When_Extracted_Then_NoAnimation()
        {
            // animate-none is the explicit cancel: recognized as a token but resolves to no animation.
            Assert.That(StyleAnimateClass.TryExtract(new[] { "animate-none" }, out _), Is.False);
        }

        [Test]
        public void Given_GradientThenNone_When_Extracted_Then_LaterNoneCancels()
        {
            // Cascade: the later animate-none wins over the earlier animate-gradient.
            Assert.That(StyleAnimateClass.TryExtract(new[] { "animate-gradient", "animate-none" }, out _), Is.False);
        }

        [Test]
        public void Given_GradientThenHue_When_Extracted_Then_LastModeWins()
        {
            Assert.That(Extract("animate-gradient", "animate-hue").Mode, Is.EqualTo(AnimateMode.Hue));
        }

        [Test]
        public void Given_UnknownAnimateToken_When_Extracted_Then_NotClaimed()
        {
            // animate-spin is not a Velvet motion (yet); the namespace stays open, so it is not claimed.
            Assert.That(StyleAnimateClass.TryExtract(new[] { "animate-spin" }, out _), Is.False);
        }

        [Test]
        public void Given_InvalidDurationSuffix_When_Extracted_Then_NotClaimed()
        {
            // animate-hue-[abc] has an unparseable time, so the whole token is rejected.
            Assert.That(StyleAnimateClass.TryExtract(new[] { "animate-hue-[abc]" }, out _), Is.False);
        }

        [Test]
        public void Given_PlainUtilities_When_Extracted_Then_NotAnimated()
        {
            Assert.That(StyleAnimateClass.TryExtract(new[] { "bg-red-500", "p-4" }, out _), Is.False);
        }

        [Test]
        public void Given_TwoSpecsSameModeAndDuration_When_Compared_Then_Equal()
        {
            Assert.That(new AnimateSpec(AnimateMode.Hue, 4f), Is.EqualTo(new AnimateSpec(AnimateMode.Hue, 4f)));
        }
    }
}
