using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the spring-driven variant enter/exit (<c>StyleTransitionConfig.Type == Spring</c>): the from→to class
    /// swap lands at rest IMMEDIATELY (no CSS-transition-triggering frame boundary the tween path needs), the
    /// per-frame physics tick (<see cref="MotionSpringDriver.Step"/>) moves the inline style toward the target
    /// and reports settled once it arrives, and an exit-cancel retarget (<see cref="MotionSpringDriver.Retarget"/>)
    /// redirects a channel toward its resting value without resetting its integrator.
    /// </summary>
    /// <remarks>
    /// Panel-free by design: <c>StyleAnimationScheduler</c>'s spring path never reads <c>resolvedStyle</c> (the
    /// numeric from/to values come from <see cref="MotionSpringClassParser"/>'s class-name parsing, not a style
    /// resolution pass), and the recurring tick this scheduler registers (<c>schedule.Execute(...).Every(16)</c>)
    /// needs a live panel clock to FIRE automatically, which the EditMode batchmode PlayerLoop never drives. So,
    /// mirroring <c>ShadowAnimationVisibilityTests</c>' own panel-free approach and the assignment's documented
    /// fallback: the scheduler's synchronous setup is asserted directly (no tick needed to observe it), and the
    /// recurring tick's own math is exercised by calling <see cref="MotionSpringDriver.Step"/> directly in a loop
    /// instead of trying to pump a real/simulated scheduler clock. GWT, one assert per case (Assume for
    /// preconditions).
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
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            MotionSpringDriver.ApplyCurrentValues(element, state!);
            Assume.That(element.style.opacity.value, Is.EqualTo(0f), "Precondition: starts at the from-value");

            // Act — a handful of early ticks, then many more later ticks.
            for (var i = 0; i < 5; i++)
            {
                MotionSpringDriver.Step(element, state!, FixedDeltaSec);
            }
            var earlyOpacity = element.style.opacity.value;
            for (var i = 0; i < 40; i++)
            {
                MotionSpringDriver.Step(element, state!, FixedDeltaSec);
            }
            var laterOpacity = element.style.opacity.value;

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
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            MotionSpringDriver.ApplyCurrentValues(element, state!);

            // Act — step until settled (a critically damped spring at this stiffness settles well inside this
            // budget; the cap just guards against an infinite loop if a regression stops it from ever settling).
            var settled = false;
            for (var i = 0; i < 600 && !settled; i++)
            {
                settled = MotionSpringDriver.Step(element, state!, FixedDeltaSec);
            }
            Assume.That(settled, Is.True, "Precondition: the spring settled within the tick budget");

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(1f).Within(0.01f));
        }

        [Test]
        public void Given_ASettledSpringChannel_When_InlineOverridesCleared_Then_TheOpacityStyleIsRemoved()
        {
            // Arrange — settle the spring first (Assume guards the precondition, mirroring the test above).
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 20f, mass: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            MotionSpringDriver.ApplyCurrentValues(element, state!);
            var settled = false;
            for (var i = 0; i < 600 && !settled; i++)
            {
                settled = MotionSpringDriver.Step(element, state!, FixedDeltaSec);
            }
            Assume.That(settled, Is.True, "Precondition: the spring settled within the tick budget");

            // Act — the scheduler calls this once every channel has settled, so the (already-applied) resting
            // classes' own opacity takes back over instead of the driver's inline value.
            MotionSpringDriver.ClearInlineOverrides(element, state!);

            // Assert
            Assert.That(element.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AUniformScaleClassPair_When_Resolved_Then_TheScaleChannelMatchesTheAxisScalePreset()
        {
            // Arrange / Act — scale-50 -> scale-100 mirrors the uniform (non-per-axis) .scale-N USS classes;
            // the parser reads this magnitude from StyleArbitraryValueResolver's own per-axis scale table
            // (shared, not a second hand-copied dictionary), so this pins that the shared table still resolves
            // the same 0.5 / 1.0 pair.
            var plan = MotionSpringClassParser.Resolve(new[] { "scale-50" }, new[] { "scale-100" });
            Assume.That(plan.Scale, Is.Not.Null, "Precondition: the pair resolves a scale channel");

            // Assert
            Assert.That((plan.Scale!.Value.from, plan.Scale.Value.to), Is.EqualTo((0.5f, 1f)));
        }

        [Test]
        public void Given_ANegativeRotateClassPair_When_Resolved_Then_TheRotateChannelMatchesTheSharedMagnitudeTable()
        {
            // Arrange / Act — rotate-45 -> rotate-n45 mirrors the static .rotate-45 / .rotate-n45 USS classes;
            // the parser now reads the magnitude from StyleArbitraryValueResolver's own rotate-preset table
            // (negating it itself for the "n"-suffixed spelling) instead of a duplicate hand-expanded ± table.
            var plan = MotionSpringClassParser.Resolve(new[] { "rotate-45" }, new[] { "rotate-n45" });
            Assume.That(plan.Rotate, Is.Not.Null, "Precondition: the pair resolves a rotate channel");

            // Assert
            Assert.That((plan.Rotate!.Value.from, plan.Rotate.Value.to), Is.EqualTo((45f, -45f)));
        }

        [Test]
        public void Given_ASpringChannelMidExit_When_Retargeted_Then_ItHeadsBackTowardTheValueItStartedFrom()
        {
            // Arrange — an exit-shaped channel: starts at the resting value (1, opaque) and heads to the exit
            // value (0). A few ticks in, it is partway there but not yet settled.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 20f, mass: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            MotionSpringDriver.ApplyCurrentValues(element, state!);
            for (var i = 0; i < 5; i++)
            {
                MotionSpringDriver.Step(element, state!, FixedDeltaSec);
            }
            Assume.That(state!.Opacity!.Target, Is.EqualTo(0f), "Precondition: heading toward the exit value");

            // Act — an exit-cancel (the key re-entered mid-exit): retarget back toward the resting value.
            MotionSpringDriver.Retarget(state!);

            // Assert — the channel's goal flipped to the value it originally started from (its RestingTarget),
            // not a fresh 0/1 default or the exit value it was still short of.
            Assert.That(state!.Opacity!.Target, Is.EqualTo(1f));
        }
    }
}
