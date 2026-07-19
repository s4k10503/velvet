using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the bezier-driven variant enter/exit (<c>StyleTransitionConfig.Type == Bezier</c>): the from→to class
    /// swap lands at rest IMMEDIATELY (no CSS-transition-triggering frame boundary), the per-frame tick
    /// (<see cref="BezierTweenDriver.Step"/>) moves the inline style along the exact cubic-bezier curve and
    /// reports done once elapsed reaches the fixed duration, and an exit-cancel retarget
    /// (<see cref="BezierTweenDriver.Retarget"/>) restarts a fresh full-duration reversal from the current value.
    /// </summary>
    /// <remarks>
    /// Panel-free by design, exactly like <see cref="MotionSpringDriverTests"/>: the scheduler's bezier path never
    /// reads <c>resolvedStyle</c> (numeric from/to values come from <see cref="MotionSpringClassParser"/>'s
    /// class-name parsing), and the recurring tick it registers needs a live panel clock the EditMode PlayerLoop
    /// never drives — so the tick's own math is exercised by calling <see cref="BezierTweenDriver.Step"/> directly
    /// in a loop. GWT, one assert per case (Assume for preconditions).
    /// </remarks>
    [TestFixture]
    internal sealed class BezierTweenDriverTests
    {
        private const float FixedDeltaSec = 1f / 60f;

        // Tailwind's own default curve, cubic-bezier(0.4,0,0.2,1).
        private const float X1 = 0.4f;
        private const float Y1 = 0f;
        private const float X2 = 0.2f;
        private const float Y2 = 1f;

        [Test]
        public void Given_ABezierVariantEnter_When_Started_Then_ClassesLandAtRestWithInlineOpacityAtTheFromValue()
        {
            // Arrange — the element already carries the resting (to) class, matching PlayVariantEnter's
            // precondition (the factory / reconciler create it with variants[animate] applied before calling this).
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            element.AddToClassList("opacity-100");

            // Act — a bezier enter from "hidden" (opacity-0) to the already-applied "visible" (opacity-100).
            scheduler.PlayVariantEnter(element, fromClasses: new[] { "opacity-0" }, toClasses: new[] { "opacity-100" },
                durationSec: 0.3f, easing: EasingMode.EaseOut, delaySec: 0f,
                type: TransitionType.Bezier, bezierX1: X1, bezierY1: Y1, bezierX2: X2, bezierY2: Y2);

            // Assert — the class swap resolved immediately (opacity-100 present, opacity-0 never added), and the
            // inline style shows the FROM pose synchronously so the element does not flash at the resting value.
            Assert.That(
                (element.ClassListContains("opacity-100"), element.ClassListContains("opacity-0"), element.style.opacity.value),
                Is.EqualTo((true, false, 0f)));
        }

        [Test]
        public void Given_ABezierDrivenOpacityChannel_When_SteppedRepeatedly_Then_TheInlineOpacityMovesTowardTheTarget()
        {
            // Arrange — an opacity bezier starting at 0, heading to 1 over a full second (so neither sample below
            // has reached the end yet).
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, durationSec: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            BezierTweenDriver.ApplyCurrentValues(element, state!);
            Assume.That(element.style.opacity.value, Is.EqualTo(0f), "Precondition: starts at the from-value");

            // Act — a handful of early ticks, then many more later ticks.
            for (var i = 0; i < 5; i++)
            {
                BezierTweenDriver.Step(element, state!, FixedDeltaSec);
            }
            var earlyOpacity = element.style.opacity.value;
            for (var i = 0; i < 40; i++)
            {
                BezierTweenDriver.Step(element, state!, FixedDeltaSec);
            }
            var laterOpacity = element.style.opacity.value;

            // Assert — opacity has moved further toward the target (1) as more ticks run.
            Assert.That(laterOpacity, Is.GreaterThan(earlyOpacity));
        }

        [Test]
        public void Given_ABezierDrivenOpacityChannel_When_SteppedUntilElapsedReachesDuration_Then_TheOpacityRestsExactlyAtTheTarget()
        {
            // Arrange
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, durationSec: 0.3f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            BezierTweenDriver.ApplyCurrentValues(element, state!);

            // Act — step until elapsed reaches the fixed duration (the cap only guards against a regression that
            // never completes).
            var completed = false;
            for (var i = 0; i < 600 && !completed; i++)
            {
                completed = BezierTweenDriver.Step(element, state!, FixedDeltaSec);
            }
            Assume.That(completed, Is.True, "Precondition: the tween completed within the tick budget");

            // Assert — the t>=1 early-return is exact, so the value rests EXACTLY at the target (tighter than a
            // spring's convergence-based settle).
            Assert.That(element.style.opacity.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ASettledBezierChannel_When_InlineOverridesCleared_Then_TheOpacityStyleIsRemoved()
        {
            // Arrange — run the tween to completion first (Assume guards the precondition).
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, durationSec: 0.3f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            BezierTweenDriver.ApplyCurrentValues(element, state!);
            var completed = false;
            for (var i = 0; i < 600 && !completed; i++)
            {
                completed = BezierTweenDriver.Step(element, state!, FixedDeltaSec);
            }
            Assume.That(completed, Is.True, "Precondition: the tween completed within the tick budget");

            // Act — the scheduler calls this on completion so the resting classes' own opacity takes back over.
            BezierTweenDriver.ClearInlineOverrides(element, state!);

            // Assert
            Assert.That(element.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ABezierChannelMidExit_When_Retargeted_Then_ItHeadsBackTowardTheValueItStartedFrom()
        {
            // Arrange — an exit-shaped channel: starts at the resting value (1, opaque) and heads to the exit
            // value (0). A few ticks in, it is partway there but not yet done.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, durationSec: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            BezierTweenDriver.ApplyCurrentValues(element, state!);
            for (var i = 0; i < 5; i++)
            {
                BezierTweenDriver.Step(element, state!, FixedDeltaSec);
            }
            Assume.That(state!.Opacity!.To, Is.EqualTo(0f), "Precondition: heading toward the exit value");

            // Act — an exit-cancel (the key re-entered mid-exit): retarget back toward the resting value.
            BezierTweenDriver.Retarget(state!);

            // Assert — the channel's goal flipped to the value it originally started from (its RestingTarget).
            Assert.That(state!.Opacity!.To, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ABezierChannelMidExit_When_Retargeted_Then_ElapsedResetsToZero()
        {
            // Arrange — pins the "fresh full-duration reversal" design decision explicitly (no spring analog):
            // a retarget restarts the clock rather than replaying time in reverse.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, durationSec: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            BezierTweenDriver.ApplyCurrentValues(element, state!);
            for (var i = 0; i < 5; i++)
            {
                BezierTweenDriver.Step(element, state!, FixedDeltaSec);
            }
            Assume.That(state!.ElapsedSec, Is.GreaterThan(0f), "Precondition: the forward tween has advanced");

            // Act
            BezierTweenDriver.Retarget(state!);

            // Assert — the reversal starts from a clean clock.
            Assert.That(state!.ElapsedSec, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AnOvershootBezierCurve_When_SteppedPartway_Then_TheInlineValueExceedsTheTargetMomentarily()
        {
            // Arrange — an overshoot curve (y1 past 1) driving opacity 0→1. This is the regression pin that
            // would catch someone substituting Mathf.Lerp for Mathf.LerpUnclamped: a clamp would silently flatten
            // the overshoot and this assert would go RED.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, 0.34f, 1.56f, 0.64f, 1f, durationSec: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");

            // Act — advance to 60% of the duration, where the overshoot curve is already past its target.
            BezierTweenDriver.Step(element, state!, 0.6f);

            // Assert — the inline value momentarily exceeds the target (1), i.e. it actually overshoots.
            Assert.That(element.style.opacity.value, Is.GreaterThan(1f));
        }
    }
}
