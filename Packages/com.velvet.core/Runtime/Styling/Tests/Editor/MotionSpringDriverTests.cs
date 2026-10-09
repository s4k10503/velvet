using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the spring-driven variant enter/exit (<c>StyleTransitionConfig.Type == Spring</c>) end to end: the
    /// from→to class swap lands at rest IMMEDIATELY (no CSS-transition-triggering frame boundary the tween path
    /// needs), the per-frame physics tick (<see cref="MotionSpringDriver.Step"/>) moves the inline style toward
    /// the target and reports settled once it arrives, an exit-cancel retarget
    /// (<see cref="MotionSpringDriver.Retarget"/>) redirects a channel toward its resting value without resetting
    /// its integrator; the axis-scope holes <c>Documentation~/motion.md</c>'s "Driven channels" documents
    /// (percentage-based translate, per-axis <c>scale-x-</c>/<c>scale-y-</c>), which resolve as utilities but
    /// plan no channel; the underlying pure physics of <see cref="SpringIntegrator"/> — it converges to its target
    /// given enough time, an underdamped configuration overshoots before settling (Framer Motion's spring is
    /// underdamped by default), and retargeting an in-flight spring carries its CURRENT value/velocity forward
    /// instead of resetting them (the continuity an interrupted AnimatePresence exit/enter needs); validation of
    /// user-supplied spring parameters, mirroring the tween path's duration guard, since a spring that can never
    /// satisfy its settle predicate (zero/negative stiffness never approaches the target; NaN diverges into the
    /// styles it writes) must warn and complete immediately instead of scheduling a forever tick whose completion
    /// callback — on a presence exit, the only thing that removes the ghost — never fires; and
    /// <see cref="MotionLayoutIdDriver.ComputeDelta"/>'s pure old-rect/new-rect math behind
    /// V.Motion's layoutId (Framer's shared-element layout animation parity).
    /// </summary>
    /// <remarks>
    /// Panel-free by design: <c>StyleAnimationScheduler</c>'s spring path never reads <c>resolvedStyle</c> (the
    /// numeric from/to values come from <see cref="MotionSpringClassParser"/>'s class-name parsing, not a style
    /// resolution pass), and the recurring tick this scheduler registers (<c>schedule.Execute(...).Every(16)</c>)
    /// needs a live panel clock to FIRE automatically, which the EditMode batchmode PlayerLoop never drives. So
    /// the scheduler's synchronous setup is asserted directly (no tick needed to observe it), and the
    /// recurring tick's own math — along with
    /// the standalone integrator and the layoutId delta math, neither of which involves a panel or VisualElement
    /// at all — is exercised by calling the driver directly in a loop instead of trying to pump a real/simulated
    /// scheduler clock. GWT, one assert per case: every fact a case depends on — channel recognition,
    /// settle/convergence, or a captured intermediate reading — folds into that single assertion via a
    /// nullable/NaN/tuple sentinel, so a regression in any of them turns the case red instead of skipping
    /// it (see <see cref="MotionPropertyChannelTests"/> for the pattern this fixture follows).
    /// </remarks>
    [TestFixture]
    internal sealed class MotionSpringDriverTests
    {
        private const float FixedDeltaSec = 1f / 60f;

        [Test]
        public void Given_ASpringVariantEnter_When_Started_Then_ClassesLandAtRestWithInlineOpacityAtTheFromValue()
        {
            // Arrange — the element already carries the resting (to) class, matching PlayVariantEnter's
            // precondition (FiberNodeFactory / GeneralPathReconciler create it with variants[animate] applied
            // before ever calling this).
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            element.AddToClassList("opacity-100");

            // Act — a spring enter from "hidden" (opacity-0) to the already-applied "visible" (opacity-100).
            scheduler.PlayVariantEnter(element, fromClasses: new[] { "opacity-0" }, toClasses: new[] { "opacity-100" },
                durationSec: 0.3f, easing: EasingMode.EaseOut, delaySec: 0f,
                type: TransitionType.Spring, stiffness: 100f, damping: 20f, mass: 1f);

            // Assert — the class swap resolved immediately (opacity-100 present, opacity-0 never added), and the
            // inline style shows the FROM pose synchronously so the element does not flash at the (already-
            // applied) resting classes' value before the first tick runs.
            Assert.That(
                (element.ClassListContains("opacity-100"), element.ClassListContains("opacity-0"), element.style.opacity.value),
                Is.EqualTo((true, false, 0f)));
        }

        [Test]
        public void Given_ASpringDrivenOpacityChannel_When_SteppedRepeatedly_Then_TheInlineOpacityMovesTowardTheTarget()
        {
            // Arrange — a critically damped opacity spring (no overshoot, so progress toward the target is
            // strictly monotonic) starting at 0, heading to 1.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 20f, mass: 1f);
            if (state != null)
            {
                MotionSpringDriver.ApplyCurrentValues(element, state);
            }

            // Act — a handful of early ticks, then many more later ticks. The sentinel readings survive when
            // no channel resolved, so both stay equal and fail below instead of the case being skipped.
            var earlyOpacity = -1f;
            var laterOpacity = -1f;
            if (state != null)
            {
                for (var i = 0; i < 5; i++)
                {
                    MotionSpringDriver.Step(element, state, FixedDeltaSec);
                }
                earlyOpacity = element.style.opacity.value;
                for (var i = 0; i < 40; i++)
                {
                    MotionSpringDriver.Step(element, state, FixedDeltaSec);
                }
                laterOpacity = element.style.opacity.value;
            }

            // Assert — opacity has moved further toward the target (1) as more ticks run.
            Assert.That(laterOpacity, Is.GreaterThan(earlyOpacity));
        }

        [Test]
        public void Given_ASpringDrivenOpacityChannel_When_SteppedUntilSettled_Then_TheOpacityRestsAtTheTarget()
        {
            // Arrange
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 20f, mass: 1f);
            if (state != null)
            {
                MotionSpringDriver.ApplyCurrentValues(element, state);
            }

            // Act — step until settled (a critically damped spring at this stiffness settles well inside this
            // budget; the cap just guards against an infinite loop if a regression stops it from ever settling).
            // NaN stands in for either failure mode (no channel resolved, or a spring that never converged),
            // so both fail the tolerance comparison rather than skipping the case.
            var restingOpacity = float.NaN;
            if (state != null)
            {
                var settled = false;
                for (var i = 0; i < 600 && !settled; i++)
                {
                    settled = MotionSpringDriver.Step(element, state, FixedDeltaSec);
                }
                if (settled)
                {
                    restingOpacity = element.style.opacity.value;
                }
            }

            // Assert
            Assert.That(restingOpacity, Is.EqualTo(1f).Within(0.01f));
        }

        [Test]
        public void Given_ASettledSpringChannel_When_InlineOverridesCleared_Then_TheOpacityStyleIsRemoved()
        {
            // Arrange — settle the spring first.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 20f, mass: 1f);
            var settled = false;
            if (state != null)
            {
                MotionSpringDriver.ApplyCurrentValues(element, state);
                for (var i = 0; i < 600 && !settled; i++)
                {
                    settled = MotionSpringDriver.Step(element, state, FixedDeltaSec);
                }
            }
            // A style that was never driven while settling is trivially "removed", so the assertion has to
            // see that it was held first.
            var heldWhileSettled = settled && element.style.opacity.keyword != StyleKeyword.Null;

            // Act — the scheduler calls this once every channel has settled, so the (already-applied) resting
            // classes' own opacity takes back over instead of the driver's inline value.
            if (state != null)
            {
                MotionSpringDriver.ClearInlineOverrides(element, state);
            }

            // Assert
            Assert.That((heldWhileSettled, element.style.opacity.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AUniformScaleClassPair_When_Resolved_Then_TheScaleChannelMatchesTheAxisScalePreset()
        {
            // Arrange / Act — scale-50 -> scale-100 mirrors the uniform (non-per-axis) .scale-N USS classes;
            // the parser reads this magnitude from StyleArbitraryValueResolver's own per-axis scale table
            // (shared, not a second hand-copied dictionary), so this pins that the shared table still resolves
            // the same 0.5 / 1.0 pair.
            var plan = MotionSpringClassParser.Resolve(new[] { "scale-50" }, new[] { "scale-100" });

            // Assert — a pair that resolved no scale channel reads as null here and fails the same
            // comparison a wrong magnitude would.
            Assert.That(plan.Scale, Is.EqualTo(((float, float)?)(0.5f, 1f)));
        }

        [Test]
        public void Given_ANegativeRotateClassPair_When_Resolved_Then_TheRotateChannelMatchesTheSharedMagnitudeTable()
        {
            // Arrange / Act — rotate-45 -> rotate-n45 mirrors the static .rotate-45 / .rotate-n45 USS classes;
            // the parser reads the magnitude from StyleArbitraryValueResolver's own rotate-preset table
            // (negating it itself for the "n"-suffixed spelling), the single source of truth this pins against
            // rather than a separately hand-expanded ± table that could drift out of sync.
            var plan = MotionSpringClassParser.Resolve(new[] { "rotate-45" }, new[] { "rotate-n45" });

            // Assert — a pair that resolved no rotate channel reads as null here and fails the same
            // comparison a wrong magnitude would.
            Assert.That(plan.Rotate, Is.EqualTo(((float, float)?)(45f, -45f)));
        }

        [Test]
        public void Given_APercentTranslateClassPair_When_Resolved_Then_NoTranslateChannelIsPlanned()
        {
            // Arrange — the gate carries the fact that these two spellings ARE real translate utilities,
            // resolving onto TranslateX in percent. Without it the case would pass just as well on a class
            // nothing recognizes, pinning the scope hole in name only.
            var resolvesAsPercentTranslate =
                StyleArbitraryValueResolver.TryParse("translate-x-1/2", out var half)
                && half.Property == ArbitraryProperty.TranslateX && half.Unit == LengthUnit.Percent;

            // Act
            var plan = MotionSpringClassParser.Resolve(new[] { "translate-x-1/2" }, new[] { "translate-x-full" });

            // Assert
            Assert.That((resolvesAsPercentTranslate, plan.TranslateX.HasValue), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APerAxisScaleClassPair_When_Resolved_Then_NoScaleChannelIsPlanned()
        {
            // Arrange — same gate shape as the percent-translate case above.
            var resolvesAsPerAxisScale =
                StyleArbitraryValueResolver.TryParse("scale-x-50", out var half)
                && half.Property == ArbitraryProperty.ScaleX;

            // Act
            var plan = MotionSpringClassParser.Resolve(new[] { "scale-x-50" }, new[] { "scale-x-110" });

            // Assert
            Assert.That((resolvesAsPerAxisScale, plan.Scale.HasValue), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ASpringChannelMidExit_When_Retargeted_Then_ItHeadsBackTowardTheValueItStartedFrom()
        {
            // Arrange — an exit-shaped channel: starts at the resting value (1, opaque) and heads to the exit
            // value (0). A few ticks in, it is partway there but not yet settled.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 20f, mass: 1f);
            var targetMidExit = float.NaN;
            if (state != null)
            {
                MotionSpringDriver.ApplyCurrentValues(element, state);
                for (var i = 0; i < 5; i++)
                {
                    MotionSpringDriver.Step(element, state, FixedDeltaSec);
                }
                targetMidExit = state.Opacity!.Target;
            }

            // Act — an exit-cancel (the key re-entered mid-exit): retarget back toward the resting value.
            // NaN stands in for a plan that resolved no channel, failing the comparison below the same way a
            // wrong target would.
            var targetAfterRetarget = float.NaN;
            if (state != null)
            {
                MotionSpringDriver.Retarget(state);
                targetAfterRetarget = state.Opacity!.Target;
            }

            // Assert — the channel was genuinely heading toward the exit value (0) before the retarget, and
            // its goal flipped to the value it originally started from (its RestingTarget, 1) afterward —
            // not a fresh 0/1 default or the exit value it was still short of.
            Assert.That((targetMidExit, targetAfterRetarget), Is.EqualTo((0f, 1f)));
        }

        [Test]
        public void Given_ASpringThatDoesNotRepeat_When_ItsPassIsAboutToEndAndThenEnds_Then_ItSettlesOnlyAtTheEnd()
        {
            // Arrange — stiffness 200, damping 13: Framer Motion's opacity spring first rests on its 1 000 ms
            // sample, which is when its animation finishes.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = MotionSpringDriver.Create(plan, stiffness: 200f, damping: 13f, mass: 1f);

            // Act — a frame at a time to 0.99 s, then the frame across 1 s. A plan that resolved no channel leaves
            // (true, false).
            var (beforeEnd, atEnd) = (true, false);
            if (state != null)
            {
                beforeEnd = false;
                for (var i = 0; i < 99 && !beforeEnd; i++)
                {
                    beforeEnd = MotionSpringDriver.Step(element, state, 0.01f);
                }
                atEnd = MotionSpringDriver.Step(element, state, 0.02f);
            }

            // Assert
            Assert.That((beforeEnd, atEnd), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ASpringRetargetedNearItsStart_When_ItSettles_Then_ItRestsWithinFramersThresholdForTheShortTravel()
        {
            // Arrange — a 16 px exit from 16 px, cancelled 0.05 s in, under 2 px from where it began: the reversal's
            // travel is under 5, so Framer Motion's spring rests it within 0.005 rather than the 0.5 of the exit's own
            // travel, or of the 30 px a sum of the two positions would read.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "translate-x-4" }, new[] { "translate-x-0" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 10f, mass: 1f);

            // Act — NaN for a plan that resolved no translate channel, or a reversal that never settles.
            var offset = float.NaN;
            if (state?.TranslateX != null)
            {
                for (var i = 0; i < 3; i++)
                {
                    MotionSpringDriver.Step(element, state, FixedDeltaSec);
                }
                MotionSpringDriver.Retarget(state);
                var settled = false;
                for (var i = 0; i < 1200 && !settled; i++)
                {
                    settled = MotionSpringDriver.Step(element, state, FixedDeltaSec);
                }
                if (settled)
                {
                    offset = Mathf.Abs(state.TranslateX.Integrator.Value - state.TranslateX.RestingTarget);
                }
            }

            // Assert
            Assert.That(offset, Is.LessThanOrEqualTo(0.005f));
        }

        [Test]
        public void Given_ACriticallyDampedSpring_When_SteppedForEnoughTime_Then_ItSettlesAtTheTarget()
        {
            // Arrange — stiffness 100 / mass 1 critically damps at damping = 2*sqrt(stiffness*mass) = 20 (no
            // ringing, the fastest non-oscillating approach).
            var spring = new SpringIntegrator(initialValue: 0f);
            const float target = 100f;

            // Act — 180 ticks (3 simulated seconds) is comfortably past this spring's settle time.
            for (var i = 0; i < 180; i++)
            {
                spring.Step(FixedDeltaSec, target, stiffness: 100f, damping: 20f, mass: 1f);
            }

            // Assert
            Assert.That(spring.Value, Is.EqualTo(target).Within(0.5f));
        }

        // Each row is a spring released 1 from its target at 2 per second, and where it is 0.1s later: an
        // underdamped, a critically damped and an overdamped spring, the reference values integrated
        // numerically (RK4, 200000 steps) rather than from the closed form under test.
        [TestCase(100.0, 10.0, 1.0, 0.766401592, -5.082686035)]
        [TestCase(100.0, 20.0, 1.0, 0.809334771, -3.678794412)]
        [TestCase(100.0, 125.0, 1.0, 0.943613190, -0.759773369)]
        public void Given_ASpringReleasedWithAVelocity_When_SolvedATenthOfASecondLater_Then_ItMatchesTheIntegratedMotion(
            double stiffness, double damping, double mass, double displacement, double velocity)
        {
            // Arrange
            var spring = (stiffness, damping, mass);

            // Act
            var solved = SpringIntegrator.Solve(1.0, 2.0, 0.1, spring);

            // Assert
            Assert.That(new[] { solved.Displacement, solved.Velocity },
                Is.EqualTo(new[] { displacement, velocity }).Within(1e-6));
        }

        [Test]
        public void Given_AHeavilyOverdampedOpacitySpring_When_SteppedForTwentySeconds_Then_ItSettles()
        {
            // Arrange — damping 125 at mass 1 puts damping * dt / mass past 2 at this tick; a semi-implicit
            // Euler step over the same 1200 ticks reaches -3e46.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 125f, mass: 1f);
            var settled = false;

            // Act
            for (var i = 0; i < 1200 && !settled; i++)
            {
                settled = MotionSpringDriver.Step(element, state, FixedDeltaSec);
            }

            // Assert
            Assert.That(settled, Is.True);
        }

        [Test]
        public void Given_AnUnderdampedSpring_When_SteppedTowardATarget_Then_ItOvershootsBeforeSettling()
        {
            // Arrange — damping (2) sits well below critical (20 for this stiffness/mass), so the spring rings
            // past its target before settling instead of approaching it monotonically.
            var spring = new SpringIntegrator(initialValue: 0f);
            const float target = 100f;
            var peakValue = float.NegativeInfinity;

            // Act — step long enough to pass through and beyond the target at least once.
            for (var i = 0; i < 180; i++)
            {
                spring.Step(FixedDeltaSec, target, stiffness: 100f, damping: 2f, mass: 1f);
                if (spring.Value > peakValue)
                {
                    peakValue = spring.Value;
                }
            }

            // Assert
            Assert.That(peakValue, Is.GreaterThan(target));
        }

        [Test]
        public void Given_ASpringMidFlightTowardOneTarget_When_RetargetedToADifferentValue_Then_TheNextStepContinuesFromItsCurrentValueAndVelocity()
        {
            // Arrange — run partway toward 100 so the spring has accumulated a nonzero value/velocity, then
            // capture that state right before retargeting.
            var spring = new SpringIntegrator(initialValue: 0f);
            for (var i = 0; i < 10; i++)
            {
                spring.Step(FixedDeltaSec, 100f, stiffness: 100f, damping: 10f, mass: 1f);
            }
            var capturedValue = spring.Value;
            var capturedVelocity = spring.Velocity;

            // Act — retarget to a wildly different value (mirrors an exit-cancel reversing back toward the
            // resting value) and take one more step; a FRESH spring seeded with the exact captured state is the
            // reference for what one step from "here" should produce with nothing reset.
            spring.Step(FixedDeltaSec, -50f, stiffness: 100f, damping: 10f, mass: 1f);
            var reference = new SpringIntegrator(capturedValue, capturedVelocity);
            reference.Step(FixedDeltaSec, -50f, stiffness: 100f, damping: 10f, mass: 1f);
            var valuesMatch = Mathf.Abs(spring.Value - reference.Value) <= 1e-6f;

            // Assert — the spring had genuinely built up velocity before the retarget (otherwise "continues
            // from its current value/velocity" would be indistinguishable from a reset to zero), and the
            // retargeted spring's value matches the reference exactly: the retarget carried the SAME
            // value/velocity forward (no discontinuity) rather than resetting either.
            Assert.That((capturedVelocity != 0f, valuesMatch), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AZeroStiffnessSpring_When_AVariantEnterPlays_Then_ItWarnsAndCompletesImmediately()
        {
            // Arrange
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            var completed = false;
            LogAssert.Expect(LogType.Warning, new Regex("[Ss]pring"));

            // Act — stiffness 0 can never move the value toward its target.
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                0f, EasingMode.Linear, 0f, onComplete: () => completed = true,
                additionalDelaySec: 0f, propertyOverrides: null,
                type: TransitionType.Spring, stiffness: 0f, damping: 10f, mass: 1f);

            // Assert — the play degrades to an immediate completion instead of a forever-tick.
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_ANaNSpringParameter_When_AVariantEnterPlays_Then_ItWarnsAndCompletesImmediately()
        {
            // Arrange
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            var completed = false;
            LogAssert.Expect(LogType.Warning, new Regex("[Ss]pring"));

            // Act — NaN stiffness would propagate NaN into every style write.
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                0f, EasingMode.Linear, 0f, onComplete: () => completed = true,
                additionalDelaySec: 0f, propertyOverrides: null,
                type: TransitionType.Spring, stiffness: float.NaN, damping: 10f, mass: 1f);

            // Assert
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_TwoIdenticalRects_When_DeltaComputed_Then_ThePlanIsEmpty()
        {
            // Arrange
            var rect = new Rect(10f, 20f, 100f, 50f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(rect, rect, new Vector2(50f, 25f));

            // Assert
            Assert.That(delta.IsEmpty, Is.True);
        }

        [Test]
        public void Given_AnOldRectOfInfiniteSize_When_DeltaComputed_Then_ThePlanIsEmpty()
        {
            // Arrange — the size a box takes into a parent drawn at zero scale.
            var oldRect = new Rect(0f, 0f, float.PositiveInfinity, float.PositiveInfinity);
            var newRect = new Rect(0f, 0f, 100f, 100f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, new Vector2(50f, 50f));

            // Assert
            Assert.That(delta.IsEmpty, Is.True);
        }

        [Test]
        public void Given_ANewRectNeverLaidOut_When_DeltaComputed_Then_ThePlanIsEmpty()
        {
            // Arrange
            var oldRect = new Rect(110f, 220f, 100f, 50f);
            var newRect = new Rect(0f, 0f, float.NaN, float.NaN);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, Vector2.zero);

            // Assert
            Assert.That(delta.IsEmpty, Is.True);
        }

        [Test]
        public void Given_ARectMovedAlongXByExactlyThePixelTolerance_When_DeltaComputed_Then_ThePlanIsEmpty()
        {
            // Arrange
            var oldRect = new Rect(MotionLayoutIdDriver.PixelTolerance, 0f, 64f, 64f);
            var newRect = new Rect(0f, 0f, 64f, 64f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, new Vector2(32f, 32f));

            // Assert
            Assert.That(delta.IsEmpty, Is.True);
        }

        [Test]
        public void Given_ARectMovedAlongYByExactlyThePixelTolerance_When_DeltaComputed_Then_ThePlanIsEmpty()
        {
            // Arrange
            var oldRect = new Rect(0f, MotionLayoutIdDriver.PixelTolerance, 64f, 64f);
            var newRect = new Rect(0f, 0f, 64f, 64f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, new Vector2(32f, 32f));

            // Assert
            Assert.That(delta.IsEmpty, Is.True);
        }

        [Test]
        public void Given_ARectWhoseWidthChangesByExactlyThePixelTolerance_When_DeltaComputed_Then_ThePlanIsEmpty()
        {
            // Arrange
            var oldRect = new Rect(0f, 0f, 64f + MotionLayoutIdDriver.PixelTolerance, 64f);
            var newRect = new Rect(0f, 0f, 64f, 64f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, Vector2.zero);

            // Assert
            Assert.That(delta.IsEmpty, Is.True);
        }

        [Test]
        public void Given_ARectWhoseHeightChangesByExactlyThePixelTolerance_When_DeltaComputed_Then_ThePlanIsEmpty()
        {
            // Arrange
            var oldRect = new Rect(0f, 0f, 64f, 64f + MotionLayoutIdDriver.PixelTolerance);
            var newRect = new Rect(0f, 0f, 64f, 64f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, Vector2.zero);

            // Assert
            Assert.That(delta.IsEmpty, Is.True);
        }

        [Test]
        public void Given_ANewRectAHundredthOfAPixelWide_When_DeltaComputed_Then_ItsWidthIsNotScaled()
        {
            // Arrange
            var oldRect = new Rect(0f, 0f, 1f, 64f);
            var newRect = new Rect(0f, 0f, 0.01f, 64f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, Vector2.zero);

            // Assert
            Assert.That(delta.Scale.x, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ANewRectAHundredthOfAPixelTall_When_DeltaComputed_Then_ItsHeightIsNotScaled()
        {
            // Arrange
            var oldRect = new Rect(0f, 0f, 64f, 1f);
            var newRect = new Rect(0f, 0f, 64f, 0.01f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, Vector2.zero);

            // Assert
            Assert.That(delta.Scale.y, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ARectMovedWithoutResizing_When_DeltaComputed_Then_OnlyTheTranslateIsSet()
        {
            // Arrange — moved from (10,20) to (110,220), same 100x50 size.
            var oldRect = new Rect(10f, 20f, 100f, 50f);
            var newRect = new Rect(110f, 220f, 100f, 50f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, new Vector2(50f, 25f));

            // Assert — the OLD-minus-NEW offset (the inverse offset to apply immediately, animating back toward
            // 0), and no change of scale.
            Assert.That((delta.Translate, delta.Scale), Is.EqualTo((new Vector2(-100f, -200f), Vector2.one)));
        }

        [Test]
        public void Given_ARectResizedAboutItsCentre_When_DeltaComputedWithACentreOrigin_Then_OnlyTheScaleIsSet()
        {
            // Arrange — grew from 100x100 to 200x200 about the same centre (100,100).
            var oldRect = new Rect(50f, 50f, 100f, 100f);
            var newRect = new Rect(0f, 0f, 200f, 200f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, new Vector2(100f, 100f));

            // Assert — oldSize/newSize on each axis: the element is now twice as big and starts at half.
            Assert.That((delta.Translate, delta.Scale), Is.EqualTo((Vector2.zero, new Vector2(0.5f, 0.5f))));
        }

        [Test]
        public void Given_ARectResizedWithItsCornerFixed_When_DeltaComputedWithACentreOrigin_Then_TheTranslateHoldsTheOldCentre()
        {
            // Arrange — grew from 100x100 to 200x200 with the top-left corner at (0,0) throughout.
            var oldRect = new Rect(0f, 0f, 100f, 100f);
            var newRect = new Rect(0f, 0f, 200f, 200f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, new Vector2(100f, 100f));

            // Assert — the old centre (50,50) less the new one (100,100).
            Assert.That(delta.Translate, Is.EqualTo(new Vector2(-50f, -50f)));
        }

        [Test]
        public void Given_ARectResizedWithItsCornerFixed_When_DeltaComputedWithThatCornerAsOrigin_Then_OnlyTheScaleIsSet()
        {
            // Arrange — the same growth, scaled about the fixed corner.
            var oldRect = new Rect(0f, 0f, 100f, 100f);
            var newRect = new Rect(0f, 0f, 200f, 200f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, Vector2.zero);

            // Assert
            Assert.That((delta.Translate, delta.Scale), Is.EqualTo((Vector2.zero, new Vector2(0.5f, 0.5f))));
        }

        [Test]
        public void Given_ANonUniformResize_When_DeltaComputed_Then_EachAxisScalesByItsOwnFactor()
        {
            // Arrange — width unchanged, height doubled, about the centre: the old centre sits 50px higher.
            var oldRect = new Rect(0f, 0f, 100f, 100f);
            var newRect = new Rect(0f, 0f, 100f, 200f);

            // Act
            var delta = MotionLayoutIdDriver.ComputeDelta(oldRect, newRect, new Vector2(50f, 100f));

            // Assert
            Assert.That((delta.Translate, delta.Scale), Is.EqualTo((new Vector2(0f, -50f), new Vector2(1f, 0.5f))));
        }
    }
}
