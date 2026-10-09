using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements.TestFramework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the scheduled Motion mechanics that only run against a REAL (simulated) panel — their
    /// scheduled swap-to-animate, deferred inline-style clears, and layout-driven FLIP inverse all run
    /// through <c>schedule.Execute().ExecuteLater(ms)</c>, which only fires once a panel ticks its
    /// scheduler against its own clock (the batchmode EditMode PlayerLoop never does). Four mechanics:
    /// (1) <c>V.Motion(layoutId:)</c>'s FLIP behavior — when a Motion's resolved layout rect changes
    /// across a re-render while carrying the same layoutId, MotionLayoutIdDriver applies an inverse
    /// inline transform immediately after layout settles at the new rect, then springs it back to zero,
    /// instead of jump-cutting straight to the new pose; (2) <c>StyleTransitionConfig.StaggerChildrenSec</c>
    /// / <c>DelayChildrenSec</c> / <c>When</c> orchestration — a PLAIN parent → child variant-tree (no
    /// AnimatePresence), where a descendant that follows the ambient label (no own <c>animate</c>) claims
    /// a sequential slot ADDED on top of its own declared delay, riding the runtime-swap play's
    /// <c>additionalDelaySec</c> so the claim delays the SWAP itself rather than parking an inline
    /// <c>transition-delay</c>, while a descendant with its own explicit <c>animate</c> opts out entirely
    /// (Framer parity); (3) a standalone <c>V.Motion(variants:, initial:, animate:)</c> mounted with
    /// NO AnimatePresence — Framer parity dictates <c>initial</c>/<c>animate</c> drive the mount enter on
    /// any <c>motion.*</c> component regardless, with the scheduled swap to the resting
    /// <c>variants[animate]</c> firing on the next tick and a later unrelated re-render never replaying
    /// <c>initial</c>; and (4) a classic (tween) variant enter's frame discipline — the from-state must
    /// survive the tick that started the enter, because the panel computes styles only after the timer
    /// queue drains, and the dangerous shape is production's own: the mount runs inside the panel's own
    /// timer tick, so a zero-delay swap item can become runnable in the very tick that mounted the element.
    /// </summary>
    [TestFixture]
    internal sealed class MotionScheduledMechanicsTests : MotionSimulatedPanelTestsBase
    {
        private const float DurationSec = 0.1f;

        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private static StateUpdater<bool> s_setMoved;
        private static Action<int> s_bump;

        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore(string initial) : base(new SetState(initial)) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState(""));
        }

        private static SetStore s_store;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_setMoved = default;
            s_setStop = default;
            s_setStep = default;
            s_tabs = null;
            s_lists = null;
            s_shared = null;
            s_bump = null;
            s_store = null;
            s_bTransition = null;
            s_sharedClasses = null;
            (s_cMounted, s_cTransition) = (false, null);
            (s_aClasses, s_bClasses) = (null, null);
            (s_aPose, s_aTransition) = (null, null);
            (s_aField, s_looseField) = (false, false);
            (s_modalTransition, s_exitCompletions, s_presenceGone) = (null, 0, false);
            (s_modalLeft, s_moverLeft) = (300, -1);
            s_cardTransition = null;
            (s_cardMounted, s_m1Open, s_m2Open) = (false, false, false);
        }

        [TearDown]
        public override void TearDown()
        {
            _moveMount?.Dispose();
            _moveMount = null;
            base.TearDown();
        }

        [Component]
        private static VNode SharedBoxRender()
        {
            var (moved, setMoved) = Hooks.UseState(false);
            s_setMoved = setMoved;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "shared",
                    layoutId: "shared-box",
                    transition: new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f },
                    className: moved
                        ? "absolute left-[200px] top-[0px] w-[100px] h-[100px]"
                        : "absolute left-[0px] top-[0px] w-[100px] h-[100px]"),
            });
        }

        [Test]
        public void Given_ALayoutIdMotion_When_ItsRectChangesAcrossARerender_Then_AnInverseTranslateAppliesImmediatelyAfterLayoutSettles()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(SharedBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            Assume.That(element, Is.Not.Null, "Precondition: the Motion mounted");

            // Act — move the Motion 200px to the right; wait one tick for GeometryChangedEvent to fire and
            // the driver to apply the inverse pose.
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the element is pinned at (roughly) its OLD screen position via an inline translate,
            // even though the resolved layout already moved it 200px right (translate.x ~= -200).
            Assert.That(element.style.translate.value.x.value, Is.LessThan(-50f));
        }

        [Test]
        public void Given_ALayoutIdMotion_When_SeveralTicksElapseAfterARectChange_Then_TheInverseTranslateSettlesToZero()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(SharedBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();
            Assume.That(element.style.translate.value.x.value, Is.LessThan(-50f),
                "Precondition: the inverse pose applied after the rect change");

            // Act — let the spring settle.
            AdvancePast(2f);

            // Assert — the inline translate override is cleared once the spring settles (StyleKeyword.Null),
            // reporting back to StyleKeyword.Auto / 0 the way MotionSpringDriver.ClearInlineOverrides always
            // leaves a settled channel.
            Assert.That(element.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AMotionWithAnArbitraryDuration_When_CodeReassignsItsTweensDurationBeforeItIsObserved_Then_ItsOwnDurationReturns(
            bool changeFirst)
        {
            // Arrange — "a" carrying duration-[400ms], visible on its variants.
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_aPose, s_aTransition, s_aClasses) = ("visible", s_quickTween, "duration-[400ms]");
            using var mounted = MountAAlone();

            s_aPose = "hidden";
            RenderShared(mounted);
            var a = Root.Q<VisualElement>("a");
            var temporary = a.style.transitionDuration;
            var tweenHoldsItsDuration = temporary.keyword == StyleKeyword.Undefined
                && temporary.value?.Count == 1 && temporary.value[0].unit == TimeUnit.Millisecond
                && Mathf.Approximately(temporary.value[0].value, 100f);

            // Act — reassign the temporary value, optionally after a change undone before timing is observed.
            if (changeFirst) a.style.transitionDuration = new List<TimeValue> { new(200f, TimeUnit.Millisecond) };
            a.style.transitionDuration = new List<TimeValue> { new(100f, TimeUnit.Millisecond) };
            AdvancePast(0.3f);

            // Assert
            var durations = a.style.transitionDuration;
            Assert.That((tweenHoldsItsDuration, durations.keyword,
                    durations.value?.Count == 1 && Mathf.Approximately(durations.value[0].value, 0.4f)),
                Is.EqualTo((true, StyleKeyword.Undefined, true)));
        }

        [Test]
        public void Given_AMotionWithAnArbitraryDuration_When_AVariantTweenWithAPropertyOverrideEnds_Then_ItsDurationIsItsOwnAgain()
        {
            // Arrange — "a" carrying duration-[400ms], visible on its variants.
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_aPose, s_aClasses) = ("visible", "duration-[400ms]");
            s_aTransition = new StyleTransitionConfig
            {
                DurationSec = 0.1f, Easing = EasingMode.Linear,
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", durationSec: 0.15f) },
            };
            using var mounted = MountAAlone();

            // Act — a swap to its hidden pose, its opacity on an override of its own, and past its end.
            s_aPose = "hidden";
            RenderShared(mounted);
            AdvancePast(0.4f);

            // Assert — its own four tenths, rather than none.
            var durations = Root.Q<VisualElement>("a").style.transitionDuration;
            Assert.That((durations.keyword, durations.value?.Count == 1 && Mathf.Approximately(durations.value[0].value, 0.4f)),
                Is.EqualTo((StyleKeyword.Undefined, true)));
        }

        [Test]
        public void Given_AMotionWhoseCodeWritesItsDurationMidVariantTween_When_ASecondSwapStartsAndEnds_Then_ThatDurationIsItsOwn()
        {
            // Arrange
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_aPose, s_aTransition) = ("visible", s_slowTween);
            using var mounted = MountAAlone();
            var a = Root.Q<VisualElement>("a");
            a.style.transitionDuration = new List<TimeValue> { new(0.4f, TimeUnit.Second) };
            a.style.transitionDelay = new List<TimeValue> { new(0.02f, TimeUnit.Second) };
            a.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOut) };
            s_aPose = "hidden";
            RenderShared(mounted);
            for (var i = 0; i < 5; i++) Tick();
            a.style.transitionDuration = new List<TimeValue> { new(0.25f, TimeUnit.Second) };
            a.style.transitionDelay = new List<TimeValue> { new(0.07f, TimeUnit.Second) };
            a.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseIn) };

            // Act — a swap back to its visible pose; past that tween's end.
            s_aPose = "visible";
            RenderShared(mounted);
            AdvancePast(1.2f);

            // Assert
            var durations = a.style.transitionDuration;
            var delays = a.style.transitionDelay;
            var curves = a.style.transitionTimingFunction;
            Assert.That((durations.keyword, durations.value?.Count == 1 && Mathf.Approximately(durations.value[0].value, 0.25f),
                    delays.keyword, delays.value?.Count == 1 && Mathf.Approximately(delays.value[0].value, 0.07f),
                    curves.keyword, curves.value?.Count == 1 && curves.value[0].Equals(new EasingFunction(EasingMode.EaseIn))),
                Is.EqualTo((StyleKeyword.Undefined, true, StyleKeyword.Undefined, true, StyleKeyword.Undefined, true)));
        }

        [Test]
        public void Given_AMotionWhoseCodeWritesAPrefixOfItsOverrideTweensDurations_When_ThatTweenEnds_Then_ThatPrefixIsItsOwn()
        {
            // Arrange — the tween writes 100ms for its catch-all and 150ms for opacity.
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            s_aPose = "visible";
            s_aTransition = new StyleTransitionConfig
            {
                DurationSec = 0.1f, Easing = EasingMode.Linear,
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", durationSec: 0.15f) },
            };
            using var mounted = MountAAlone();
            s_aPose = "hidden";
            RenderShared(mounted);
            var a = Root.Q<VisualElement>("a");

            // Act — its first entry alone, and past the tween's end.
            a.style.transitionDuration = new List<TimeValue> { new(100f, TimeUnit.Millisecond) };
            AdvancePast(0.4f);

            // Assert
            var durations = a.style.transitionDuration;
            Assert.That((durations.keyword,
                    durations.value?.Count == 1 && durations.value[0].Equals(new TimeValue(100f, TimeUnit.Millisecond))),
                Is.EqualTo((StyleKeyword.Undefined, true)));
        }

        [Test]
        public void Given_AMotionWhoseCodeWritesTheLastTweensDurationAfterItEnded_When_ANextTweenEnds_Then_ThatDurationIsItsOwn()
        {
            // Arrange — a 100ms tween that ends with no timing of the element's own.
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_aPose, s_aTransition) = ("visible", s_quickTween);
            using var mounted = MountAAlone();
            s_aPose = "hidden";
            RenderShared(mounted);
            AdvancePast(0.3f);
            var a = Root.Q<VisualElement>("a");
            a.style.transitionDuration = new List<TimeValue> { new(100f, TimeUnit.Millisecond) };

            // Act — a swap back on the same tween, and past its end.
            s_aPose = "visible";
            RenderShared(mounted);
            AdvancePast(0.3f);

            // Assert
            var durations = a.style.transitionDuration;
            Assert.That((durations.keyword,
                    durations.value?.Count == 1 && durations.value[0].Equals(new TimeValue(100f, TimeUnit.Millisecond))),
                Is.EqualTo((StyleKeyword.Undefined, true)));
        }

        private static readonly string[] s_hiddenPose = { "opacity-0" };
        private static readonly string[] s_visiblePose = { "opacity-100" };

        private static StyleTransitionConfig LinearTween(float durationSec, float delaySec = 0f) =>
            new() { DurationSec = durationSec, DelaySec = delaySec, Easing = EasingMode.Linear };

        private static StyleTransitionConfig LinearExit(float durationSec, float delaySec = 0f) => new()
        {
            DurationSec = durationSec, DelaySec = delaySec, Easing = EasingMode.Linear,
            ExitFromClass = "opacity-100", ExitToClass = "opacity-0",
        };

        private static void WriteTimingSlot(VisualElement element, string slot)
        {
            switch (slot)
            {
                case "duration":
                    element.style.transitionDuration = new List<TimeValue> { new(250f, TimeUnit.Millisecond) };
                    break;
                case "delay":
                    element.style.transitionDelay = new List<TimeValue> { new(70f, TimeUnit.Millisecond) };
                    break;
                default:
                    element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseIn) };
                    break;
            }
        }

        private static string TimingSlot(VisualElement element, string slot)
        {
            if (slot == "curve")
            {
                var curves = element.style.transitionTimingFunction;
                var modes = new List<string>();
                foreach (var curve in curves.value ?? new List<EasingFunction>()) modes.Add(curve.mode.ToString());
                return curves.keyword + ":" + string.Join(",", modes);
            }
            var times = slot == "duration" ? element.style.transitionDuration : element.style.transitionDelay;
            return times.keyword + ":" + (times.value == null ? "" : string.Join(",", times.value));
        }

        // A Div whose duration-[400ms] the resolver writes inline, for a scheduler of the case's own to play on.
        private VisualElement ElementWithItsOwnDuration()
        {
            _reconciler.Reconcile(Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(name: "own", className: "duration-[400ms]") });
            return Root.Q<VisualElement>("own");
        }

        private static (StyleKeyword, bool) OwnDurationReading(VisualElement element)
        {
            var durations = element.style.transitionDuration;
            return (durations.keyword, durations.value?.Count == 1 && Mathf.Approximately(durations.value[0].value, 0.4f));
        }

        [TestCase(0.1f, 0.3f)]
        [TestCase(0.3f, 0.1f)]
        public void Given_AnElementWithItsOwnDuration_When_AnEnterAndAnExitTweenOverlapOnItAndBothEnd_Then_ItsDurationIsItsOwn(
            float enterSec, float exitSec)
        {
            // Arrange
            var element = ElementWithItsOwnDuration();
            var scheduler = new StyleAnimationScheduler();

            // Act — an enter, an exit started on top of it, and past both ends in the order the durations give.
            scheduler.PlayVariantEnter(element, s_hiddenPose, s_visiblePose, LinearTween(enterSec));
            scheduler.PlayExit(element, LinearExit(exitSec), onComplete: null);
            AdvancePast(0.8f);

            // Assert
            Assert.That(OwnDurationReading(element), Is.EqualTo((StyleKeyword.Undefined, true)));
        }

        [Test]
        public void Given_AnEnterTweenWithAnExitTweenStartedOnTopOfIt_When_TheEnterEnds_Then_TheExitsDurationStillTimesTheElement()
        {
            // Arrange
            var element = ElementWithItsOwnDuration();
            var scheduler = new StyleAnimationScheduler();
            scheduler.PlayVariantEnter(element, s_hiddenPose, s_visiblePose, LinearTween(0.1f));
            scheduler.PlayExit(element, LinearExit(0.6f), onComplete: null);

            // Act — past the enter's end, short of the exit's.
            AdvancePast(0.2f);

            // Assert
            var durations = element.style.transitionDuration;
            Assert.That((durations.keyword,
                    durations.value?.Count == 1 && durations.value[0].Equals(new TimeValue(600f, TimeUnit.Millisecond))),
                Is.EqualTo((StyleKeyword.Undefined, true)));
        }

        [Test]
        public void Given_AnEnterTweenWithAnExitTweenStartedOnTopOfIt_When_TheExitEnds_Then_TheEntersDurationTimesTheElementAgain()
        {
            // Arrange
            var element = ElementWithItsOwnDuration();
            var scheduler = new StyleAnimationScheduler();
            scheduler.PlayVariantEnter(element, s_hiddenPose, s_visiblePose, LinearTween(0.6f));
            scheduler.PlayExit(element, LinearExit(0.1f), onComplete: null);

            // Act — past the exit's end, short of the enter's.
            AdvancePast(0.2f);

            // Assert
            var durations = element.style.transitionDuration;
            Assert.That((durations.keyword,
                    durations.value?.Count == 1 && durations.value[0].Equals(new TimeValue(600f, TimeUnit.Millisecond))),
                Is.EqualTo((StyleKeyword.Undefined, true)));
        }

        [TestCase("duration", "Undefined:250ms")]
        [TestCase("delay", "Undefined:70ms")]
        [TestCase("curve", "Undefined:EaseIn")]
        public void Given_TwoTweensOverlappingWhenCodeWritesATimingSlot_When_TheLaterEnds_Then_TheSlotKeepsTheCodesValue(
            string slot, string expected)
        {
            // Arrange — both tweens write all three slots.
            var element = ElementWithItsOwnDuration();
            var scheduler = new StyleAnimationScheduler();
            scheduler.PlayVariantEnter(element, s_hiddenPose, s_visiblePose, LinearTween(0.6f, delaySec: 0.01f));
            scheduler.PlayExit(element, LinearExit(0.1f, delaySec: 0.01f), onComplete: null);
            WriteTimingSlot(element, slot);

            // Act — past the exit's end, short of the enter's.
            AdvancePast(0.2f);

            // Assert
            Assert.That(TimingSlot(element, slot), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base writes a landing straight into the slot on screen.
        // With tween timing layered, a landing for an enter under a later tween must still reach that slot.
        [Test]
        public void Given_AnExitTweenStartedOverAVariantEnter_When_AZeroDurationPoseLandsOnTheEnter_Then_TheLandingIsOnScreen()
        {
            // Arrange
            var element = ElementWithItsOwnDuration();
            var scheduler = new StyleAnimationScheduler();
            scheduler.PlayVariantEnter(element, s_hiddenPose, s_visiblePose, LinearTween(0.5f));
            scheduler.PlayExit(element, LinearExit(0.6f), onComplete: null);

            // Act
            scheduler.LandNamedProperties(element, new[] { "opacity-50" }, StyleTransitionConfig.None);

            // Assert — the exit's 600ms, with opacity's 1ms landing entry after it.
            var durations = element.style.transitionDuration.value;
            Assert.That(durations == null ? "" : string.Join(",", durations), Is.EqualTo("600ms,1ms"));
        }

        [Test]
        public void Given_AnExitWhoseDelayCodeWroteBeforeItRestarted_When_TheRestartEndsBeforeTheReversal_Then_TheDelayIsTheCodes()
        {
            // Arrange — the exits write no delay of their own.
            var element = ElementWithItsOwnDuration();
            var scheduler = new StyleAnimationScheduler();
            scheduler.PlayExit(element, LinearExit(0.6f), onComplete: null);
            WriteTimingSlot(element, "delay");
            scheduler.PlayExit(element, LinearExit(0.1f), onComplete: null);

            // Act — past the restarted exit's end, short of the reversal's.
            AdvancePast(0.2f);

            // Assert
            Assert.That(TimingSlot(element, "delay"), Is.EqualTo("Undefined:70ms"));
        }

        [Test]
        public void Given_AnElementWithItsOwnDuration_When_AnExitTweenRestartsWhileItPlays_Then_ItsDurationIsItsOwnOnceBothEnd()
        {
            // Arrange
            var element = ElementWithItsOwnDuration();
            var scheduler = new StyleAnimationScheduler();
            scheduler.PlayExit(element, LinearExit(0.3f), onComplete: null);

            // Act — the restart hands the first exit to a reversal; past both ends.
            scheduler.PlayExit(element, LinearExit(0.3f), onComplete: null);
            AdvancePast(1f);

            // Assert
            Assert.That(OwnDurationReading(element), Is.EqualTo((StyleKeyword.Undefined, true)));
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Tween)]
        public void Given_AnElementWithItsOwnDuration_When_CancelAllStopsAPlayOnIt_Then_ItsDurationIsItsOwn(TransitionType type)
        {
            // Arrange
            var element = ElementWithItsOwnDuration();
            var scheduler = new StyleAnimationScheduler();
            var config = type == TransitionType.Spring
                ? new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 200f, Damping = 26f }
                : LinearTween(0.3f);
            scheduler.PlayVariantEnter(element, s_hiddenPose, s_visiblePose, config);

            // Act
            scheduler.CancelAll();

            // Assert
            Assert.That(OwnDurationReading(element), Is.EqualTo((StyleKeyword.Undefined, true)));
        }

        // Where the replacement sits after the move across parents; the mounted element sits at left 200.
        private static int s_movedLeft;

        // One layoutId Motion, moved from the second parent to the first. The first parent reconciles first,
        // so the replacement is created while the element it replaces is still mounted, and the mounted one
        // is never patched in between.
        [Component]
        private static VNode AcrossParentsBoxRender()
        {
            var (moved, setMoved) = Hooks.UseState(false);
            s_setMoved = setMoved;
            VNode Box(int left) => V.Motion(
                name: "shared",
                layoutId: "shared-box",
                transition: new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f },
                className: $"left-[{left}px] top-[0px] w-[100px] h-[100px]");
            return V.Div(children: new VNode[]
            {
                V.Div(key: "first", children: moved ? new[] { Box(s_movedLeft) } : Array.Empty<VNode>()),
                V.Div(key: "second", children: moved ? Array.Empty<VNode>() : new[] { Box(200) }),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionThatNeverMoved_When_ItMovesToAnotherParentAtTheSameBox_Then_TheReplacementCarriesNoInversePose()
        {
            // Arrange
            s_movedLeft = 200;
            using var mounted = V.Mount(Root, V.Component(AcrossParentsBoxRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");

            // Act
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();

            // Assert — a new element, standing where the old one stood, with neither an inline translate nor
            // an inline scale.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That((ReferenceEquals(original, replacement), replacement.style.translate.keyword, replacement.style.scale.keyword),
                Is.EqualTo((false, StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ALayoutIdMotionThatNeverMoved_When_ItMovesToAnotherParentAtAnotherBox_Then_TheReplacementTweensFromTheOldBoxAtFullSize()
        {
            // Arrange
            s_movedLeft = 400;
            using var mounted = V.Mount(Root, V.Component(AcrossParentsBoxRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");

            // Act
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned near the old element's box, 200px left of its own, at its own size.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That((ReferenceEquals(original, replacement),
                    UnityEngine.Mathf.Abs(replacement.style.translate.value.x.value + 200f) < 50f,
                    replacement.style.scale.keyword),
                Is.EqualTo((false, true, StyleKeyword.Null)));
        }

        private static StateUpdater<int> s_setStep;

        private static readonly StyleTransitionConfig s_layoutSpring =
            new() { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f };

        private static VNode SharedBox(int left, Type elementType = null) => V.Motion(
            name: "shared",
            layoutId: "shared-box",
            elementType: elementType,
            transition: s_layoutSpring,
            className: $"left-[{left}px] top-[0px] w-[100px] h-[100px]");

        // Step 1 is a same-key type flip: the element type changes, so the reconciler tears the mounted
        // element down before it creates the replacement.
        [Component]
        private static VNode TypeFlipBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[] { step == 0 ? SharedBox(200) : SharedBox(200 + step * 200, typeof(Box)) });
        }

        [Test]
        public void Given_ALayoutIdMotion_When_ASameKeyTypeFlipReplacesItElsewhere_Then_TheReplacementTweensFromTheOldBoxAtFullSize()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(TypeFlipBoxRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned near the old element's box, 200px left of its own, at its own size.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That((ReferenceEquals(original, replacement),
                    UnityEngine.Mathf.Abs(replacement.style.translate.value.x.value + 200f) < 50f,
                    replacement.style.scale.keyword),
                Is.EqualTo((false, true, StyleKeyword.Null)));
        }

        // GREEN_ON_BASE(characterization): the base tweens a moved Motion from its own previous box.
        // A replacement's registration must outlive the pass boundary that expires teardown boxes.
        [Test]
        public void Given_ASameKeyTypeFlipsReplacementWhoseTweenSettled_When_ItMovesAgain_Then_ItTweensFromItsOwnBox()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(TypeFlipBoxRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();
            AdvancePast(2f);

            // Act
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned near its own previous box, 200px left of the new one.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That(UnityEngine.Mathf.Abs(replacement.style.translate.value.x.value + 200f), Is.LessThan(50f));
        }

        // Step 1 removes the Motion and step 2 mounts another under the same id: two renders, with no
        // frame between them.
        [Component]
        private static VNode RemoveThenRemountBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: step switch
            {
                0 => new[] { SharedBox(200) },
                1 => Array.Empty<VNode>(),
                _ => new[] { SharedBox(400) },
            });
        }

        // GREEN_ON_BASE(characterization): the base keeps no box for an id whose element left in an earlier render.
        // The box a teardown now leaves for a same-key type flip must expire with its render.
        [Test]
        public void Given_ALayoutIdMotionRemovedInOneRender_When_ALaterRenderMountsAnotherUnderTheSameId_Then_ItCarriesNoInversePose()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(RemoveThenRemountBoxRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            var removed = Root.Q<VisualElement>("shared");

            // Act
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert
            var remounted = Root.Q<VisualElement>("shared");
            Assert.That((removed, remounted?.style.translate.keyword),
                Is.EqualTo(((VisualElement)null, (StyleKeyword?)StyleKeyword.Null)));
        }

        // Two 150px-tall parents, one below the other: the box stands at the same place inside either, and
        // 150px apart on screen.
        [Component]
        private static VNode OffsetParentsBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Div(key: "first", className: "h-[150px]", children: step == 1 ? new[] { SharedBox(200) } : Array.Empty<VNode>()),
                V.Div(key: "second", className: "h-[150px]", children: step == 1 ? Array.Empty<VNode>() : new[] { SharedBox(200) }),
            });
        }

        [Test]
        public void Given_ALayoutIdMotion_When_ItMovesToAParentAboveAtTheSameLocalBox_Then_TheReplacementTweensFromTheOldScreenPosition()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(OffsetParentsBoxRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned near the old element's box, 150px below its own.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That((ReferenceEquals(original, replacement),
                    UnityEngine.Mathf.Abs(replacement.style.translate.value.y.value - 150f) < 50f,
                    replacement.style.scale.keyword),
                Is.EqualTo((false, true, StyleKeyword.Null)));
        }

        // A pooled Label-typed layoutId Motion moves twice (steps 1 and 2) and is removed (step 3) before any
        // layout settles either move; step 4 rents a Label for an unrelated Motion that carries no layoutId.
        [Component]
        private static VNode PooledMotionReuseRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: step switch
            {
                0 => new[] { SharedBox(0, typeof(Label)) },
                1 => new[] { SharedBox(200, typeof(Label)) },
                2 => new[] { SharedBox(250, typeof(Label)) },
                3 => Array.Empty<VNode>(),
                _ => new VNode[]
                {
                    V.Motion(name: "reused", elementType: typeof(Label), transition: s_layoutSpring,
                        className: "left-[300px] top-[0px] w-[50px] h-[50px]"),
                },
            });
        }

        [Test]
        public void Given_ALayoutIdMotionRemovedBeforeEitherOfTwoMovesSettled_When_ThePoolHandsItsElementToAnotherMotion_Then_TheOtherMotionCarriesNoInversePose()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(PooledMotionReuseRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            s_setStep.Invoke(3);
            mounted.FlushStateForTest();

            // Act
            s_setStep.Invoke(4);
            mounted.FlushStateForTest();
            Tick();

            // Assert
            var reused = Root.Q<VisualElement>("reused");
            Assert.That((ReferenceEquals(original, reused), reused.style.translate.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }

        // Three parents; each step moves the box to the parent before the one holding it, so every
        // replacement is created while the element it replaces is still mounted.
        [Component]
        private static VNode ThreeParentsBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            VNode[] At(int parent, int left) => step == 2 - parent ? new[] { SharedBox(left) } : Array.Empty<VNode>();
            return V.Div(children: new VNode[]
            {
                V.Div(key: "first", children: At(0, 400)),
                V.Div(key: "second", children: At(1, 200)),
                V.Div(key: "third", children: At(2, 0)),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionReplacedTwiceBeforeALayout_When_TheLastReplacementSettles_Then_ItTweensFromTheBoxOnScreenBeforeBoth()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(ThreeParentsBoxRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned near the first element's box, 400px left of its own, at its own size.
            var last = Root.Q<VisualElement>("shared");
            Assert.That((UnityEngine.Mathf.Abs(last.style.translate.value.x.value + 400f) < 50f, last.style.scale.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }

        // A layoutId Motion inside another: step 1 moves the outer one 200px right, and the inner one 20px
        // right inside it.
        [Component]
        private static VNode NestedLayoutIdRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "outer",
                    layoutId: "outer-box",
                    transition: s_layoutSpring,
                    className: $"left-[{step * 200}px] top-[0px] w-[300px] h-[300px]",
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", layoutId: "inner-box", transition: s_layoutSpring,
                            className: $"left-[{20 + step * 20}px] top-[20px] w-[50px] h-[50px]"),
                    }),
            });
        }

        // GREEN_ON_BASE(characterization): the base tweens a nested layoutId Motion by its own move inside its parent.
        [Test]
        public void Given_ALayoutIdMotionInsideAnother_When_BothMove_Then_TheInnerTweensOnlyItsOwnMoveInsideTheOuter()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(NestedLayoutIdRender, key: "root"));
            Tick();
            var outer = Root.Q<VisualElement>("outer");
            var inner = Root.Q<VisualElement>("inner");

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the outer one tweens from its old place and carries the inner one, which tweens only
            // the 20px it moved inside the outer one.
            Assert.That((outer.style.translate.value.x.value < -150f,
                    UnityEngine.Mathf.Abs(inner.style.translate.value.x.value + 20f) < 10f),
                Is.EqualTo((true, true)));
        }

        // The host's padding changes outside any render: the box moves without a patch.
        [Component]
        private static VNode PaddedHostBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(name: "host", children: new VNode[] { SharedBox(step * 200) });
        }

        // GREEN_ON_BASE(characterization): the base plays a layoutId tween once per patch that moved the box.
        // A layout change no render made must not replay the wait a settled tween already consumed.
        [Test]
        public void Given_ALayoutIdTweenThatSettled_When_ALayoutChangeNoRenderMadeMovesTheBox_Then_NoTweenPlays()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(PaddedHostBoxRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();
            AdvancePast(2f);
            var element = Root.Q<VisualElement>("shared");

            // Act
            Root.Q<VisualElement>("host").style.paddingLeft = 30f;
            Tick();

            // Assert — the box did move, and carries no inline translate.
            Assert.That((element.layout.x, element.style.translate.keyword), Is.EqualTo((230f, StyleKeyword.Null)));
        }

        private static readonly string[] s_rowIds =
        {
            "row-0", "row-1", "row-2", "row-3", "row-4", "row-5", "row-6", "row-7", "row-8", "row-9", "row-10", "row-11",
        };

        // Each row is a layoutId Motion under its own id; a scroll renders the range outside any render.
        private static VirtualListNode LayoutIdRows() => V.VirtualList(
            items: s_rowIds,
            keySelector: id => id,
            itemHeight: 50f,
            renderer: id => V.Motion(key: id, name: id, layoutId: id, transition: s_layoutSpring, className: "w-[100px] h-[50px]"),
            overscan: 0);

        // GREEN_ON_BASE(characterization): the base forgets an id whose element a scroll took out of range.
        // A teardown outside every pass must not leave a box a later render claims.
        [Test]
        public void Given_ALayoutIdRowAScrollTookOutOfRange_When_ALaterRenderMountsAnotherUnderItsId_Then_ItCarriesNoInversePose()
        {
            // Arrange — the rows re-render once after layout, so row-0 holds its own box when the scroll takes it.
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            Root.Add(scrollView);
            using var controller = new FiberVirtualListController(scrollView, LayoutIdRows(), _reconciler);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 100f);
            Tick();
            controller.Update(LayoutIdRows());
            Tick();
            var row0 = Root.Q<VisualElement>("row-0");
            controller.UpdateVisibleRange(scrollY: 500f, viewportHeight: 100f);
            var host = new VisualElement();
            Root.Add(host);

            // Act
            _reconciler.Reconcile(host, Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(name: "remount", layoutId: "row-0", transition: s_layoutSpring,
                    className: "left-[300px] top-[0px] w-[100px] h-[50px]"),
            });
            Tick();

            // Assert
            var remounted = host.Q<VisualElement>("remount");
            Assert.That((row0.panel, remounted.style.translate.keyword), Is.EqualTo(((IPanel)null, StyleKeyword.Null)));
        }

        // Step 1 flips the outer Motion's element type and moves it 200px right, and moves the inner one 20px
        // right inside it: both are recreated in one render, the inner one under a new parent.
        [Component]
        private static VNode NestedInTypeFlippedOuterRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "outer",
                    layoutId: "outer-box",
                    elementType: step == 0 ? null : typeof(Box),
                    transition: s_layoutSpring,
                    className: $"left-[{step * 200}px] top-[0px] w-[300px] h-[300px]",
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", layoutId: "inner-box", transition: s_layoutSpring,
                            className: $"left-[{20 + step * 20}px] top-[20px] w-[50px] h-[50px]"),
                    }),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionInsideATypeFlippedOne_When_BothMove_Then_TheInnerTweensOnlyItsOwnMoveInsideTheOuter()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(NestedInTypeFlippedOuterRender, key: "root"));
            Tick();

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the outer one tweens from 200px left and carries the inner one, which tweens only the
            // 20px it moved inside the outer one.
            var outer = Root.Q<VisualElement>("outer");
            var inner = Root.Q<VisualElement>("inner");
            Assert.That((UnityEngine.Mathf.Abs(outer.style.translate.value.x.value + 200f) < 50f,
                    UnityEngine.Mathf.Abs(inner.style.translate.value.x.value + 20f) < 10f),
                Is.EqualTo((true, true)));
        }

        private readonly record struct TabState(int Index);

        private sealed class TabStore : Store<TabState>
        {
            public TabStore(int initial) : base(new TabState(initial)) { }
            public void Select(int index) => SetState(_ => new TabState(index));
            protected override void ResetCore() => SetState(_ => new TabState(0));
        }

        private static TabStore s_tabs;

        // Two tab components 200px apart, each reading the selected index from one store; the selected one
        // renders the underline. One store update re-renders both.
        private static VNode TabBody(int index, int selected) => V.Div(
            className: $"left-[{index * 200}px] top-[0px] w-[100px] h-[50px]",
            children: selected == index
                ? new VNode[] { V.Motion(name: "underline", layoutId: "underline", transition: s_layoutSpring, className: "w-[100px] h-[10px]") }
                : Array.Empty<VNode>());

        [Component]
        private static VNode FirstTabRender() => TabBody(0, Hooks.UseStore(s_tabs, s => s.Index));

        [Component]
        private static VNode SecondTabRender() => TabBody(1, Hooks.UseStore(s_tabs, s => s.Index));

        [Component]
        private static VNode TabsRender() => V.Div(children: new VNode[]
        {
            V.Component(FirstTabRender, key: "first"),
            V.Component(SecondTabRender, key: "second"),
        });

        private float UnderlineTranslateAfterSelecting(int from, int to)
        {
            s_tabs = new TabStore(from);
            using var mounted = V.Mount(Root, V.Component(TabsRender, key: "root"));
            Tick();
            Tick();
            s_tabs.Select(to);
            Tick();
            Tick();
            var underline = Root.Q<VisualElement>("underline");
            return underline.style.scale.keyword == StyleKeyword.Null ? underline.style.translate.value.x.value : float.NaN;
        }

        [Test]
        public void Given_TwoTabComponentsOnOneStore_When_TheSecondIsSelected_Then_TheUnderlineTweensFromTheFirst()
        {
            // Arrange / Act
            var translate = UnderlineTranslateAfterSelecting(0, 1);

            // Assert — pinned near the first tab, 200px left of the second.
            Assert.That(UnityEngine.Mathf.Abs(translate + 200f), Is.LessThan(50f));
        }

        // GREEN_ON_BASE(characterization): the base already tweens the underline in this direction.
        // Making the other direction tween must leave this one as it was.
        [Test]
        public void Given_TwoTabComponentsOnOneStore_When_TheFirstIsSelected_Then_TheUnderlineTweensFromTheSecond()
        {
            // Arrange / Act
            var translate = UnderlineTranslateAfterSelecting(1, 0);

            // Assert — pinned near the second tab, 200px right of the first.
            Assert.That(UnityEngine.Mathf.Abs(translate - 200f), Is.LessThan(50f));
        }

        // GREEN_ON_BASE(characterization): the base keeps no box for an id whose element left in an earlier render.
        // A box a batch drain leaves must expire at that drain's end rather than wait for a pass outside one.
        [Test]
        public void Given_TwoTabComponentsOnOneStore_When_NoneIsSelectedAndThenTheSecond_Then_TheUnderlineAppearsInPlace()
        {
            // Arrange
            s_tabs = new TabStore(0);
            using var mounted = V.Mount(Root, V.Component(TabsRender, key: "root"));
            Tick();
            Tick();
            s_tabs.Select(-1);
            Tick();
            Tick();
            var removed = Root.Q<VisualElement>("underline");

            // Act
            s_tabs.Select(1);
            Tick();
            Tick();

            // Assert
            var underline = Root.Q<VisualElement>("underline");
            Assert.That((removed, underline?.style.translate.keyword),
                Is.EqualTo(((VisualElement)null, (StyleKeyword?)StyleKeyword.Null)));
        }

        // Step 1 moves the box out of a parent drawn at half scale into an unscaled one above it.
        [Component]
        private static VNode ScaledParentBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Div(key: "first", className: "w-[400px] h-[200px]", children: step == 1 ? new[] { SharedBox(200) } : Array.Empty<VNode>()),
                V.Div(key: "second", className: "w-[400px] h-[200px] scale-[0.5]",
                    children: step == 1 ? Array.Empty<VNode>() : new[] { SharedBox(200) }),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionInAHalfScaleParent_When_ItMovesToAnUnscaledOne_Then_ItTweensFromHalfItsSize()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(ScaledParentBoxRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — a new element, starting at the half size the old one was drawn at.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That((ReferenceEquals(original, replacement),
                    UnityEngine.Mathf.Abs(replacement.style.scale.value.value.x - 0.5f) < 0.1f),
                Is.EqualTo((false, true)));
        }

        // Step 1 moves the outer layoutId Motion 200px right; step 2 moves only the inner one, 20px right
        // inside it.
        [Component]
        private static VNode OuterThenInnerMoveRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "outer",
                    layoutId: "outer-box",
                    transition: s_layoutSpring,
                    className: $"left-[{(step >= 1 ? 200 : 0)}px] top-[0px] w-[300px] h-[300px]",
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", layoutId: "inner-box", transition: s_layoutSpring,
                            className: $"left-[{(step >= 2 ? 40 : 20)}px] top-[20px] w-[50px] h-[50px]"),
                    }),
            });
        }

        // GREEN_ON_BASE(characterization): the base compares a nested layoutId Motion's rects inside its parent.
        // Comparing them in panel space instead takes in the outer Motion's tween between patch and settle.
        [Test]
        public void Given_AnOuterLayoutIdMotionStillTweening_When_OnlyTheInnerMovesInsideIt_Then_TheInnerTweensOnlyItsOwnMove()
        {
            // Arrange — the outer one's tween is a few frames in.
            using var mounted = V.Mount(Root, V.Component(OuterThenInnerMoveRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 5; i++) Tick();
            var outer = Root.Q<VisualElement>("outer");
            var inner = Root.Q<VisualElement>("inner");

            // Act
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the outer one is still mid-tween, and the inner one tweens only the 20px it moved.
            Assert.That((outer.style.translate.value.x.value < -50f,
                    UnityEngine.Mathf.Abs(inner.style.translate.value.x.value + 20f) < 5f),
                Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base leaves a tweening outer Motion alone when only an inner one moves.
        // Settling a moved ancestor first must not restart one that did not move.
        [Test]
        public void Given_AnOuterLayoutIdMotionStillTweening_When_OnlyTheInnerMovesInsideIt_Then_TheOuterTweenCarriesOn()
        {
            // Arrange — the outer one's tween is a few frames in and still closing on its box.
            using var mounted = V.Mount(Root, V.Component(OuterThenInnerMoveRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 5; i++) Tick();
            var outer = Root.Q<VisualElement>("outer");
            var before = TranslateX(outer);

            // Act
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert — a frame further on; restarted from where it was drawn at the patch, it would stand still.
            Assert.That(before - TranslateX(outer), Is.LessThan(-5f));
        }

        // A board rotated 45 degrees holding two 200px-tall columns; step 1 moves the box from the second
        // column to the first, 200px up the board.
        [Component]
        private static VNode RotatedBoardBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(className: "left-[200px] top-[200px] w-[400px] h-[400px] rotate-[45deg]", children: new VNode[]
            {
                V.Div(key: "first", className: "w-[400px] h-[200px]", children: step == 1 ? new[] { SharedBox(200) } : Array.Empty<VNode>()),
                V.Div(key: "second", className: "w-[400px] h-[200px]", children: step == 1 ? Array.Empty<VNode>() : new[] { SharedBox(200) }),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionInARotatedBoard_When_ItMovesToTheOtherColumn_Then_ItTweensFromItsOldPlaceAtItsOwnSize()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(RotatedBoardBoxRender, key: "root"));
            Tick();

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned 200px down the board from its new place, and not scaled.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That((UnityEngine.Mathf.Abs(replacement.style.translate.value.x.value) < 5f,
                    UnityEngine.Mathf.Abs(replacement.style.translate.value.y.value - 200f) < 5f,
                    replacement.style.scale.keyword),
                Is.EqualTo((true, true, StyleKeyword.Null)));
        }

        private static TabStore s_lists;

        // Two list components 300px apart on one store; the selected one renders a Button holding the card.
        // The Button is a pooled element type.
        private static VNode ListBody(int index, int selected) => V.Div(
            className: $"left-[{index * 300}px] top-[0px] w-[200px] h-[100px]",
            children: selected == index
                ? new VNode[]
                {
                    V.Button(children: new VNode[]
                    {
                        V.Motion(name: "card", layoutId: "card", transition: s_layoutSpring, className: "w-[100px] h-[50px]"),
                    }),
                }
                : Array.Empty<VNode>());

        [Component]
        private static VNode FirstListRender() => ListBody(0, Hooks.UseStore(s_lists, s => s.Index));

        [Component]
        private static VNode SecondListRender() => ListBody(1, Hooks.UseStore(s_lists, s => s.Index));

        [Component]
        private static VNode ListsRender() => V.Div(children: new VNode[]
        {
            V.Component(FirstListRender, key: "first"),
            V.Component(SecondListRender, key: "second"),
        });

        [Test]
        public void Given_TwoListComponentsHoldingTheCardInAPooledButton_When_TheSecondIsSelected_Then_TheCardTweensFromTheFirst()
        {
            // Arrange
            s_lists = new TabStore(0);
            using var mounted = V.Mount(Root, V.Component(ListsRender, key: "root"));
            Tick();
            Tick();

            // Act
            s_lists.Select(1);
            Tick();
            Tick();

            // Assert — pinned near the first list, 300px left of the second, and not scaled.
            var card = Root.Q<VisualElement>("card");
            Assert.That((UnityEngine.Mathf.Abs(card.style.translate.value.x.value + 300f) < 50f, card.style.scale.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }

        private static string s_resizeOrigin;

        // Step 1 doubles the box in place, its top-left corner fixed; s_resizeOrigin is its transform origin.
        [Component]
        private static VNode ResizingBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var size = step == 0 ? 100 : 200;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "shared", layoutId: "shared-box", transition: s_layoutSpring,
                    className: $"left-[0px] top-[0px] w-[{size}px] h-[{size}px] {s_resizeOrigin}"),
            });
        }

        private VisualElement ResizeBoxWithOrigin(string origin)
        {
            s_resizeOrigin = origin;
            var mounted = V.Mount(Root, V.Component(ResizingBoxRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();
            return Root.Q<VisualElement>("shared");
        }

        [Test]
        public void Given_ALayoutIdMotionScaledAboutItsCentre_When_ItDoublesWithItsCornerFixed_Then_TheTweenStartsOverTheOldBox()
        {
            // Arrange / Act
            var element = ResizeBoxWithOrigin("");

            // Assert — half size, shifted up-left by a quarter of the new size so the old corner stays put.
            Assert.That((UnityEngine.Mathf.Abs(element.style.scale.value.value.x - 0.5f) < 0.01f,
                    UnityEngine.Mathf.Abs(element.style.translate.value.x.value + 50f) < 1f,
                    UnityEngine.Mathf.Abs(element.style.translate.value.y.value + 50f) < 1f),
                Is.EqualTo((true, true, true)));
        }

        // GREEN_ON_BASE(characterization): the base starts a corner-origin resize at the old corner with no translate.
        // Compensating the translate for a centre origin that the element does not have would move it.
        [Test]
        public void Given_ALayoutIdMotionScaledAboutItsCorner_When_ItDoublesWithThatCornerFixed_Then_TheTweenStartsWithNoTranslate()
        {
            // Arrange / Act
            var element = ResizeBoxWithOrigin("origin-[0%_0%]");

            // Assert — half size about the fixed corner.
            Assert.That((UnityEngine.Mathf.Abs(element.style.scale.value.value.x - 0.5f) < 0.01f, element.style.translate.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }

        // Step 1 moves the box out of an unscaled parent into one below it drawn at half scale.
        [Component]
        private static VNode IntoScaledParentBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Div(key: "first", className: "w-[400px] h-[200px]", children: step == 1 ? Array.Empty<VNode>() : new[] { SharedBox(200) }),
                V.Div(key: "second", className: "w-[400px] h-[200px] scale-[0.5]",
                    children: step == 1 ? new[] { SharedBox(200) } : Array.Empty<VNode>()),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionInAnUnscaledParent_When_ItMovesToAHalfScaleOne_Then_ItTweensFromTwiceItsSize()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(IntoScaledParentBoxRender, key: "root"));
            Tick();

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — twice its size in the half-scale parent is the size it was drawn at before.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That(UnityEngine.Mathf.Abs(replacement.style.scale.value.value.x - 2f), Is.LessThan(0.1f));
        }

        // Step 1 moves the outer layoutId Motion 200px right; step 2 flips the inner one's element type and
        // moves it 20px right inside the outer one, still under the same outer element.
        [Component]
        private static VNode OuterThenInnerFlipRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "outer",
                    layoutId: "outer-box",
                    transition: s_layoutSpring,
                    className: $"left-[{(step >= 1 ? 200 : 0)}px] top-[0px] w-[300px] h-[300px]",
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", layoutId: "inner-box", transition: s_layoutSpring,
                            elementType: step >= 2 ? typeof(Box) : null,
                            className: $"left-[{(step >= 2 ? 40 : 20)}px] top-[20px] w-[50px] h-[50px]"),
                    }),
            });
        }

        [Test]
        public void Given_AnOuterLayoutIdMotionStillTweening_When_TheInnerFlipsTypeAndMovesInsideIt_Then_TheInnerTweensOnlyItsOwnMove()
        {
            // Arrange — the outer one's tween is a few frames in.
            using var mounted = V.Mount(Root, V.Component(OuterThenInnerFlipRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 5; i++) Tick();
            var outer = Root.Q<VisualElement>("outer");

            // Act
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the outer one is still mid-tween, and the replacement tweens only the 20px it moved.
            var inner = Root.Q<VisualElement>("inner");
            Assert.That((outer.style.translate.value.x.value < -50f,
                    UnityEngine.Mathf.Abs(inner.style.translate.value.x.value + 20f) < 5f),
                Is.EqualTo((true, true)));
        }

        // Which Motions step 1 of GrowingOuterRender recreates by flipping their element type. Flipping the
        // outer one recreates the inner one with it, under the new outer element.
        private static bool s_flipOuter;
        private static bool s_flipInner;
        private static Vector2Int s_grownSize;

        // Step 1 grows the outer layoutId Motion from 300px square to s_grownSize, its corner fixed; the inner
        // one stands where it was inside it.
        [Component]
        private static VNode GrowingOuterRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var size = step == 0 ? new Vector2Int(300, 300) : s_grownSize;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "outer",
                    layoutId: "outer-box",
                    elementType: s_flipOuter && step != 0 ? typeof(Box) : null,
                    transition: s_layoutSpring,
                    className: $"left-[0px] top-[0px] w-[{size.x}px] h-[{size.y}px]",
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", layoutId: "inner-box", transition: s_layoutSpring,
                            elementType: s_flipInner && step != 0 ? typeof(Box) : null,
                            className: "left-[20px] top-[20px] w-[50px] h-[50px]"),
                    }),
            });
        }

        private MountedTree MountGrowingOuter(bool flipOuter, bool flipInner, int grownWidth = 600, int grownHeight = 600)
        {
            s_flipOuter = flipOuter;
            s_flipInner = flipInner;
            s_grownSize = new Vector2Int(grownWidth, grownHeight);
            var mounted = V.Mount(Root, V.Component(GrowingOuterRender, key: "root"));
            Tick();
            return mounted;
        }

        // Plays step 1 up to the frame its layout settles on.
        private void GrowTheOuter(MountedTree mounted)
        {
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();
        }

        // A point of an element's own box carried up to Root through each element's layout offset and its
        // inline translate and scale about its transform origin: where it is drawn while a layoutId tween owns
        // those slots.
        private Vector2 DrawnPoint(VisualElement element, Vector2 point)
        {
            for (var e = element; e != Root; e = e.hierarchy.parent)
            {
                Vector2 origin = e.resolvedStyle.transformOrigin;
                var translate = e.style.translate.keyword == StyleKeyword.Null
                    ? Vector2.zero
                    : new Vector2(e.style.translate.value.x.value, e.style.translate.value.y.value);
                var scale = e.style.scale.keyword == StyleKeyword.Null ? Vector2.one : (Vector2)e.style.scale.value.value;
                point = e.layout.position + translate + origin + Vector2.Scale(scale, point - origin);
            }
            return point;
        }

        private Rect DrawnBox(VisualElement element)
        {
            var min = DrawnPoint(element, Vector2.zero);
            return new Rect(min, DrawnPoint(element, element.layout.size) - min);
        }

        private static bool Near(Rect actual, Rect expected) =>
            Mathf.Abs(actual.x - expected.x) < 1f && Mathf.Abs(actual.y - expected.y) < 1f
            && Mathf.Abs(actual.width - expected.width) < 1f && Mathf.Abs(actual.height - expected.height) < 1f;

        [Test]
        public void Given_ALayoutIdMotionStandingInsideAGrowingOne_When_TheOuterStartsItsTween_Then_TheInnerIsDrawnOverItsOldBox()
        {
            // Arrange
            using var mounted = MountGrowingOuter(flipOuter: false, flipInner: false);

            // Act
            GrowTheOuter(mounted);

            // Assert — where it stood, at its own size, though the outer one is drawn at half its new size.
            Assert.That(Near(DrawnBox(Root.Q<VisualElement>("inner")), new Rect(20f, 20f, 50f, 50f)), Is.True);
        }

        [Test]
        public void Given_ALayoutIdMotionStandingInsideAGrowingOne_When_TheOuterTweenIsUnderWay_Then_TheInnerKeepsItsSizeAndItsPlaceInTheOuter()
        {
            // Arrange
            using var mounted = MountGrowingOuter(flipOuter: false, flipInner: false);
            GrowTheOuter(mounted);
            var outer = Root.Q<VisualElement>("outer");

            // Act
            for (var i = 0; i < 5; i++) Tick();

            // Assert — the outer one is still growing, and the inner one sits 20px inside its drawn corner at
            // its own 50px.
            var outerBox = DrawnBox(outer);
            Assert.That((outerBox.width < 550f,
                    Near(DrawnBox(Root.Q<VisualElement>("inner")), new Rect(outerBox.position + new Vector2(20f, 20f), new Vector2(50f, 50f)))),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ALayoutIdMotionStandingInsideOneWhoseHeightDoubles_When_TheOuterStartsItsTween_Then_TheInnerIsDrawnOverItsOldBox()
        {
            // Arrange
            using var mounted = MountGrowingOuter(flipOuter: false, flipInner: false, grownWidth: 300, grownHeight: 600);

            // Act
            GrowTheOuter(mounted);

            // Assert — where it stood, square, though the outer one is drawn squashed to half its new height.
            Assert.That(Near(DrawnBox(Root.Q<VisualElement>("inner")), new Rect(20f, 20f, 50f, 50f)), Is.True);
        }

        [Test]
        public void Given_ALayoutIdMotionInsideAGrowingOne_When_ItFlipsTypeWhereItStands_Then_ItIsDrawnOverItsOldBox()
        {
            // Arrange
            using var mounted = MountGrowingOuter(flipOuter: false, flipInner: true);
            var original = Root.Q<VisualElement>("inner");

            // Act
            GrowTheOuter(mounted);

            // Assert — a new element, where the old one stood and at its size.
            var replacement = Root.Q<VisualElement>("inner");
            Assert.That((ReferenceEquals(original, replacement), Near(DrawnBox(replacement), new Rect(20f, 20f, 50f, 50f))),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ALayoutIdMotionInsideAGrowingOne_When_TheOuterFlipsTypeWhileGrowing_Then_TheInnerIsDrawnOverItsOldBox()
        {
            // Arrange
            using var mounted = MountGrowingOuter(flipOuter: true, flipInner: false);
            var original = Root.Q<VisualElement>("inner");

            // Act
            GrowTheOuter(mounted);

            // Assert — recreated with the outer one, where it stood and at its size.
            var replacement = Root.Q<VisualElement>("inner");
            Assert.That((ReferenceEquals(original, replacement), Near(DrawnBox(replacement), new Rect(20f, 20f, 50f, 50f))),
                Is.EqualTo((false, true)));
        }

        // Set before a mount of GrowingOuterThenInnerRender: whether step 2 flips the inner layoutId Motion's
        // element type where it stands, and the classes of a second one step 2 mounts beside it, if any.
        private static bool s_flipInnerLate;
        private static string s_lateClasses;

        // Step 1 grows the outer layoutId Motion as GrowingOuterRender does; step 2 comes while it is still growing.
        [Component]
        private static VNode GrowingOuterThenInnerRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var size = step == 0 ? 300 : 600;
            var children = new List<VNode>
            {
                V.Motion(key: "inner", name: "inner", layoutId: "inner-box", transition: s_layoutSpring,
                    elementType: s_flipInnerLate && step == 2 ? typeof(Box) : null,
                    className: "left-[20px] top-[20px] w-[50px] h-[50px]"),
            };
            if (s_lateClasses != null && step == 2)
            {
                children.Add(V.Motion(key: "late", name: "late", layoutId: "late-box", transition: s_layoutSpring,
                    className: $"left-[20px] top-[40px] w-[50px] h-[50px] {s_lateClasses}"));
            }
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "outer", layoutId: "outer-box", transition: s_layoutSpring,
                    className: $"left-[0px] top-[0px] w-[{size}px] h-[{size}px]", children: children.ToArray()),
            });
        }

        // Mounts GrowingOuterThenInnerRender and plays step 1 and the four frames after it.
        private MountedTree GrowOuterPartWay(bool flipInnerLate, string lateClasses)
        {
            s_flipInnerLate = flipInnerLate;
            s_lateClasses = lateClasses;
            var mounted = V.Mount(Root, V.Component(GrowingOuterThenInnerRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 4; i++) Tick();
            return mounted;
        }

        [Test]
        public void Given_ALayoutIdMotionInsideOneStillGrowing_When_ItFlipsTypeWhereItStands_Then_ItIsDrawnOverItsOldBoxOnTheFrameItSettles()
        {
            // Arrange
            using var mounted = GrowOuterPartWay(flipInnerLate: true, lateClasses: null);
            var original = Root.Q<VisualElement>("inner");

            // Act
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert — a new element, 20px inside the outer one's drawn corner at its own 50px, though the outer
            // one is still drawn short of its layout.
            var outerBox = DrawnBox(Root.Q<VisualElement>("outer"));
            var replacement = Root.Q<VisualElement>("inner");
            Assert.That((ReferenceEquals(original, replacement), outerBox.width < 550f,
                    Near(DrawnBox(replacement), new Rect(outerBox.position + new Vector2(20f, 20f), new Vector2(50f, 50f)))),
                Is.EqualTo((false, true, true)));
        }

        [Test]
        public void Given_ALayoutIdMotionMountedInsideOneStillGrowing_When_TheStylesheetScalesIt_Then_ItIsDrawnAtThatScale()
        {
            // Arrange — the scale comes from the bundled stylesheet rather than an inline value.
            VelvetStyleUtilities.AttachTo(Root);
            using var mounted = GrowOuterPartWay(flipInnerLate: false, lateClasses: "scale-150");

            // Act — the frame it is first laid out on, and the next.
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();
            Tick();

            // Assert — its own one and a half, drawn inside an outer one whose scale its own undoes.
            var late = Root.Q<VisualElement>("late");
            var outer = Root.Q<VisualElement>("outer");
            Assert.That(late.style.scale.value.value.x * outer.style.scale.value.value.x, Is.EqualTo(1.5f).Within(0.01f));
        }

        private static readonly StyleTransitionConfig s_slowTween = new() { DurationSec = 1f, Easing = EasingMode.Linear };
        private static readonly StyleTransitionConfig s_quickTween = new() { DurationSec = 0.1f, Easing = EasingMode.Linear };

        // Stop 1 grows the outer layoutId Motion on a slow tween and moves the inner one 100px right inside it on
        // a quick one; a bump patches the inner one where it stands.
        [Component]
        private static VNode GrowingOuterBumpedInnerRender()
        {
            var (stop, setStop) = Hooks.UseState(0);
            var (bump, setBump) = Hooks.UseState(0);
            s_setStop = setStop;
            s_setBump = setBump;
            var size = stop == 0 ? 300 : 600;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "outer", layoutId: "outer-box", transition: s_slowTween,
                    className: $"left-[0px] top-[0px] w-[{size}px] h-[{size}px]",
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", layoutId: "inner-box", transition: s_quickTween,
                            className: $"left-[{20 + stop * 100}px] top-[20px] w-[50px] h-[50px] bump-{bump}"),
                    }),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionPatchedMidTweenInsideOneStillGrowing_When_ALaterLayoutChangeMovesIt_Then_ItDoesNotStartFromWhereItsTweenDrewItThen()
        {
            // Arrange — patched where it stands two frames into its move, which then ends while the outer one grows on.
            using var mounted = V.Mount(Root, V.Component(GrowingOuterBumpedInnerRender, key: "root"));
            Tick();
            s_setStop.Invoke(1);
            mounted.FlushStateForTest();
            Tick();
            Tick();
            Tick();
            s_setBump.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 9; i++) Tick();
            var inner = Root.Q<VisualElement>("inner");

            // Act — a layout change no render made.
            inner.style.marginLeft = 50f;
            Tick();

            // Assert — from its layout when patched, 120px inside the outer one's drawn corner, rather than from
            // where its move drew it then.
            var outerBox = DrawnBox(Root.Q<VisualElement>("outer"));
            Assert.That(outerBox.width < 550f ? DrawnBox(inner).x - outerBox.x : float.NaN, Is.EqualTo(120f).Within(1f));
        }

        // Step 1 moves the outer layoutId Motion 100px right and doubles it, and moves the card out of the tray
        // beside it into it, in the same render.
        [Component]
        private static VNode CardIntoMovingOuterRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            VNode[] Card() => new[]
            {
                V.Motion(key: "card", name: "card", layoutId: "card", transition: s_layoutSpring,
                    className: "left-[20px] top-[20px] w-[50px] h-[50px]"),
            };
            return V.Div(children: new VNode[]
            {
                V.Div(key: "tray", className: "left-[400px] top-[0px] w-[100px] h-[100px]",
                    children: step == 0 ? Card() : Array.Empty<VNode>()),
                V.Motion(key: "outer", name: "outer", layoutId: "outer-box", transition: s_layoutSpring,
                    className: $"left-[{step * 100}px] top-[0px] w-[{200 + step * 200}px] h-[{200 + step * 200}px]",
                    children: step == 0 ? Array.Empty<VNode>() : Card()),
            });
        }

        [Test]
        public void Given_ACardMovingIntoALayoutIdMotionThatMovesAndGrows_When_BothSettle_Then_TheCardIsDrawnOverItsOldBox()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(CardIntoMovingOuterRender, key: "root"));
            Tick();
            var before = DrawnBox(Root.Q<VisualElement>("card"));

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert
            Assert.That(Near(DrawnBox(Root.Q<VisualElement>("card")), before), Is.True);
        }

        // Stop 1 moves the inner layoutId Motion 100px right inside the outer one, which is patched where it stands
        // in the same render.
        [Component]
        private static VNode BumpedOuterRender()
        {
            var (stop, setStop) = Hooks.UseState(0);
            s_setStop = setStop;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "outer", layoutId: "outer-box", transition: s_layoutSpring,
                    className: $"left-[0px] top-[0px] w-[300px] h-[300px] bump-{stop}",
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", layoutId: "inner-box", transition: s_layoutSpring,
                            className: $"left-[{20 + stop * 100}px] top-[20px] w-[50px] h-[50px]"),
                    }),
            });
        }

        // GREEN_ON_BASE(characterization): the base keeps the wait a patch that moved nothing leaves past a move inside it.
        // Settling an ancestor that moved before its descendant must not settle one that did not.
        [Test]
        public void Given_ALayoutIdMotionPatchedWhereItStandsAsTheOneInsideItMoves_When_ALaterLayoutChangeMovesIt_Then_ItTweens()
        {
            // Arrange — the inner one's move played out.
            using var mounted = V.Mount(Root, V.Component(BumpedOuterRender, key: "root"));
            Tick();
            s_setStop.Invoke(1);
            mounted.FlushStateForTest();
            Tick();
            AdvancePast(3f);
            var outer = Root.Q<VisualElement>("outer");

            // Act — a layout change no render made.
            outer.style.marginLeft = 50f;
            Tick();

            // Assert — pinned near where it stood, 50px back.
            Assert.That(TranslateX(outer), Is.LessThan(-25f));
        }

        // Step 1 moves the box a pixel right and a pixel down, 500px right of the panel's corner and 400px below it.
        [Component]
        private static VNode FarPixelMoveRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "shared", layoutId: "shared-box", transition: s_layoutSpring,
                    className: $"left-[{500 + step}px] top-[{400 + step}px] w-[100px] h-[100px]"),
            });
        }

        // GREEN_ON_BASE(characterization): the base rests a one-pixel layoutId spring within a tenth of a pixel.
        // Scaling the rest threshold by the move's travel must take that travel from how far each edge moved.
        [Test]
        public void Given_ALayoutIdSpringMovingAPixelFarFromThePanelsCorner_When_AlmostASecondHasPassed_Then_ItHasEnded()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(FarPixelMoveRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();
            var element = Root.Q<VisualElement>("shared");
            var start = TranslateX(element);

            // Act — sixty frames.
            for (var i = 0; i < 60; i++) Tick();

            // Assert — it rests within a tenth of the one pixel it moves; had any edge's travel been taken as the
            // sum of its two positions, the spring would rest within a ten-thousandth and still be running.
            Assert.That((start, element.style.translate.keyword), Is.EqualTo((-1f, StyleKeyword.Null)));
        }

        // Step 1 grows a spacer above a plain parent by 100px, pushing it down, and flips the layoutId Motion's
        // element type where it stands inside that parent.
        [Component]
        private static VNode PushedParentFlipRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Div(key: "spacer", className: $"w-[10px] h-[{(step == 0 ? 0 : 100)}px]"),
                V.Div(key: "wrap", className: "w-[300px] h-[200px]", children: new VNode[]
                {
                    V.Motion(name: "shared", layoutId: "shared-box", transition: s_layoutSpring,
                        elementType: step == 0 ? null : typeof(Box),
                        className: "left-[20px] top-[20px] w-[50px] h-[50px]"),
                }),
            });
        }

        // GREEN_ON_BASE(characterization): the base leaves a Motion that flips type where it stands without a pose.
        // The box its teardown leaves must keep the parent-relative comparison under that same parent.
        [Test]
        public void Given_ALayoutIdMotionInAPlainParentPushedDown_When_ItFlipsTypeWhereItStandsInIt_Then_ItCarriesNoInversePose()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(PushedParentFlipRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — a new element with no inline translate, though the parent moved 100px down.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That((ReferenceEquals(original, replacement), replacement.style.translate.keyword),
                Is.EqualTo((false, StyleKeyword.Null)));
        }

        private static TabStore s_shared;

        // Grows by 100px when the second slot is selected, pushing the shared parent below it down.
        [Component]
        private static VNode SharedParentSpacerRender() =>
            V.Div(className: $"w-[10px] h-[{(Hooks.UseStore(s_shared, s => s.Index) == 1 ? 100 : 0)}px]");

        // Renders a pooled Label and the card while the first slot is selected; both render straight into the
        // shared parent, having no element of their own. The Label comes first: the slot's rows are removed
        // from the last, so the Label goes back to the pool after the card's teardown has left its box.
        [Component]
        private static VNode FirstSharedSlotRender() => Hooks.UseStore(s_shared, s => s.Index) == 0
            ? V.Fragment(children: new VNode[]
            {
                V.Label(text: "pooled when the slot empties"),
                V.Motion(name: "card", layoutId: "card", transition: s_layoutSpring, className: "w-[100px] h-[50px]"),
            })
            : null;

        [Component]
        private static VNode SecondSharedSlotRender() => Hooks.UseStore(s_shared, s => s.Index) == 1
            ? V.Motion(name: "card", layoutId: "card", transition: s_layoutSpring, className: "w-[100px] h-[50px]")
            : null;

        [Component]
        private static VNode SharedParentSlotsRender() => V.Div(children: new VNode[]
        {
            V.Component(SharedParentSpacerRender, key: "spacer"),
            V.Div(key: "shared", className: "w-[300px] h-[200px]", children: new VNode[]
            {
                V.Component(FirstSharedSlotRender, key: "first"),
                V.Component(SecondSharedSlotRender, key: "second"),
            }),
        });

        // GREEN_ON_BASE(characterization): the base compares a layoutId Motion's rects within the one parent it stays in.
        // Another element the pool takes back meanwhile must not strip the kept box of that parent.
        [Test]
        public void Given_TwoComponentsRenderingIntoOneParentPushedDown_When_TheCardMovesFromTheFirstToTheSecond_Then_ItCarriesNoInversePose()
        {
            // Arrange
            s_shared = new TabStore(0);
            using var mounted = V.Mount(Root, V.Component(SharedParentSlotsRender, key: "root"));
            Tick();
            Tick();
            var original = Root.Q<VisualElement>("card");

            // Act
            s_shared.Select(1);
            Tick();
            Tick();

            // Assert — a new element standing where the old one stood inside the parent, with no inline translate.
            var card = Root.Q<VisualElement>("card");
            Assert.That((ReferenceEquals(original, card), card.layout.y, card.style.translate.keyword),
                Is.EqualTo((false, 0f, StyleKeyword.Null)));
        }

        private static StateUpdater<int> s_setStop;

        // Three stops along x, transition-transform so the tween takes a transition suspension.
        [Component]
        private static VNode ThreeStopBoxRender()
        {
            var (stop, setStop) = Hooks.UseState(0);
            s_setStop = setStop;
            return V.Div(children: new VNode[]
            {
                V.Motion(
                    name: "shared",
                    layoutId: "shared-box",
                    transition: new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f },
                    className: $"absolute left-[{stop * 200}px] top-[0px] w-[100px] h-[100px] transition-transform"),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionWhoseTweenSettled_When_ItMovesAgain_Then_TheInverseTranslateAppliesAgain()
        {
            // Arrange — one move, played out to rest.
            using var mounted = V.Mount(Root, V.Component(SharedBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();
            AdvancePast(2f);

            // Act — move back to where it started.
            s_setMoved.Invoke(false);
            mounted.FlushStateForTest();
            Tick();

            // Assert — pinned near its old screen position again, 200px to the right of the new one.
            Assert.That(element.style.translate.value.x.value, Is.GreaterThan(50f));
        }

        // GREEN_ON_BASE(characterization): the base already stops a departing element's tween at teardown.
        // The tick's new bookkeeping must go on doing so.
        [Test]
        public void Given_ALayoutIdMotionMidTween_When_ItIsTornDown_Then_ItsTweenStopsWritingToIt()
        {
            // Arrange
            var mounted = V.Mount(Root, V.Component(SharedBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();
            mounted.Dispose();
            var atTeardown = element.style.translate.value.x.value;

            // Act
            Tick();
            Tick();

            // Assert — nothing has written a later frame.
            Assert.That(element.style.translate.value.x.value, Is.EqualTo(atTeardown));
        }

        [Test]
        public void Given_ALayoutIdTweenSupersededMidFlight_When_TheLastTweenSettles_Then_TheElementsTransitionsAreHandedBack()
        {
            // Arrange — three moves, each landing while the tween before it is still in flight.
            using var mounted = V.Mount(Root, V.Component(ThreeStopBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            for (var stop = 1; stop <= 3; stop++)
            {
                s_setStop.Invoke(stop);
                mounted.FlushStateForTest();
                Tick();
                Tick();
            }

            // Act
            AdvancePast(3f);

            // Assert — every tween has settled and both inline slots are back with the classes.
            Assert.That((element.style.translate.keyword, element.style.transitionProperty.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ALayoutIdMotion_When_OnlyItsHeightDoubles_Then_ItStartsAtItsOldHeightAndItsOwnWidth()
        {
            // Arrange
            s_ownClasses = "";
            using var mounted = V.Mount(Root, V.Component(TallerBoxRender, key: "root"));
            Tick();

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the width untouched and the height halved, not both averaged.
            var element = Root.Q<VisualElement>("shared");
            Assert.That((Vector2)element.style.scale.value.value, Is.EqualTo(new Vector2(1f, 0.5f)));
        }

        // Step 1 doubles the box's height in place, its top edge fixed.
        [Component]
        private static VNode TallerBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "shared", layoutId: "shared-box", transition: s_layoutSpring,
                    className: $"left-[0px] top-[0px] w-[100px] h-[{(step == 0 ? 100 : 200)}px] {s_ownClasses}"),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionScaledByItsOwnClass_When_ItDoublesInPlace_Then_TheTweenStartsAtTheInverseTimesItsOwnScale()
        {
            // Arrange / Act
            var element = ResizeBoxWithOrigin("scale-[1.5]");

            // Assert — half of its own one and a half, so it is drawn at the size it was drawn at before.
            Assert.That(Mathf.Abs(element.style.scale.value.value.x - 0.75f), Is.LessThan(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base hands a settled tween's scale slot back to the element's own class.
        // Composing the tween with that class must not leave the composite behind.
        [Test]
        public void Given_ALayoutIdMotionScaledByItsOwnClass_When_ItsTweenSettles_Then_ItsOwnScaleIsInTheSlot()
        {
            // Arrange
            var element = ResizeBoxWithOrigin("scale-[1.5]");

            // Act
            AdvancePast(3f);

            // Assert
            Assert.That(element.style.scale.value.value.x, Is.EqualTo(1.5f).Within(1e-4f));
        }

        [Test]
        public void Given_ALayoutIdMotionTranslatedByItsOwnClass_When_ItMoves_Then_TheTweenStartsWhereItWasDrawn()
        {
            // Arrange — the box is drawn 30px right of its layout by its own class, at 30 before the move.
            s_ownClasses = "translate-x-[30px]";
            using var mounted = V.Mount(Root, V.Component(OwnTranslateBoxRender, key: "root"));
            Tick();

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the layout moved 200px right, so the slot holds its own 30px less 200.
            var element = Root.Q<VisualElement>("shared");
            Assert.That(Mathf.Abs(element.style.translate.value.x.value + 170f), Is.LessThan(1f));
        }

        [Test]
        public void Given_ALayoutIdMotionTranslatedByAStylesheetClass_When_ItMoves_Then_TheTweenAddsToThatTranslate()
        {
            // Arrange — anim-slide-up-exit-to translates the box 20px up from the bundled stylesheet rather than inline.
            VelvetStyleUtilities.AttachTo(Root);
            s_ownClasses = "anim-slide-up-exit-to";
            using var mounted = V.Mount(Root, V.Component(OwnTranslateBoxRender, key: "root"));
            Tick();

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — its own 20px up, and the 200px back to where it stood.
            var translate = Root.Q<VisualElement>("shared").style.translate.value;
            Assert.That(new[] { translate.x.value, translate.y.value }, Is.EqualTo(new[] { -200f, -20f }).Within(1f));
        }

        private static string s_ownClasses;

        // Step 1 moves the box 200px right; s_ownClasses are its own transform classes.
        [Component]
        private static VNode OwnTranslateBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "shared", layoutId: "shared-box", transition: s_layoutSpring,
                    className: $"left-[{step * 200}px] top-[0px] w-[100px] h-[100px] {s_ownClasses}"),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionMidTween_When_ItMovesAgain_Then_TheNewTweenStartsWhereItIsDrawn()
        {
            // Arrange — the first move is a few frames in, the box drawn somewhere between its two stops.
            using var mounted = V.Mount(Root, V.Component(ThreeStopBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            s_setStop.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 6; i++) Tick();
            var drawnBefore = DrawnBox(element).x;

            // Act
            s_setStop.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the first stop is 200px behind the second, the box is drawn short of it, and the new tween
            // starts from where it is drawn rather than from the first stop.
            Assert.That((drawnBefore < 150f, Mathf.Abs(DrawnBox(element).x - drawnBefore) < 1f), Is.EqualTo((true, true)));
        }

        // Step 1 moves the box out of a parent it leaves behind for good into a new one.
        [Component]
        private static VNode ReplacedParentBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                step == 0
                    ? V.Div(key: "left", name: "left", className: "w-[300px] h-[200px]", children: new[] { SharedBox(0) })
                    : V.Div(key: "right", name: "right", className: "left-[300px] w-[300px] h-[200px]", children: new[] { SharedBox(0) }),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionThatLeftARemovedParent_When_ItsTweenStarts_Then_ItsIdNoLongerHoldsThatParent()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(ReplacedParentBoxRender, key: "root"));
            Tick();
            var removedParent = Root.Q<VisualElement>("left");

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — the old parent is out of the tree, and nothing the id holds refers to it.
            var heldParent = mounted.Root.Reconciler.Context.LayoutIdRegistry.TryGetValue("shared-box", out var entry)
                ? entry.Box?.Parent
                : null;
            Assert.That((removedParent.panel, ReferenceEquals(heldParent, removedParent)),
                Is.EqualTo(((IPanel)null, false)));
        }

        // A board stretched to twice its width holding a column rotated 45 degrees: the column's axes are drawn
        // sheared, so the box in it is drawn as a rhombus. Step 1 moves the box out into a plain parent.
        [Component]
        private static VNode ShearedFrameBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Div(key: "board", className: "w-[200px] h-[200px] scale-x-[2]", children: new VNode[]
                {
                    V.Div(key: "column", className: "w-[200px] h-[200px] rotate-[45deg]",
                        children: step == 0 ? new[] { SharedBox(50) } : Array.Empty<VNode>()),
                }),
                V.Div(key: "plain", className: "w-[200px] h-[200px]", children: step == 0 ? Array.Empty<VNode>() : new[] { SharedBox(50) }),
            });
        }

        // GREEN_ON_BASE(characterization): the base starts a box leaving a sheared frame at its sides' drawn lengths.
        // No uniform scale reproduces a rhombus, and Documentation~/motion.md states which one is taken.
        [Test]
        public void Given_ALayoutIdMotionInAShearedFrame_When_ItMovesToAPlainParent_Then_ItStartsAtTheDrawnLengthOfItsSides()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(ShearedFrameBoxRender, key: "root"));
            Tick();

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — each side of the unit square is drawn sqrt(2^2 cos^2 45 + sin^2 45) = sqrt(2.5) long.
            var replacement = Root.Q<VisualElement>("shared");
            Assert.That(Mathf.Abs(replacement.style.scale.value.value.x - Mathf.Sqrt(2.5f)), Is.LessThan(0.01f));
        }

        private static StyleTransitionConfig s_moveTransition;
        private static float? s_moveDuration;
        private static EasingMode? s_moveEasing;
        private static float? s_moveDelay;

        // Step 1 moves the box 200px right on s_moveTransition and V.Motion's timing parameters.
        [Component]
        private static VNode TimedMoveBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "shared", layoutId: "shared-box", transition: s_moveTransition,
                    duration: s_moveDuration, easing: s_moveEasing, delay: s_moveDelay,
                    className: $"absolute left-[{step * 200}px] top-[0px] w-[100px] h-[100px]"),
            });
        }

        private MountedTree _moveMount;

        // Mounts TimedMoveBoxRender on the given timing and plays step 1 up to the frame its layout settles on.
        private VisualElement MoveOn(StyleTransitionConfig transition, float? duration = null, EasingMode? easing = null, float? delay = null)
        {
            s_moveTransition = transition;
            s_moveDuration = duration;
            s_moveEasing = easing;
            s_moveDelay = delay;
            _moveMount = V.Mount(Root, V.Component(TimedMoveBoxRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            _moveMount.FlushStateForTest();
            Tick();
            return Root.Q<VisualElement>("shared");
        }

        private static float TranslateX(VisualElement element) =>
            element.style.translate.keyword == StyleKeyword.Null ? 0f : element.style.translate.value.x.value;

        [Test]
        public void Given_ALayoutIdMotionMidTween_When_ItMovesAgainOnAZeroDurationTransition_Then_ItLandsAtOnce()
        {
            // Arrange
            var element = MoveOn(s_layoutSpring);
            for (var i = 0; i < 3; i++) Tick();

            // Act — the second move 200px further, on no duration at all.
            s_moveTransition = StyleTransitionConfig.None;
            s_setStep.Invoke(2);
            _moveMount.FlushStateForTest();
            Tick();

            // Assert — at its new box, the running tween no longer drawing it back.
            Assert.That(TranslateX(element), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void Given_ALayoutIdMotionWithAZeroDurationTransition_When_ItMoves_Then_ItLandsAtOnce()
        {
            // Arrange / Act
            var element = MoveOn(StyleTransitionConfig.None);

            // Assert
            Assert.That(element.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALayoutIdMotionWithATweenTransition_When_ItsDurationHasPassed_Then_TheTweenHasEnded()
        {
            // Arrange — pinned at the old box on the frame the layout settles.
            var element = MoveOn(new StyleTransitionConfig { DurationSec = 0.2f });
            var start = TranslateX(element);

            // Act — past 0.2s, far short of the time a spring on the config's default knobs needs to settle.
            for (var i = 0; i < 20; i++) Tick();

            // Assert
            Assert.That((start < -150f, element.style.translate.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ALayoutIdMotionWithADelayedTween_When_LessThanTheDelayHasPassed_Then_ItHoldsTheOldBox()
        {
            // Arrange
            var element = MoveOn(new StyleTransitionConfig { DurationSec = 0.2f, DelaySec = 0.3f });

            // Act
            for (var i = 0; i < 6; i++) Tick();

            // Assert
            Assert.That(TranslateX(element), Is.EqualTo(-200f).Within(0.01f));
        }

        [Test]
        public void Given_ALayoutIdMotionWithALinearBezierTransition_When_HalfItsDurationHasPassed_Then_ItIsHalfWay()
        {
            // Arrange
            var element = MoveOn(new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 0.32f, BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
            });

            // Act — ten 16ms frames.
            for (var i = 0; i < 10; i++) Tick();

            // Assert — half of the 200px left, give or take a frame.
            Assert.That(TranslateX(element), Is.EqualTo(-100f).Within(15f));
        }

        [Test]
        public void Given_ALayoutIdMotionWithABezierTransitionTheValidatorRejects_When_ItMoves_Then_ItLandsAtOnce()
        {
            // Arrange / Act — a playable duration, and a control point that is not a number.
            var element = MoveOn(new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 0.32f, BezierX1 = float.NaN, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
            });

            // Assert
            Assert.That(element.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALayoutIdMotionWithNoTransition_When_FramersDefaultDurationHasPassed_Then_TheTweenHasEnded()
        {
            // Arrange
            var element = MoveOn(null);
            var start = TranslateX(element);

            // Act — past the 0.45s default tween.
            for (var i = 0; i < 34; i++) Tick();

            // Assert — pinned at first, done by now; a spring on StyleTransitionConfig's defaults is still moving.
            Assert.That((start < -150f, element.style.translate.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ALayoutIdMotionGivenOnlyADuration_When_ThatDurationHasPassed_Then_TheTweenHasEnded()
        {
            // Arrange — V.Motion's duration parameter without a transition times the Fade preset it defaults to.
            var element = MoveOn(null, duration: 0.1f);

            // Act — about 0.19s, inside Framer's 0.45s default.
            for (var i = 0; i < 12; i++) Tick();

            // Assert
            Assert.That(element.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALayoutIdMotionGivenOnlyAnEasing_When_ThePresetsDurationHasPassed_Then_TheTweenHasEnded()
        {
            // Arrange
            var element = MoveOn(null, easing: EasingMode.Linear);

            // Act — about 0.24s: past the Fade preset's 0.2s, inside Framer's 0.45s default.
            for (var i = 0; i < 15; i++) Tick();

            // Assert
            Assert.That(element.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALayoutIdMotionGivenOnlyADelay_When_LessThanTheDelayHasPassed_Then_ItHoldsTheOldBox()
        {
            // Arrange
            var element = MoveOn(null, delay: 0.3f);

            // Act
            for (var i = 0; i < 6; i++) Tick();

            // Assert
            Assert.That(TranslateX(element), Is.EqualTo(-200f).Within(0.01f));
        }

        [Test]
        public void Given_ALayoutIdMotionGivenNoTiming_When_TheFadePresetsDurationHasPassed_Then_ItIsStillOnFramersDefaultCurve()
        {
            // Arrange
            var element = MoveOn(null);

            // Act — about 0.24s: past the Fade preset's 0.2s, a little over half of Framer's 0.45s.
            for (var i = 0; i < 15; i++) Tick();

            // Assert — about 29px short on Framer's curve; the Fade preset has landed, and a spring on
            // 100/10/1 is within a few pixels of its box.
            Assert.That(TranslateX(element), Is.LessThan(-15f));
        }

        [Test]
        public void Given_ALayoutIdMotionOnAnEaseInCubicTween_When_HalfItsDurationHasPassed_Then_ItHasCoveredAnEighthOfTheWay()
        {
            // Arrange
            var element = MoveOn(new StyleTransitionConfig { DurationSec = 0.32f, Easing = EasingMode.EaseInCubic });

            // Act — ten 16ms frames.
            for (var i = 0; i < 10; i++) Tick();

            // Assert — an eighth of the 200px covered, give or take a frame; a linear curve would be half way.
            Assert.That(TranslateX(element), Is.LessThan(-150f));
        }

        [Test]
        public void Given_ALayoutIdMotionOnADelayedSpring_When_LessThanTheDelayHasPassed_Then_ItHoldsTheOldBox()
        {
            // Arrange
            var element = MoveOn(new StyleTransitionConfig
            {
                Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f, DelaySec = 0.3f,
            });

            // Act
            for (var i = 0; i < 6; i++) Tick();

            // Assert
            Assert.That(TranslateX(element), Is.EqualTo(-200f).Within(0.01f));
        }

        [Test]
        public void Given_ALayoutIdMotionOnATweenOneFrameLong_When_OneFrameHasPassed_Then_TheTweenHasEnded()
        {
            // Arrange
            var element = MoveOn(new StyleTransitionConfig { DurationSec = 0.016f });

            // Act
            Tick();

            // Assert
            Assert.That(element.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALayoutIdMotionMidTween_When_SomethingElseWritesItsTranslate_Then_TheTweenComposesWithThatValue()
        {
            // Arrange
            var element = MoveOn(s_layoutSpring);
            Tick();

            // Act — a drag, say, writes the slot.
            element.style.translate = new Translate(1000f, 0f);
            Tick();

            // Assert — the 1000px plus a tween still within 200px of its box.
            Assert.That(TranslateX(element), Is.GreaterThan(500f));
        }

        [Test]
        public void Given_ALayoutIdMotionWhoseTranslateSomethingElseWroteMidTween_When_TheTweenEnds_Then_TheSlotHoldsThatValue()
        {
            // Arrange
            var element = MoveOn(s_layoutSpring);
            Tick();
            element.style.translate = new Translate(30f, 0f);

            // Act
            AdvancePast(3f);

            // Assert
            Assert.That(TranslateX(element), Is.EqualTo(30f).Within(0.01f));
        }

        [Test]
        public void Given_ALayoutIdMotionMidResize_When_SomethingElseWritesItsScale_Then_TheTweenComposesWithThatValue()
        {
            // Arrange — the box doubles about its corner, so the tween writes only the scale.
            var element = ResizeBoxWithOrigin("origin-[0%_0%]");

            // Act
            element.style.scale = new Scale(new Vector3(3f, 3f, 1f));
            Tick();

            // Assert — three times a tween scale of at least a half.
            Assert.That(element.style.scale.value.value.x, Is.GreaterThan(1.2f));
        }

        // GREEN_ON_BASE(characterization): the base suspends an element's transitions for its tween whichever slots it writes.
        // Taking the suspension slot by slot must still take it for a tween that writes the scale alone.
        [Test]
        public void Given_ALayoutIdMotionThatTransitionsItsTransform_When_ItResizesAboutItsCorner_Then_ItsTransitionsAreSuspended()
        {
            // Arrange / Act — the tween writes only the scale.
            var element = ResizeBoxWithOrigin("origin-[0%_0%] transition-transform");

            // Assert
            Assert.That(element.style.transitionProperty.keyword, Is.EqualTo(StyleKeyword.Undefined));
        }

        [Test]
        public void Given_ALayoutIdMotionScaledByAStylesheetClass_When_ItDoublesInPlace_Then_TheTweenStartsAtTheInverseTimesThatScale()
        {
            // Arrange — the scale comes from the bundled stylesheet rather than an inline value.
            VelvetStyleUtilities.AttachTo(Root);

            // Act
            var element = ResizeBoxWithOrigin("scale-150");

            // Assert
            Assert.That(element.style.scale.value.value.x, Is.EqualTo(0.75f).Within(0.01f));
        }

        // Step 1 moves a 200px box 200px right; its own classes translate it by half its width and a quarter
        // of its height.
        [Component]
        private static VNode PercentTranslateBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "shared", layoutId: "shared-box", transition: s_layoutSpring,
                    className: $"left-[{step * 200}px] top-[0px] w-[200px] h-[200px] translate-x-1/2 translate-y-1/4"),
            });
        }

        [Test]
        public void Given_ALayoutIdMotionTranslatedByAFractionOfItsSize_When_ItMoves_Then_TheTweenAddsToThatFractionInPixels()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(PercentTranslateBoxRender, key: "root"));
            Tick();

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — its own 100px and 50px, less the 200px the layout moved.
            var element = Root.Q<VisualElement>("shared");
            var translate = element.style.translate.value;
            Assert.That(new[] { translate.x.value, translate.y.value }, Is.EqualTo(new[] { -100f, 50f }).Within(1f));
        }

        [Test]
        public void Given_ALayoutIdMotionMidTween_When_ItMovesAgain_Then_ItsFrameStepsTheNewTweenOncePerFrame()
        {
            // Arrange — the first move's frame is already running when the second starts.
            var element = MoveOn(new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 0.32f, BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
            });
            s_setStep.Invoke(2);
            _moveMount.FlushStateForTest();
            Tick();

            // Act — ten 16ms frames, half of the second tween.
            for (var i = 0; i < 10; i++) Tick();

            // Assert — about half of the 400px left; stepped twice a frame it would have landed.
            Assert.That(TranslateX(element), Is.LessThan(-100f));
        }

        // GREEN_ON_BASE(characterization): the base runs every move's tween past its first frame.
        // Ending projections through a list kept across frames must not end a later one.
        [Test]
        public void Given_ALayoutIdMotionWhoseTweenEnded_When_ItMovesAgainAndAFramePasses_Then_ItIsStillTweening()
        {
            // Arrange
            var element = MoveOn(s_layoutSpring);
            AdvancePast(3f);
            s_setStep.Invoke(2);
            _moveMount.FlushStateForTest();
            Tick();

            // Act
            Tick();

            // Assert
            Assert.That(TranslateX(element), Is.LessThan(-50f));
        }

        // GREEN_ON_BASE(characterization): the base stops a torn-down element's tween.
        // Projections kept by element must not follow it into the pool.
        [Test]
        public void Given_ALayoutIdMotionRemovedMidTween_When_ThePoolHandsItsElementToAnotherMotion_Then_TheTweenDoesNotWriteToIt()
        {
            // Arrange — the first move is tweening when the Motion is removed.
            using var mounted = V.Mount(Root, V.Component(PooledMotionReuseRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("shared");
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();
            Tick();
            s_setStep.Invoke(3);
            mounted.FlushStateForTest();

            // Act
            s_setStep.Invoke(4);
            mounted.FlushStateForTest();
            Tick();

            // Assert
            var reused = Root.Q<VisualElement>("reused");
            Assert.That((ReferenceEquals(original, reused), reused.style.translate.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ALayoutIdMotionMidTween_When_ItLeavesThePanelWithoutBeingUnmounted_Then_ItsTranslateIsHandedBack()
        {
            // Arrange
            var element = MoveOn(s_layoutSpring);
            Tick();

            // Act
            element.RemoveFromHierarchy();
            Tick();

            // Assert
            Assert.That(element.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALayoutIdMotionThatLeftThePanelMidTween_When_SomethingElseWritesItsTranslate_Then_TheSlotKeepsThatValue()
        {
            // Arrange
            var element = MoveOn(s_layoutSpring);
            Tick();
            element.RemoveFromHierarchy();

            // Act
            element.style.translate = new Translate(1000f, 0f);
            Tick();

            // Assert
            Assert.That(TranslateX(element), Is.EqualTo(1000f).Within(0.01f));
        }

        // "a" holds the id at the left; step 1 moves "b" 50px right and gives it that id.
        [Component]
        private static VNode TakenIdRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(key: "a", name: "a", layoutId: "card", transition: s_layoutSpring,
                    className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]"),
                V.Motion(key: "b", name: "b", layoutId: step == 0 ? null : "card", transition: s_layoutSpring,
                    className: $"absolute left-[{(step == 0 ? 300 : 350)}px] top-[0px] w-[100px] h-[100px]"),
            });
        }

        // GREEN_ON_BASE(characterization): the base tweens a Motion that takes an id from the box its holder stands at.
        // Reading a Motion's own box again where it settles must not reach a box it read off another.
        [Test]
        public void Given_ALiveLayoutIdMotionHoldingAnId_When_AnotherTakesItAndMoves_Then_TheTakerTweensFromTheHoldersBox()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(TakenIdRender, key: "root"));
            Tick();

            // Act
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            Tick();

            // Assert — from "a" at 0, 350px back; from its own box it would be 50px back.
            Assert.That(TranslateX(Root.Q<VisualElement>("b")), Is.LessThan(-200f));
        }

        private static StateUpdater<int> s_setBump;
        private static StyleTransitionConfig s_bumpTransition;

        // Stop 1 moves the box 200px right; a bump patches it where it stands.
        [Component]
        private static VNode BumpedBoxRender()
        {
            var (stop, setStop) = Hooks.UseState(0);
            var (bump, setBump) = Hooks.UseState(0);
            s_setStop = setStop;
            s_setBump = setBump;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "shared", layoutId: "shared-box", transition: s_bumpTransition,
                    className: $"absolute left-[{stop * 200}px] top-[0px] w-[100px] h-[100px] bump-{bump}"),
            });
        }

        // GREEN_ON_BASE(characterization): the base's wait holds the box the patch left, where the element stands once its tween ends.
        // A box read off the tween's drawing must not outlive that tween.
        [Test]
        public void Given_ALayoutIdMotionPatchedMidTweenWithoutMoving_When_ALaterLayoutChangeMovesIt_Then_ItDoesNotStartFromWhereTheTweenDrewItThen()
        {
            // Arrange — patched where it stands a few frames into a move, the move then played out.
            s_bumpTransition = s_layoutSpring;
            using var mounted = V.Mount(Root, V.Component(BumpedBoxRender, key: "root"));
            Tick();
            var element = Root.Q<VisualElement>("shared");
            s_setStop.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 4; i++) Tick();
            s_setBump.Invoke(1);
            mounted.FlushStateForTest();
            Tick();
            AdvancePast(3f);

            // Act — a layout change no render made.
            element.style.marginLeft = 50f;
            Tick();

            // Assert — not from about 200px back, where the tween drew it when it was patched.
            Assert.That(TranslateX(element), Is.GreaterThan(-100f));
        }

        // GREEN_ON_BASE(characterization): the base validates nothing a patch hands it.
        // Validating the layout transition must not come to warn on every render.
        [Test]
        public void Given_ALayoutIdMotionOnADurationTheSchedulerRejects_When_ItRerendersWithoutMoving_Then_NothingWarns()
        {
            // Arrange
            var warnings = 0;
            void Count(string message, string stack, LogType type)
            {
                if (message.Contains("Invalid DurationSec")) warnings++;
            }
            s_bumpTransition = new StyleTransitionConfig { DurationSec = 11f };
            using var mounted = V.Mount(Root, V.Component(BumpedBoxRender, key: "root"));
            Tick();
            Application.logMessageReceived += Count;

            // Act
            try
            {
                for (var bump = 1; bump <= 2; bump++)
                {
                    s_setBump.Invoke(bump);
                    mounted.FlushStateForTest();
                    Tick();
                }
            }
            finally
            {
                Application.logMessageReceived -= Count;
            }

            // Assert
            Assert.That(warnings, Is.Zero);
        }

        private static VisualElement s_otherRoot;

        // Step 1 moves two boxes 200px right: "here" on this panel, and "there" portalled onto s_otherRoot's.
        [Component]
        private static VNode TwoPanelBoxRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            VNode Box(string id) => V.Motion(key: id, name: id, layoutId: id, transition: s_layoutSpring,
                className: $"absolute left-[{step * 200}px] top-[0px] w-[100px] h-[100px]");
            return V.Div(children: new VNode[] { Box("here"), V.Portal(s_otherRoot, new[] { Box("there") }, key: "portal") });
        }

        // GREEN_ON_BASE(characterization): the base steps each tween by a tick of its own on its element's panel.
        // One frame per panel must step and end only its own panel's projections.
        [Test]
        public void Given_LayoutIdMotionsMovingAlikeOnTwoPanels_When_BothPanelsRunTheirFrames_Then_EachMovesAtItsOwnPace()
        {
            // Arrange
            using var other = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            s_otherRoot = other.rootVisualElement;
            using var mounted = V.Mount(Root, V.Component(TwoPanelBoxRender, key: "root"));
            Tick();
            other.FrameUpdateMs(16);
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();

            // Act — each settles in the first round of both panels' frames and is stepped in the next two.
            for (var i = 0; i < 3; i++)
            {
                Tick();
                other.FrameUpdateMs(16);
            }

            // Assert — each is still drawn well back of its box. Stepped by both panels' frames, each would have
            // run twice as long; ended by either panel's frame, each would be back at its box.
            var here = TranslateX(Root.Q<VisualElement>("here"));
            var there = TranslateX(other.rootVisualElement.Q<VisualElement>("there"));
            Assert.That(new[] { here, there }, Is.All.LessThan(-140f));
        }

        // Set before each render of SharedLiveIdRender: "a" stands at s_aLeft under s_aId while s_aMounted, and
        // "b" stands at s_bLeft under "card", after it, on s_bTransition, while s_bMounted. Both carry
        // s_sharedClasses.
        private static string s_aId;
        private static int s_aLeft;
        private static bool s_aMounted;
        private static bool s_bMounted;
        private static int s_bLeft;
        private static StyleTransitionConfig s_bTransition;
        private static string s_sharedClasses;
        private static int s_sharedRenders;
        // "c" stands 600px right under "card" on s_cTransition while s_cMounted.
        private static bool s_cMounted;
        private static StyleTransitionConfig s_cTransition;
        // Classes "a" and "b" carry of their own; "a" holds a plain child, "a-child".
        private static string s_aClasses;
        private static string s_bClasses;
        // The pose "a" takes of s_fade, none while null, on s_aTransition in place of s_slowTween while set.
        private static string s_aPose;
        private static StyleTransitionConfig s_aTransition;
        // A TextField "field" inside "a", and one "loose" outside every holder.
        private static bool s_aField;
        private static bool s_looseField;

        [Component]
        private static VNode SharedLiveIdRender()
        {
            var (_, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var children = new List<VNode>();
            if (s_aMounted)
            {
                children.Add(V.Motion(key: "a", name: "a", layoutId: s_aId, transition: s_aTransition ?? s_slowTween,
                    variants: s_aPose != null ? s_fade : null, animate: s_aPose,
                    className: $"absolute left-[{s_aLeft}px] top-[0px] w-[100px] h-[100px] {s_sharedClasses} {s_aClasses}",
                    children: s_aField
                        ? new VNode[] { V.Div(key: "a-child", name: "a-child", className: "w-[20px] h-[20px]"), V.TextField(key: "field", name: "field") }
                        : new VNode[] { V.Div(key: "a-child", name: "a-child", className: "w-[20px] h-[20px]") }));
            }
            if (s_bMounted)
            {
                children.Add(V.Motion(key: "b", name: "b", layoutId: "card", transition: s_bTransition,
                    className: $"absolute left-[{s_bLeft}px] top-[0px] w-[100px] h-[100px] {s_sharedClasses} {s_bClasses}"));
            }
            if (s_cMounted)
            {
                children.Add(V.Motion(key: "c", name: "c", layoutId: "card", transition: s_cTransition,
                    className: "absolute left-[600px] top-[0px] w-[100px] h-[100px]"));
            }
            if (s_looseField)
            {
                children.Add(V.TextField(key: "loose", name: "loose"));
            }
            return V.Div(children: children.ToArray());
        }

        // Mounts "a" alone under "card", then "b" 300px right under the same id on a one-second linear tween, and
        // plays the frame after.
        private MountedTree MountBOverA()
        {
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bLeft, s_bTransition, s_sharedClasses) = (300, s_bTransition ?? s_slowTween, s_sharedClasses ?? "");
            var mounted = MountAAlone();
            s_bMounted = true;
            RenderShared(mounted);
            return mounted;
        }

        private MountedTree MountAAlone()
        {
            var mounted = V.Mount(Root, V.Component(SharedLiveIdRender, key: "root"));
            Tick();
            return mounted;
        }

        // Renders SharedLiveIdRender from the statics as they stand and plays the frame after.
        private void RenderShared(MountedTree mounted)
        {
            s_setStep.Invoke(++s_sharedRenders);
            mounted.FlushStateForTest();
            Tick();
        }

        // How far "b" is through its tween from "a", read off where it is drawn: the tween is linear over 300px.
        private float BProgress() => 1f + TranslateX(Root.Q<VisualElement>("b")) / 300f;

        // 1 where nothing writes the element's opacity inline.
        private static float WrittenOpacity(VisualElement element) =>
            element.style.opacity.keyword == StyleKeyword.Undefined ? element.style.opacity.value : 1f;

        [Test]
        public void Given_ALayoutIdMotionHoldingAnId_When_AnotherMountsUnderItAndItsTweenEnds_Then_TheFirstIsHidden()
        {
            // Arrange
            using var mounted = MountBOverA();

            // Act
            AdvancePast(1f);

            // Assert
            Assert.That(Root.Q<VisualElement>("a").resolvedStyle.visibility, Is.EqualTo(Visibility.Hidden));
        }

        [Test]
        public void Given_ALayoutIdMotionHoldingAnId_When_AnotherMountsUnderIt_Then_ItIsDrawnWhereTheOtherIsDrawn()
        {
            // Arrange
            using var mounted = MountBOverA();

            // Act — "b" some 50px on from "a".
            for (var i = 0; i < 10; i++) Tick();

            // Assert
            var b = DrawnBox(Root.Q<VisualElement>("b"));
            Assert.That((b.x > 30f, Near(DrawnBox(Root.Q<VisualElement>("a")), b)), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ALayoutIdMotionThatJoinedAnId_When_AQuarterOfItsTweenHasPassed_Then_ItIsFadedInOnCircOut()
        {
            // Arrange
            using var mounted = MountBOverA();

            // Act
            for (var i = 0; i < 15; i++) Tick();

            // Assert — Framer's easeCrossfadeIn: circOut over the first half of the move.
            var progress = BProgress();
            var expected = Mathf.Sin(Mathf.Acos(1f - progress / 0.5f));
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("b")), Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_ALayoutIdMotionThatJoinedAnId_When_ThreeQuartersOfItsTweenHavePassed_Then_TheOneBehindItIsFadingOut()
        {
            // Arrange
            using var mounted = MountBOverA();

            // Act
            for (var i = 0; i < 45; i++) Tick();

            // Assert — Framer's easeCrossfadeOut: linear from halfway to 95% of the move.
            var progress = BProgress();
            var expected = 1f - (progress - 0.5f) / 0.45f;
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("a")), Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_ALayoutIdMotionThatJoinedAnId_When_ItsTweenEnds_Then_ItsOpacityIsHandedBack()
        {
            // Arrange
            using var mounted = MountBOverA();
            var b = Root.Q<VisualElement>("b");
            var fading = b.style.opacity.keyword != StyleKeyword.Null;

            // Act
            AdvancePast(1f);

            // Assert
            Assert.That((fading, b.style.opacity.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ALayoutIdMotionJoiningAnId_When_ItIsPatchedAgainBeforeItsFirstLayout_Then_ItStillCrossfades()
        {
            // Arrange
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bLeft, s_bTransition, s_sharedClasses) = (300, s_slowTween, "");
            using var mounted = MountAAlone();
            s_bMounted = true;
            s_setStep.Invoke(++s_sharedRenders);
            mounted.FlushStateForTest();

            // Act — a second render before any layout, then "b" some 50px on.
            s_setStep.Invoke(++s_sharedRenders);
            mounted.FlushStateForTest();
            for (var i = 0; i < 11; i++) Tick();

            // Assert — "a" drawn where "b" is, fading behind it.
            var b = DrawnBox(Root.Q<VisualElement>("b"));
            Assert.That((b.x > 30f, Near(DrawnBox(Root.Q<VisualElement>("a")), b)), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ALayoutIdMotionThatTransitionsAll_When_AnotherTakesItsIdWithoutATween_Then_TheNextFrameHidesIt()
        {
            // Arrange — the bundled sheet, so that transition-all gives visibility a transition.
            VelvetStyleUtilities.AttachTo(Root);
            (s_bTransition, s_sharedClasses) = (StyleTransitionConfig.None, "transition-all");

            // Act
            using var mounted = MountBOverA();

            // Assert
            Assert.That(Root.Q<VisualElement>("a").resolvedStyle.visibility, Is.EqualTo(Visibility.Hidden));
        }

        [Test]
        public void Given_TwoLiveLayoutIdMotionsSharingAnId_When_TheOneMountedFirstMoves_Then_ItDoesNotTakeTheLead()
        {
            // Arrange
            using var mounted = MountBOverA();
            AdvancePast(1f);

            // Act
            s_aLeft = 100;
            RenderShared(mounted);

            // Assert — taking the lead, it would tween from where "b" is drawn.
            Assert.That(Root.Q<VisualElement>("a").style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_TwoLiveLayoutIdMotionsSharingAnId_When_TheLeadLeaves_Then_TheOtherTweensFromWhereTheLeadStood()
        {
            // Arrange — "b" at rest 300px right of "a".
            using var mounted = MountBOverA();
            AdvancePast(1f);

            // Act
            s_bMounted = false;
            RenderShared(mounted);

            // Assert — one frame into a one-second tween back from 300px right.
            Assert.That(TranslateX(Root.Q<VisualElement>("a")), Is.GreaterThan(250f));
        }

        // GREEN_ON_BASE(characterization): the base tweens a Motion that moves in the render its id's other holder leaves from that holder's box.
        // A promotion started against the member's layout before the render moved it must still tween to where it moved.
        [Test]
        public void Given_TwoLiveLayoutIdMotionsAtOneBox_When_TheLeadLeavesAndTheOtherMovesInOneRender_Then_TheOtherTweens()
        {
            // Arrange — both at the left, "b" leading.
            s_bLeft = 0;
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bTransition, s_sharedClasses) = (s_slowTween, "");
            using var mounted = MountAAlone();
            s_bMounted = true;
            RenderShared(mounted);
            AdvancePast(1f);

            // Act
            (s_bMounted, s_aLeft) = (false, 200);
            RenderShared(mounted);

            // Assert — back over the left, 200px short of its new box.
            Assert.That(TranslateX(Root.Q<VisualElement>("a")), Is.LessThan(-150f));
        }

        [Test]
        public void Given_TwoLiveLayoutIdMotionsSharingAnId_When_TheLeadLeaves_Then_TheOtherIsShownAgain()
        {
            // Arrange
            using var mounted = MountBOverA();
            AdvancePast(1f);
            var whileBLeads = Root.Q<VisualElement>("a").resolvedStyle.visibility;

            // Act
            s_bMounted = false;
            RenderShared(mounted);

            // Assert
            Assert.That((whileBLeads, Root.Q<VisualElement>("a").resolvedStyle.visibility),
                Is.EqualTo((Visibility.Hidden, Visibility.Visible)));
        }

        // GREEN_ON_BASE(characterization): the base leaves a tween running when another Motion under its id leaves.
        // A member that leaves must not be taken for the lead and restart the lead's tween.
        [Test]
        public void Given_TwoLiveLayoutIdMotionsSharingAnId_When_TheOneBehindTheLeadLeaves_Then_TheLeadsTweenCarriesOn()
        {
            // Arrange — the bundled sheet, so that both are absolute and "a" leaving moves nothing else; "b" part way into
            // its one-second tween from "a".
            VelvetStyleUtilities.AttachTo(Root);
            using var mounted = MountBOverA();
            for (var i = 0; i < 10; i++) Tick();
            var before = TranslateX(Root.Q<VisualElement>("b"));

            // Act
            s_aMounted = false;
            RenderShared(mounted);

            // Assert — nearer its box than before, not back over "a" 300px left.
            Assert.That(TranslateX(Root.Q<VisualElement>("b")), Is.GreaterThan(before));
        }

        [Test]
        public void Given_AHiddenLayoutIdMotion_When_ItTakesAnotherId_Then_ItIsShownAgain()
        {
            // Arrange
            using var mounted = MountBOverA();
            AdvancePast(1f);
            var whileBLeads = Root.Q<VisualElement>("a").resolvedStyle.visibility;

            // Act
            s_aId = "other";
            RenderShared(mounted);

            // Assert
            Assert.That((whileBLeads, Root.Q<VisualElement>("a").resolvedStyle.visibility),
                Is.EqualTo((Visibility.Hidden, Visibility.Visible)));
        }

        [Test]
        public void Given_AHiddenLayoutIdMotion_When_ItsLayoutIdIsRemoved_Then_ItIsShownAgain()
        {
            // Arrange
            using var mounted = MountBOverA();
            AdvancePast(1f);
            var whileBLeads = Root.Q<VisualElement>("a").resolvedStyle.visibility;

            // Act
            s_aId = null;
            RenderShared(mounted);

            // Assert
            Assert.That((whileBLeads, Root.Q<VisualElement>("a").resolvedStyle.visibility),
                Is.EqualTo((Visibility.Hidden, Visibility.Visible)));
        }

        [Test]
        public void Given_TheOnlyLayoutIdMotionHoldingAnId_When_ItsLayoutIdIsRemovedAndAnotherMountsUnderIt_Then_TheOtherAppearsInPlace()
        {
            // Arrange
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bLeft, s_bTransition, s_sharedClasses) = (300, s_slowTween, "");
            using var mounted = MountAAlone();
            s_aId = null;
            RenderShared(mounted);

            // Act
            s_bMounted = true;
            RenderShared(mounted);

            // Assert
            Assert.That(Root.Q<VisualElement>("b").style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base starts no tween on a Motion when the holder of an id it left leaves.
        // A Motion that took another id must not stay behind as a member of the one it left.
        [Test]
        public void Given_AHiddenLayoutIdMotionThatTookAnotherId_When_TheLeadOfItsOldIdLeaves_Then_ItDoesNotTween()
        {
            // Arrange
            using var mounted = MountBOverA();
            AdvancePast(1f);
            s_aId = "other";
            RenderShared(mounted);

            // Act
            s_bMounted = false;
            RenderShared(mounted);

            // Assert
            Assert.That(Root.Q<VisualElement>("a").style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base tweens a Motion that mounts as the only holder of the id a removed one held, and writes no opacity.
        // A holder alone under its id does not crossfade: it mixes from the previous holder's opacity, here its own.
        [Test]
        public void Given_ALayoutIdMotionRemovedAsAnotherMountsUnderItsId_When_TheOtherTweens_Then_ItDoesNotFade()
        {
            // Arrange
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bLeft, s_bTransition, s_sharedClasses) = (300, s_slowTween, "");
            using var mounted = MountAAlone();

            // Act
            (s_aMounted, s_bMounted) = (false, true);
            RenderShared(mounted);

            // Assert — back over "a", at its own opacity.
            var b = Root.Q<VisualElement>("b");
            Assert.That((TranslateX(b) < -250f, WrittenOpacity(b)), Is.EqualTo((true, 1f)));
        }

        [Test]
        public void Given_TwoLiveLayoutIdMotionsSharingAnId_When_TheLeadMovesOnItsOwn_Then_TheOtherStaysHidden()
        {
            // Arrange
            using var mounted = MountBOverA();
            AdvancePast(1f);

            // Act
            s_bLeft = 400;
            RenderShared(mounted);

            // Assert
            Assert.That(Root.Q<VisualElement>("a").resolvedStyle.visibility, Is.EqualTo(Visibility.Hidden));
        }

        [Test]
        public void Given_ALayoutIdMotionBehindANewLead_When_TheCrossfadeEnds_Then_ItsOpacityIsHandedBack()
        {
            // Arrange
            using var mounted = MountBOverA();
            for (var i = 0; i < 5; i++) Tick();
            var a = Root.Q<VisualElement>("a");
            var fading = a.style.opacity.keyword != StyleKeyword.Null;

            // Act
            AdvancePast(1f);

            // Assert
            Assert.That((fading, a.style.opacity.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ALayoutIdMotionWithAPendingMove_When_ItsLayoutIdIsRemovedAsItMoves_Then_ItDoesNotTween()
        {
            // Arrange — "a" tweening a move 100px right, then patched where it stands.
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bLeft, s_bTransition, s_sharedClasses) = (300, s_slowTween, "");
            using var mounted = MountAAlone();
            s_aLeft = 100;
            RenderShared(mounted);
            RenderShared(mounted);

            // Act
            (s_aId, s_aLeft) = (null, 200);
            RenderShared(mounted);

            // Assert
            Assert.That(Root.Q<VisualElement>("a").style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALayoutIdLeadMidTween_When_ItLeaves_Then_TheOtherTweensFromWhereItWasDrawn()
        {
            // Arrange — "b" some 150px on from "a".
            using var mounted = MountBOverA();
            for (var i = 0; i < 30; i++) Tick();

            // Act
            s_bMounted = false;
            RenderShared(mounted);

            // Assert — back over where "b" was drawn, not over "b"'s box or at its own.
            Assert.That(TranslateX(Root.Q<VisualElement>("a")), Is.InRange(100f, 200f));
        }

        // GREEN_ON_BASE(characterization): the base never suspends the transitions of a Motion another takes the id from.
        // A member that leads again must hand back the suspension it held while hidden.
        [Test]
        public void Given_AHiddenTransitionAllMotion_When_ItLeadsAgainAndItsTweenEnds_Then_ItsTransitionsAreHandedBack()
        {
            // Arrange — the bundled sheet, and "b" taking the id without a tween, which hides "a" at once.
            VelvetStyleUtilities.AttachTo(Root);
            (s_bTransition, s_sharedClasses) = (StyleTransitionConfig.None, "transition-all");
            using var mounted = MountBOverA();

            // Act
            s_bMounted = false;
            RenderShared(mounted);
            AdvancePast(1f);

            // Assert
            Assert.That(Root.Q<VisualElement>("a").style.transitionProperty.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base hides every holder of an id but the lead, crossfading none of them.
        // A lead that lands must end the crossfade of every member drawn behind it, the one superseded included.
        [Test]
        public void Given_ALeadSupersededMidCrossfade_When_ANewLeadOnAOneFrameTweenLands_Then_BothBehindItAreHidden()
        {
            // Arrange — "a" tweening a move of its own when "b" takes the id over, and "c" on a one-millisecond tween
            // taking it from "b" five frames later.
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bLeft, s_bTransition, s_sharedClasses) = (300, s_slowTween, "");
            s_cTransition = new StyleTransitionConfig { DurationSec = 0.001f, Easing = EasingMode.Linear };
            using var mounted = MountAAlone();
            s_aLeft = 100;
            RenderShared(mounted);
            s_bMounted = true;
            RenderShared(mounted);
            for (var i = 0; i < 5; i++) Tick();

            // Act
            s_cMounted = true;
            RenderShared(mounted);
            for (var i = 0; i < 3; i++) Tick();

            // Assert
            Assert.That((Root.Q<VisualElement>("a").resolvedStyle.visibility, Root.Q<VisualElement>("b").resolvedStyle.visibility),
                Is.EqualTo((Visibility.Hidden, Visibility.Hidden)));
        }

        [Test]
        public void Given_ALeadCrossfading_When_AMoveOfItsOwnInterruptsIt_Then_TheCrossfadeHoldsWhereItWas()
        {
            // Arrange — "b" about a third of the way through its crossfade.
            using var mounted = MountBOverA();
            for (var i = 0; i < 18; i++) Tick();
            var reached = WrittenOpacity(Root.Q<VisualElement>("b"));

            // Act
            s_bLeft = 400;
            RenderShared(mounted);
            for (var i = 0; i < 5; i++) Tick();

            // Assert — "b" still at the part-way opacity the crossfade reached, and "a" still drawn behind it.
            var b = WrittenOpacity(Root.Q<VisualElement>("b"));
            Assert.That((reached < 0.95f, Mathf.Abs(b - reached) < 0.06f, Root.Q<VisualElement>("a").resolvedStyle.visibility),
                Is.EqualTo((true, true, Visibility.Visible)));
        }

        // GREEN_ON_BASE(characterization): the base draws no crossfade, so a Motion taking an id is drawn at its own
        // opacity. A crossfade with no tween to run must leave it there.
        [Test]
        public void Given_AMotionMidItsOwnTween_When_ItTakesAHeldIdOnAZeroDuration_Then_ItIsDrawnAtItsOwnOpacity()
        {
            // Arrange — "b" holds "card"; "a" holds an id of its own, some way through a tween of its own.
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("solo", 0, true, true);
            (s_bLeft, s_bTransition, s_sharedClasses) = (300, s_slowTween, "");
            using var mounted = MountAAlone();
            s_aLeft = 100;
            RenderShared(mounted);
            for (var i = 0; i < 10; i++) Tick();

            // Act — "a" moves and takes "card" on a zero-duration transition.
            (s_aId, s_aLeft, s_aTransition) = ("card", 150, new StyleTransitionConfig { DurationSec = 0f });
            RenderShared(mounted);

            // Assert
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("a")), Is.EqualTo(1f));
        }

        [Test]
        public void Given_AHalfOpaqueHolderRemovedAsAnotherMountsAtItsBox_When_AQuarterOfTheTweenHasPassed_Then_TheOtherIsMixingFromItsOpacity()
        {
            // Arrange — the bundled sheet, and "a" at half opacity where "b" mounts.
            VelvetStyleUtilities.AttachTo(Root);
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bLeft, s_bTransition, s_sharedClasses, s_aClasses) = (0, s_slowTween, "", "opacity-50");
            using var mounted = MountAAlone();
            (s_aMounted, s_bMounted) = (false, true);
            RenderShared(mounted);

            // Act
            for (var i = 0; i < 15; i++) Tick();

            // Assert — part way from "a"'s half to its own whole, as Framer animates a node taking another's place
            // however little it moves.
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("b")), Is.InRange(0.55f, 0.8f));
        }

        [Test]
        public void Given_TwoHoldersAtOneBoxOnASoftSpring_When_TheSecondHasTakenTheIdForTwoFrames_Then_ItIsStillFadingIn()
        {
            // Arrange — the bundled sheet; "b" mounts under "card" where "a" stands, on a spring that leaves its start
            // slowly.
            VelvetStyleUtilities.AttachTo(Root);
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bLeft, s_sharedClasses) = (0, "");
            s_bTransition = new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 50f, Damping = 10f, Mass = 1f };
            using var mounted = MountAAlone();
            s_bMounted = true;
            RenderShared(mounted);

            // Act
            Tick();

            // Assert
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("b")), Is.LessThan(0.9f));
        }

        // GREEN_ON_BASE(characterization): the base leaves a Motion's own inline opacity alone as it takes an id.
        // A crossfade must hand back the opacity the Motion held.
        [Test]
        public void Given_AMotionAtItsOwnInlineOpacityThatJoinedAnId_When_ItsCrossfadeEnds_Then_ItHoldsItsOwnOpacityAgain()
        {
            // Arrange
            s_bClasses = "opacity-[0.6]";
            using var mounted = MountBOverA();

            // Act
            AdvancePast(1f);

            // Assert
            var b = Root.Q<VisualElement>("b");
            Assert.That((b.style.opacity.keyword, b.style.opacity.value), Is.EqualTo((StyleKeyword.Undefined, 0.6f)));
        }

        [Test]
        public void Given_AMotionAtItsOwnInlineOpacityJoiningAnId_When_AQuarterOfItsTweenHasPassed_Then_ItsOwnIsFadedInOnCircOut()
        {
            // Arrange
            s_bClasses = "opacity-[0.6]";
            using var mounted = MountBOverA();

            // Act
            for (var i = 0; i < 15; i++) Tick();

            // Assert
            var expected = 0.6f * Mathf.Sin(Mathf.Acos(1f - BProgress() / 0.5f));
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("b")), Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_ALeadCrossfadingIn_When_AClassTakesItsOpacityToZeroOnATransition_Then_ItsOwnIsCarriedOnThatTransition()
        {
            // Arrange — the bundled sheet; "b" transitions its opacity linearly over a second.
            VelvetStyleUtilities.AttachTo(Root);
            s_bClasses = "transition-opacity duration-1000 ease-linear";
            using var mounted = MountBOverA();
            for (var i = 0; i < 15; i++) Tick();

            // Act — about a sixth of a second after "b" takes opacity-0.
            s_bClasses += " opacity-0";
            RenderShared(mounted);
            for (var i = 0; i < 10; i++) Tick();

            // Assert — its own opacity part-way down that second, under the crossfade's circOut.
            var own = WrittenOpacity(Root.Q<VisualElement>("b")) / Mathf.Sin(Mathf.Acos(1f - BProgress() / 0.5f));
            Assert.That(own, Is.InRange(0.7f, 0.95f));
        }

        [Test]
        public void Given_ALeadCrossfadingInWithAnArbitraryTransitionDuration_When_AClassTakesItsOpacityToZero_Then_ItsOwnIsCarriedLinearlyOverThatDuration()
        {
            // Arrange — the bundled sheet; "b" transitions its opacity linearly over a second given by an arbitrary value.
            VelvetStyleUtilities.AttachTo(Root);
            s_bClasses = "transition-opacity duration-[1000ms] ease-linear";
            using var mounted = MountBOverA();
            for (var i = 0; i < 15; i++) Tick();

            // Act — about a sixth of a second after "b" takes opacity-0.
            s_bClasses += " opacity-0";
            RenderShared(mounted);
            for (var i = 0; i < 10; i++) Tick();

            // Assert — its own opacity about a sixth of the way down that second, as UI Toolkit runs it, under the
            // crossfade's circOut: not on the transition-property list the suspension writes, whose curve is Ease.
            var own = WrittenOpacity(Root.Q<VisualElement>("b")) / Mathf.Sin(Mathf.Acos(1f - BProgress() / 0.5f));
            Assert.That(own, Is.InRange(0.78f, 0.92f));
        }

        [Test]
        public void Given_AMemberHalfWayThroughItsOwnFade_When_ANewLeadTakesTheId_Then_TheFadeEndsWhenItWouldHave()
        {
            // Arrange — the bundled sheet; "a" about half way through a linear second down to opacity-0 as "b" takes the
            // id and starts drawing it.
            VelvetStyleUtilities.AttachTo(Root);
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bLeft, s_bTransition, s_sharedClasses) = (300, s_slowTween, "");
            s_aClasses = "transition-opacity duration-1000 ease-linear";
            using var mounted = MountAAlone();
            s_aClasses += " opacity-0";
            RenderShared(mounted);
            for (var i = 0; i < 30; i++) Tick();
            s_bMounted = true;
            RenderShared(mounted);

            // Act — some 0.45 s on, before "a" starts fading out behind "b".
            for (var i = 0; i < 27; i++) Tick();

            // Assert — almost at nothing, where a fade begun a second earlier ends, rather than half way down another
            // whole second begun as "b" took the id.
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("a")), Is.InRange(0f, 0.12f));
        }

        [Test]
        public void Given_AMemberPartWayThroughADelayedFade_When_ANewLeadTakesTheId_Then_TheFadeEndsWhenItWouldHave()
        {
            // Arrange — the bundled sheet, and the test's own giving "a" a linear second down to opacity-0 after a fifth
            // of a second's delay; "a" some 0.3 s into that second as "b" takes the id and starts drawing it.
            VelvetStyleUtilities.AttachTo(Root);
            Root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(OpacityTransitionSheetPath));
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bLeft, s_bTransition, s_sharedClasses) = (300, s_slowTween, "");
            s_aClasses = "layout-id-test-fade-delayed";
            using var mounted = MountAAlone();
            s_aClasses += " opacity-0";
            RenderShared(mounted);
            for (var i = 0; i < 30; i++) Tick();
            s_bMounted = true;
            RenderShared(mounted);

            // Act — some 0.45 s on, before "a" starts fading out behind "b".
            for (var i = 0; i < 27; i++) Tick();

            // Assert — about a quarter, where the fade stands 0.75 s into its second, rather than near a half, where
            // it would stand had it waited out its delay again as "b" took the id.
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("a")), Is.InRange(0.15f, 0.35f));
        }

        [Test]
        public void Given_AMotionBehindANewLead_When_ItsVariantSwapsToHiddenOnADelayedTransition_Then_ItsOwnIsCarriedOnThatTransition()
        {
            // Arrange — "a" visible on its variants, and "b" a little under halfway through taking the id from it.
            VelvetStyleUtilities.AttachTo(Root);
            (s_aPose, s_aTransition) = ("visible", new StyleTransitionConfig { DurationSec = 1f, DelaySec = 0.1f, Easing = EasingMode.Linear });
            using var mounted = MountBOverA();
            for (var i = 0; i < 27; i++) Tick();

            // Act — about a quarter of a second after "a" swaps to its hidden pose.
            s_aPose = "hidden";
            RenderShared(mounted);
            for (var i = 0; i < 14; i++) Tick();

            // Assert — its own opacity about a seventh of the way down after the delay, under the crossfade's fade out,
            // and drawn at what is written rather than trailing it on the swap's transition.
            var a = Root.Q<VisualElement>("a");
            var written = WrittenOpacity(a);
            var own = written / (1f - (BProgress() - 0.5f) / 0.45f);
            Assert.That((own > 0.78f && own < 0.95f, Mathf.Abs(a.resolvedStyle.opacity - written) < 0.1f), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base hands a transition-all lead's transitions back once its move lands.
        // The crossfade's suspension must be handed back with it.
        [Test]
        public void Given_ATransitionAllMotionThatJoinedAnId_When_ItsCrossfadeEnds_Then_ItsTransitionsAreHandedBack()
        {
            // Arrange
            VelvetStyleUtilities.AttachTo(Root);
            s_bClasses = "transition-all";
            using var mounted = MountBOverA();
            var b = Root.Q<VisualElement>("b");
            var suspended = b.style.transitionProperty.keyword != StyleKeyword.Null;

            // Act
            AdvancePast(1f);

            // Assert
            Assert.That((suspended, b.style.transitionProperty.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AHolderWhoseOwnOpacityChangedAfterItsCrossfade_When_ItLeavesAsAnotherMountsUnderItsId_Then_TheOtherMixesFromItsOpacityNow()
        {
            // Arrange — the bundled sheet; "b" at half opacity takes the id from "a" and lands, then drops to a quarter.
            VelvetStyleUtilities.AttachTo(Root);
            (s_bClasses, s_cTransition) = ("opacity-50", s_slowTween);
            using var mounted = MountBOverA();
            AdvancePast(1f);
            s_bClasses = "opacity-25";
            RenderShared(mounted);

            // Act — "a" and "b" leave as "c" mounts under the id; a quarter of the way through its tween.
            (s_aMounted, s_bMounted, s_cMounted) = (false, false, true);
            RenderShared(mounted);
            for (var i = 0; i < 14; i++) Tick();

            // Assert
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("c")), Is.EqualTo(Mathf.Lerp(0.25f, 1f, CProgress())).Within(0.02f));
        }

        // "a" holds "pool"; step 1 mounts a Label-typed "b" 300px right under it, step 2 removes "b", and step 3 rents
        // a Label for an unrelated Motion fading in on a spring.
        [Component]
        private static VNode PooledCrossfadeRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var a = V.Motion(key: "a", name: "a", layoutId: "pool", transition: s_slowTween,
                className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]");
            return V.Div(children: step switch
            {
                1 => new[]
                {
                    a, V.Motion(key: "b", name: "b", layoutId: "pool", elementType: typeof(Label), transition: s_slowTween,
                        className: "absolute left-[300px] top-[0px] w-[100px] h-[100px]"),
                },
                3 => new[]
                {
                    a, V.Motion(key: "reused", name: "reused", elementType: typeof(Label), variants: s_fade, initial: "hidden",
                        animate: "visible", transition: s_layoutSpring, className: "w-[50px] h-[50px]"),
                },
                _ => new[] { a },
            });
        }

        // GREEN_ON_BASE(characterization): the base writes no opacity over a lead crossfading in, so none follows it into the pool.
        // What a crossfade keeps for an element must not follow it into the pool either.
        [Test]
        public void Given_ALeadRemovedMidCrossfade_When_ThePoolHandsItsElementToAMotionFadingInOnASpring_Then_ItsOpacityIsHandedBackAsItSettles()
        {
            // Arrange — "b" about a quarter of the way through crossfading in when it is removed.
            using var mounted = V.Mount(Root, V.Component(PooledCrossfadeRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 15; i++) Tick();
            var original = Root.Q<VisualElement>("b");
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Act
            s_setStep.Invoke(3);
            mounted.FlushStateForTest();
            AdvancePast(2f);

            // Assert
            var reused = Root.Q<VisualElement>("reused");
            Assert.That((ReferenceEquals(original, reused), reused.style.opacity.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }

        // GREEN_ON_BASE(characterization): the base leaves a class's opacity transition to UI Toolkit, which runs it past any layoutId move.
        // One the crossfade carried must run on past the crossfade as well.
        [Test]
        public void Given_ALeadCrossfadingIn_When_ItsOwnOpacityTransitionOutlastsTheMove_Then_ItRunsOnPastTheLanding()
        {
            // Arrange — the bundled sheet; "b" transitions its opacity linearly over a second, and takes opacity-0 some
            // three quarters of the way through its move.
            VelvetStyleUtilities.AttachTo(Root);
            s_bClasses = "transition-opacity duration-1000 ease-linear";
            using var mounted = MountBOverA();
            for (var i = 0; i < 47; i++) Tick();
            s_bClasses += " opacity-0";
            RenderShared(mounted);

            // Act — a third of a second on, past the landing.
            for (var i = 0; i < 20; i++) Tick();

            // Assert — about a third of the way down.
            Assert.That(Root.Q<VisualElement>("b").resolvedStyle.opacity, Is.InRange(0.55f, 0.8f));
        }

        [Test]
        public void Given_AThirdHolderTakingTheIdFromALeadMidCrossfade_When_TheCrossfadeOutIsUnderWay_Then_TheFirstIsDrawnAtWhatTheLeadWasDrawnAt()
        {
            // Arrange — "b" about a quarter of the way through crossfading in over "a", drawn part-way faded in, when "c"
            // takes the id from it.
            (s_bClasses, s_cTransition) = ("", s_slowTween);
            using var mounted = MountBOverA();
            for (var i = 0; i < 15; i++) Tick();
            var b = Root.Q<VisualElement>("b");
            var (drawn, drawnLeft) = (WrittenOpacity(b), 300f + TranslateX(b));
            s_cMounted = true;
            RenderShared(mounted);

            // Act — some three fifths of the way through "c"'s move.
            for (var i = 0; i < 36; i++) Tick();

            // Assert — at the opacity "b" was drawn at as "c" took the id, fading out: Framer's promote takes the
            // previous lead's animationValues, which the new lead's animation stops. "c" tweens linearly from where
            // "b" was drawn.
            var progress = 1f + TranslateX(Root.Q<VisualElement>("c")) / (600f - drawnLeft);
            var expected = drawn * (1f - (progress - 0.5f) / 0.45f);
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("a")), Is.EqualTo(expected).Within(0.04f));
        }

        [Test]
        public void Given_ALeadAtItsOwnInlineOpacityCrossfadingIn_When_ItsClassesChangeThatOpacity_Then_TheNewOneIsDrawnFadedAtOnce()
        {
            // Arrange — "b" some way through crossfading in at its own 0.6.
            s_bClasses = "opacity-[0.6]";
            using var mounted = MountBOverA();
            for (var i = 0; i < 15; i++) Tick();

            // Act — the render alone, before any frame.
            s_bClasses = "opacity-[0.3]";
            s_setStep.Invoke(++s_sharedRenders);
            mounted.FlushStateForTest();

            // Assert — the new 0.3 under the fade in reached so far, rather than 0.3 drawn whole.
            var expected = 0.3f * Mathf.Sin(Mathf.Acos(1f - BProgress() / 0.5f));
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("b")), Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_AMemberBehindANewLead_When_ItTakesAnOpacityTransitionMidCrossfade_Then_ItIsDrawnAtTheOpacityWritten()
        {
            // Arrange — the bundled sheet; "a" some way behind "b" before it takes a one-second opacity transition.
            VelvetStyleUtilities.AttachTo(Root);
            using var mounted = MountBOverA();
            for (var i = 0; i < 20; i++) Tick();
            s_aClasses = "transition-opacity duration-1000";
            RenderShared(mounted);

            // Act — some seven tenths of the way, where "a" is fading out.
            for (var i = 0; i < 24; i++) Tick();

            // Assert — within a frame's step of the opacity written, which a second-long transition of each write
            // would trail far behind.
            var a = Root.Q<VisualElement>("a");
            var written = WrittenOpacity(a);
            Assert.That((written < 0.9f, Mathf.Abs(a.resolvedStyle.opacity - written) < 0.1f), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AMemberBehindALeadWhoseVariantSwapHasEnded_When_AClassChangesItsOpacity_Then_ItIsCarriedOnTheClassesTransition()
        {
            // Arrange — the bundled sheet; "b" on a three-second move, and "a", which carries its opacity over a second,
            // swapping to its hidden pose on a tenth of a second a few frames in, a tween over well before the class
            // below lands.
            VelvetStyleUtilities.AttachTo(Root);
            (s_aPose, s_aTransition, s_aClasses) = ("visible", s_quickTween, "transition-opacity duration-1000 ease-linear");
            s_bTransition = new StyleTransitionConfig { DurationSec = 3f, Easing = EasingMode.Linear };
            using var mounted = MountBOverA();
            for (var i = 0; i < 5; i++) Tick();
            s_aPose = "hidden";
            RenderShared(mounted);
            for (var i = 0; i < 25; i++) Tick();

            // Act — a tenth of a second or so after "a" takes opacity-50, before the fade out starts.
            s_aClasses += " opacity-50";
            RenderShared(mounted);
            for (var i = 0; i < 9; i++) Tick();

            // Assert — a tenth or so of the way up on the class's second, rather than landed on the swap's tenth.
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("a")), Is.InRange(0.02f, 0.2f));
        }

        // GREEN_ON_BASE(characterization): the base never turns off the picking of a layoutId Motion's subtree.
        // A control a member drops while drawn behind a lead must take pointers again wherever the pool hands it next.
        [Test]
        public void Given_AMemberDroppingATextFieldWhileDrawnBehindALead_When_ThePoolHandsItOn_Then_ItsInputTakesPointers()
        {
            // Arrange — the field's input as it picks before "b" takes the id over "a", and "a" dropping it five
            // frames into "b"'s move.
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bLeft, s_bTransition, s_sharedClasses, s_aField) = (300, s_slowTween, "", true);
            using var mounted = MountAAlone();
            var field = Root.Q<TextField>("field");
            var own = field.Q(className: TextField.inputUssClassName).pickingMode;
            s_bMounted = true;
            RenderShared(mounted);
            for (var i = 0; i < 5; i++) Tick();
            s_aField = false;
            RenderShared(mounted);

            // Act
            s_looseField = true;
            RenderShared(mounted);

            // Assert
            var loose = Root.Q<TextField>("loose");
            Assert.That((ReferenceEquals(field, loose), loose.Q(className: TextField.inputUssClassName).pickingMode),
                Is.EqualTo((true, own)));
        }

        private static TabStore s_holders;

        // Two sibling components on one store, the first holding "a" under "pool" at half opacity while it is selected
        // and the second "b" 300px right under the same id. Both are Labels, a pooled type: the first leaves in the
        // flush before the second mounts, so the pool gives b a's element.
        [Component]
        private static VNode FirstHolderRender() => V.Div(children: Hooks.UseStore(s_holders, s => s.Index) == 0
            ? new VNode[]
            {
                V.Motion(key: "a", name: "a", layoutId: "pool", elementType: typeof(Label), transition: s_slowTween,
                    className: "absolute left-[0px] top-[0px] w-[100px] h-[100px] opacity-50"),
            }
            : Array.Empty<VNode>());

        [Component]
        private static VNode SecondHolderRender() => V.Div(children: Hooks.UseStore(s_holders, s => s.Index) == 1
            ? new VNode[]
            {
                V.Motion(key: "b", name: "b", layoutId: "pool", elementType: typeof(Label), transition: s_slowTween,
                    className: "absolute left-[300px] top-[0px] w-[100px] h-[100px]"),
            }
            : Array.Empty<VNode>());

        [Component]
        private static VNode PooledHolderRender() => V.Div(children: new VNode[]
        {
            V.Component(FirstHolderRender, key: "first"),
            V.Component(SecondHolderRender, key: "second"),
        });

        [Test]
        public void Given_AHalfOpaqueHolderWhoseElementThePoolGivesTheNextHolder_When_AQuarterOfTheTweenHasPassed_Then_TheNextIsMixingFromItsOpacity()
        {
            // Arrange — the bundled sheet, so opacity-50 resolves.
            VelvetStyleUtilities.AttachTo(Root);
            s_holders = new TabStore(0);
            using var mounted = V.Mount(Root, V.Component(PooledHolderRender, key: "root"));
            Tick();
            var original = Root.Q<VisualElement>("a");
            s_holders.Select(1);
            Tick();

            // Act
            for (var i = 0; i < 14; i++) Tick();

            // Assert — mixed from "a"'s half, rather than from "b"'s own whole because the element that held it is b's.
            var b = Root.Q<VisualElement>("b");
            Assert.That(ReferenceEquals(original, b) ? WrittenOpacity(b) : float.NaN,
                Is.EqualTo(Mathf.Lerp(0.5f, 1f, BProgress())).Within(0.02f));
        }

        [Test]
        public void Given_AMemberWhoseStylesheetTransitionsItsOpacityBehindANewLead_When_ItFadesOut_Then_ItIsDrawnAtTheOpacityWritten()
        {
            // Arrange — a stylesheet of the test's own giving "a" a one-second opacity transition through a class no
            // bundled utility names.
            Root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(OpacityTransitionSheetPath));
            s_aClasses = "layout-id-test-fade";
            using var mounted = MountBOverA();

            // Act — three quarters of the way, where "a" is fading out.
            for (var i = 0; i < 45; i++) Tick();

            // Assert — within a frame's step of the opacity written, which a second-long transition of each write
            // would trail far behind.
            var a = Root.Q<VisualElement>("a");
            var written = WrittenOpacity(a);
            Assert.That((written < 0.9f, Mathf.Abs(a.resolvedStyle.opacity - written) < 0.1f), Is.EqualTo((true, true)));
        }

        private const string OpacityTransitionSheetPath =
            "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdOpacityTransition.uss";

        [Test]
        public void Given_AnOpacityTransitioningMotionBehindANewLead_When_ItFadesOut_Then_ItIsDrawnAtTheOpacityWritten()
        {
            // Arrange — the bundled sheet, and "a" carrying its opacity on a one-second transition.
            VelvetStyleUtilities.AttachTo(Root);
            s_aClasses = "transition-opacity duration-1000";
            using var mounted = MountBOverA();

            // Act — three quarters of the way, where "a" is fading out.
            for (var i = 0; i < 45; i++) Tick();

            // Assert — within a frame's step of the opacity written, which a second-long transition of each write
            // would trail far behind.
            var a = Root.Q<VisualElement>("a");
            var written = WrittenOpacity(a);
            Assert.That((written < 0.9f, Mathf.Abs(a.resolvedStyle.opacity - written) < 0.1f), Is.EqualTo((true, true)));
        }

        // A card holding "card" with a title inside it holding "title", the first card's title at half opacity; at
        // step 1 alone, a second card 300px right with its own title, under the same two ids.
        [Component]
        private static VNode NestedTitlesRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            VNode Card(string name, int left) => V.Motion(key: name, name: name, layoutId: "card", transition: s_slowTween,
                className: $"absolute left-[{left}px] top-[0px] w-[100px] h-[100px]",
                children: new VNode[]
                {
                    V.Motion(key: "title", name: name + "-title", layoutId: "title", transition: s_slowTween,
                        className: $"left-[10px] top-[10px] w-[50px] h-[20px] {(name == "a" ? "opacity-50" : "")}"),
                });
            return V.Div(children: step == 1 ? new[] { Card("a", 0), Card("b", 300) } : new[] { Card("a", 0) });
        }

        [Test]
        public void Given_TwoCardsWithTitlesSharingIds_When_TheSecondCrossfadesOverTheFirst_Then_ItsTitleDoesNotFadeAgain()
        {
            // Arrange — the bundled sheet, so the first title's opacity-50 resolves.
            VelvetStyleUtilities.AttachTo(Root);
            using var mounted = V.Mount(Root, V.Component(NestedTitlesRender, key: "root"));
            Tick();

            // Act — past halfway, where the first card is fading out.
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 41; i++) Tick();

            // Assert — the first card fading out behind the second, and neither title fading or mixing from the other's
            // opacity: Framer's crossfade skips a node below one crossfading, and its opacity mix one not alone under
            // its id.
            Assert.That((WrittenOpacity(Root.Q<VisualElement>("a")) < 1f, WrittenOpacity(Root.Q<VisualElement>("b-title")), WrittenOpacity(Root.Q<VisualElement>("a-title"))),
                Is.EqualTo((true, 1f, 1f)));
        }

        [Test]
        public void Given_TwoCardsWithTitlesSharingIds_When_TheFirstLeadsAgain_Then_ItsTitleTakesPointers()
        {
            // Arrange — the second card crossfades over the first, its title taking no pointer while drawn, and lands.
            using var mounted = V.Mount(Root, V.Component(NestedTitlesRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 5; i++) Tick();
            var title = Root.Q<VisualElement>("a-title");
            var whileDrawn = title.pickingMode;
            AdvancePast(1f);

            // Act — the second card leaves, and the first tweens back from it.
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            AdvancePast(1f);

            // Assert
            Assert.That((whileDrawn, title.pickingMode), Is.EqualTo((PickingMode.Ignore, PickingMode.Position)));
        }

        // How far "c" is through its tween from "b", read off where it is drawn: the tween is linear over 300px.
        private float CProgress() => 1f + TranslateX(Root.Q<VisualElement>("c")) / 300f;

        [Test]
        public void Given_AThirdHolderCrossfadingOverAHalfOpaqueLead_When_ThreeQuartersHavePassed_Then_TheFirstIsDrawnAtTheLeadsOpacityFadingOut()
        {
            // Arrange — the bundled sheet; "b" at half opacity takes the id from "a", then "c" from "b".
            VelvetStyleUtilities.AttachTo(Root);
            (s_bClasses, s_cTransition) = ("opacity-50", s_slowTween);
            using var mounted = MountBOverA();
            AdvancePast(1f);
            s_cMounted = true;
            RenderShared(mounted);

            // Act
            for (var i = 0; i < 45; i++) Tick();

            // Assert — Framer draws every member behind the lead at the previous lead's opacity fading out, so "a",
            // fully opaque of its own, is drawn at half of that.
            var expected = 0.5f * (1f - (CProgress() - 0.5f) / 0.45f);
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("a")), Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_AHalfOpaqueHolderRemovedAsAnotherMountsUnderItsId_When_AQuarterOfTheTweenHasPassed_Then_TheOtherIsMixingFromItsOpacity()
        {
            // Arrange — the bundled sheet, and "a" at half opacity.
            VelvetStyleUtilities.AttachTo(Root);
            (s_aId, s_aLeft, s_aMounted, s_bMounted) = ("card", 0, true, false);
            (s_bLeft, s_bTransition, s_sharedClasses, s_aClasses) = (300, s_slowTween, "", "opacity-50");
            using var mounted = MountAAlone();
            (s_aMounted, s_bMounted) = (false, true);
            RenderShared(mounted);

            // Act
            for (var i = 0; i < 15; i++) Tick();

            // Assert — mixed from "a"'s half to its own whole, as Framer mixes an only member's opacity.
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("b")), Is.EqualTo(Mathf.Lerp(0.5f, 1f, BProgress())).Within(0.02f));
        }

        [Test]
        public void Given_AMemberFadingBehindANewLead_When_TheCrossfadeEnds_Then_ItsSubtreeTakesPointersAgain()
        {
            // Arrange — "a" part way through fading behind "b".
            using var mounted = MountBOverA();
            for (var i = 0; i < 5; i++) Tick();
            var child = Root.Q<VisualElement>("a-child");
            var whileFading = child.pickingMode;

            // Act
            AdvancePast(1f);

            // Assert
            Assert.That((whileFading, child.pickingMode), Is.EqualTo((PickingMode.Ignore, PickingMode.Position)));
        }

        [Test]
        public void Given_AMemberFadingBehindANewLead_When_TheCrossfadeIsUnderWay_Then_ItTakesNoPointer()
        {
            // Arrange / Act
            using var mounted = MountBOverA();
            for (var i = 0; i < 5; i++) Tick();

            // Assert
            Assert.That(Root.Q<VisualElement>("a").pickingMode, Is.EqualTo(PickingMode.Ignore));
        }

        [Test]
        public void Given_AThirdHolderCrossfadingOverALead_When_TheLeadsOwnOpacityDropsMidCrossfade_Then_TheFirstFollowsItsOpacityNow()
        {
            // Arrange — the bundled sheet; "c" takes the id from "b", which then drops to a quarter opacity.
            VelvetStyleUtilities.AttachTo(Root);
            (s_bClasses, s_cTransition) = ("opacity-50", s_slowTween);
            using var mounted = MountBOverA();
            AdvancePast(1f);
            s_cMounted = true;
            RenderShared(mounted);
            s_bClasses = "opacity-25";
            RenderShared(mounted);

            // Act
            for (var i = 0; i < 44; i++) Tick();

            // Assert — at "b"'s quarter, as it is now, rather than the half it stood at when "c" took the id.
            var expected = 0.25f * (1f - (CProgress() - 0.5f) / 0.45f);
            Assert.That(WrittenOpacity(Root.Q<VisualElement>("a")), Is.EqualTo(expected).Within(0.02f));
        }

        // Set before each render of CardModalRender: whether the modal is open, and the card's transition when not
        // the one-second tween.
        private static bool s_modalOpen;
        private static StyleTransitionConfig s_cardTransition;
        // The modal's transition when not the one-second tween, and how many times the presence's exits completed.
        private static StyleTransitionConfig s_modalTransition;
        private static int s_exitCompletions;
        // Whether the modal's V.AnimatePresence is rendered at all.
        private static bool s_presenceGone;
        // Where the modal stands, and where a Motion under an id of its own stands, none while negative.
        private static int s_modalLeft;
        private static int s_moverLeft;

        // A card at the left, and a modal 300px right under the same id inside a V.AnimatePresence, with a
        // one-second fade out as its exit.
        [Component]
        private static VNode CardModalRender()
        {
            var (_, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            return V.Div(children: new VNode[]
            {
                V.Motion(key: "card", name: "card", layoutId: "card", transition: s_cardTransition ?? s_slowTween,
                    className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]"),
                s_moverLeft < 0 ? V.Div(key: "no-mover") : V.Motion(key: "mover", name: "mover", layoutId: "mover",
                    transition: s_slowTween, className: $"absolute left-[{s_moverLeft}px] top-[200px] w-[50px] h-[50px]"),
                s_presenceGone ? V.Div(key: "gone") : V.AnimatePresence(key: "presence", onExitComplete: () => s_exitCompletions++, children: s_modalOpen
                    ? new VNode[]
                    {
                        V.Motion(key: "modal", name: "modal", layoutId: "card", variants: s_fade, animate: "visible",
                            exit: "hidden", transition: s_modalTransition ?? s_slowTween,
                            className: $"absolute left-[{s_modalLeft}px] top-[0px] w-[100px] h-[100px]"),
                    }
                    : Array.Empty<VNode>()),
            });
        }

        // Mounts the card, opens the modal over it and lets its tween end, then closes it and plays the frame after.
        private MountedTree OpenAndCloseTheModal()
        {
            s_modalOpen = false;
            var mounted = V.Mount(Root, V.Component(CardModalRender, key: "root"));
            Tick();
            s_modalOpen = true;
            RenderShared(mounted);
            AdvancePast(1f);
            s_modalOpen = false;
            RenderShared(mounted);
            return mounted;
        }

        [Test]
        public void Given_AModalLeadingACardsLayoutId_When_ItsExitStarts_Then_TheCardIsShownTweeningFromTheModal()
        {
            // Arrange / Act
            using var mounted = OpenAndCloseTheModal();

            // Assert — drawn, and one frame into its tween back from 300px right.
            var card = Root.Q<VisualElement>("card");
            Assert.That((card.resolvedStyle.visibility, TranslateX(card) > 250f), Is.EqualTo((Visibility.Visible, true)));
        }

        [Test]
        public void Given_AModalWhoseExitHandedTheCardItsLayoutId_When_ItComesBackMidExit_Then_ItLeadsAgain()
        {
            // Arrange
            using var mounted = OpenAndCloseTheModal();

            // Act
            s_modalOpen = true;
            RenderShared(mounted);
            AdvancePast(1f);

            // Assert
            Assert.That((Root.Q<VisualElement>("modal").resolvedStyle.visibility, Root.Q<VisualElement>("card").resolvedStyle.visibility),
                Is.EqualTo((Visibility.Visible, Visibility.Hidden)));
        }

        // Opens the modal over the card and lets its tween end, closes it, and plays the given frames.
        private MountedTree CloseTheModalFor(int frames)
        {
            s_modalOpen = false;
            var mounted = V.Mount(Root, V.Component(CardModalRender, key: "root"));
            Tick();
            s_modalOpen = true;
            RenderShared(mounted);
            AdvancePast(1f);
            s_modalOpen = false;
            RenderShared(mounted);
            for (var i = 0; i < frames; i++) Tick();
            return mounted;
        }

        [Test]
        public void Given_AModalWithAShortExitRelegatingTheCard_When_ItsExitEnds_Then_ItStaysUntilTheCardLands()
        {
            // Arrange — a tenth-of-a-second modal over a card on its one-second tween.
            s_modalTransition = s_quickTween;
            using var mounted = CloseTheModalFor(15);
            var presentWhileTheCardMoves = Root.Q<VisualElement>("modal") != null;

            // Act
            AdvancePast(1f);

            // Assert
            Assert.That((presentWhileTheCardMoves, Root.Q<VisualElement>("modal") == null), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AModalWaitingForTheCardToLand_When_ItComesBackFirst_Then_NoExitCompletes()
        {
            // Arrange
            s_modalTransition = s_quickTween;
            using var mounted = CloseTheModalFor(15);

            // Act
            s_modalOpen = true;
            RenderShared(mounted);
            AdvancePast(1f);

            // Assert
            Assert.That(s_exitCompletions, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base removes a modal once its own exit has played.
        // A card that lands first must leave the modal to its exit.
        [Test]
        public void Given_AModalWithALongExitRelegatingTheCard_When_TheCardLandsFirst_Then_TheModalStaysForItsExit()
        {
            // Arrange / Act — the card on a tenth of a second, the modal half way through its one-second exit.
            s_cardTransition = s_quickTween;
            using var mounted = CloseTheModalFor(30);

            // Assert
            Assert.That(Root.Q<VisualElement>("modal"), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base removes a modal once its own exit has played.
        // A landing settles the exit once, however long another projection keeps the frame running.
        [Test]
        public void Given_AModalWithALongExitRelegatingTheCardWhileAnotherMotionMoves_When_TheCardLandsFirst_Then_TheModalStaysForItsExit()
        {
            // Arrange — the card on a tenth of a second, and a Motion under an id of its own set moving on a second as
            // the modal closes.
            (s_cardTransition, s_moverLeft, s_modalOpen) = (s_quickTween, 0, false);
            using var mounted = V.Mount(Root, V.Component(CardModalRender, key: "root"));
            Tick();
            s_modalOpen = true;
            RenderShared(mounted);
            AdvancePast(1f);

            // Act — half way through the modal's one-second exit, long after the card landed.
            (s_modalOpen, s_moverLeft) = (false, 300);
            RenderShared(mounted);
            for (var i = 0; i < 30; i++) Tick();

            // Assert
            Assert.That(Root.Q<VisualElement>("modal"), Is.Not.Null);
        }

        [Test]
        public void Given_AModalAtTheCardsBoxWithAShortExitRelegatingTheCard_When_ItsExitEnds_Then_ItStaysUntilTheCardLands()
        {
            // Arrange / Act — the bundled sheet, without which `absolute` leaves the modal in flow below the card; the
            // modal over the card's own box, on a tenth of a second; half way through the card's one-second tween,
            // which a promotion plays however little it moves.
            VelvetStyleUtilities.AttachTo(Root);
            (s_modalLeft, s_modalTransition) = (0, s_quickTween);
            using var mounted = CloseTheModalFor(30);

            // Assert
            Assert.That(Root.Q<VisualElement>("modal"), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base removes a modal once its own exit has played.
        // A card that took the lead without a tween leaves nothing to wait for.
        [Test]
        public void Given_ACardOnNoTransition_When_TheModalsExitEnds_Then_TheModalLeaves()
        {
            // Arrange
            s_cardTransition = StyleTransitionConfig.None;
            using var mounted = CloseTheModalFor(1);

            // Act
            AdvancePast(1f);

            // Assert
            Assert.That(Root.Q<VisualElement>("modal"), Is.Null);
        }

        [Test]
        public void Given_AModalWaitingForTheCardToLand_When_TheTreeIsUnmounted_Then_TheNextFramesCompleteNoExit()
        {
            // Arrange
            s_modalTransition = s_quickTween;
            var mounted = CloseTheModalFor(15);

            // Act
            mounted.Dispose();
            Tick();
            Tick();

            // Assert
            Assert.That(s_exitCompletions, Is.EqualTo(0));
        }

        [Test]
        public void Given_AModalWaitingForTheCardToLand_When_ItsAnimatePresenceIsRemoved_Then_NoExitCompletes()
        {
            // Arrange
            s_modalTransition = s_quickTween;
            using var mounted = CloseTheModalFor(15);

            // Act
            s_presenceGone = true;
            RenderShared(mounted);
            AdvancePast(1f);

            // Assert
            Assert.That(s_exitCompletions, Is.EqualTo(0));
        }

        // Set before each render of NestedModalRender: whether the modal is open.
        private static bool s_nestedModalOpen;

        // A card holding "card" with a title inside it holding "title", and a modal 300px right holding the same two
        // ids inside a V.AnimatePresence, with a one-second fade out as its exit.
        [Component]
        private static VNode NestedModalRender()
        {
            var (_, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            VNode Title(string name) => V.Motion(key: "title", name: name + "-title", layoutId: "title", transition: s_slowTween,
                className: "left-[10px] top-[10px] w-[50px] h-[20px]");
            return V.Div(children: new VNode[]
            {
                V.Motion(key: "card", name: "card", layoutId: "card", transition: s_slowTween,
                    className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]", children: new[] { Title("card") }),
                V.AnimatePresence(key: "presence", children: s_nestedModalOpen
                    ? new VNode[]
                    {
                        V.Motion(key: "modal", name: "modal", layoutId: "card", variants: s_fade, animate: "visible",
                            exit: "hidden", transition: s_slowTween,
                            className: "absolute left-[300px] top-[0px] w-[100px] h-[100px]", children: new[] { Title("modal") }),
                    }
                    : Array.Empty<VNode>()),
            });
        }

        // Opens the modal over the card and lets its tween end, then closes it and plays the given frames.
        private MountedTree CloseTheNestedModalFor(int frames)
        {
            s_nestedModalOpen = false;
            var mounted = V.Mount(Root, V.Component(NestedModalRender, key: "root"));
            Tick();
            s_nestedModalOpen = true;
            RenderShared(mounted);
            AdvancePast(1f);
            s_nestedModalOpen = false;
            RenderShared(mounted);
            for (var i = 0; i < frames; i++) Tick();
            return mounted;
        }

        [Test]
        public void Given_ACardAndAModalWithTitlesSharingIds_When_TheModalStartsItsExit_Then_TheCardsTitleDoesNotFadeAgain()
        {
            // Arrange / Act
            using var mounted = CloseTheNestedModalFor(10);

            // Assert — the card fading in, its title drawn under it at its own opacity.
            var title = Root.Q<VisualElement>("card-title");
            Assert.That((WrittenOpacity(Root.Q<VisualElement>("card")) < 1f, WrittenOpacity(title), title.resolvedStyle.visibility),
                Is.EqualTo((true, 1f, Visibility.Visible)));
        }

        [Test]
        public void Given_AClosingModalWithATitle_When_ItComesBackMidExit_Then_ItsTitleDoesNotFadeAgain()
        {
            // Arrange
            using var mounted = CloseTheNestedModalFor(1);

            // Act
            s_nestedModalOpen = true;
            RenderShared(mounted);
            for (var i = 0; i < 10; i++) Tick();

            // Assert — the modal fading in again, its title drawn under it at its own opacity.
            var title = Root.Q<VisualElement>("modal-title");
            Assert.That((WrittenOpacity(Root.Q<VisualElement>("modal")) < 1f, WrittenOpacity(title), title.resolvedStyle.visibility),
                Is.EqualTo((true, 1f, Visibility.Visible)));
        }

        [Test]
        public void Given_ACardOnNoTransition_When_TheModalLeadingItsLayoutIdStartsItsExit_Then_TheModalIsHidden()
        {
            // Arrange / Act
            s_cardTransition = StyleTransitionConfig.None;
            using var mounted = OpenAndCloseTheModal();

            // Assert
            Assert.That(Root.Q<VisualElement>("modal").resolvedStyle.visibility, Is.EqualTo(Visibility.Hidden));
        }

        // Set before each render of PresencePairRender: whether the card is mounted and whether each modal is open.
        private static bool s_cardMounted;
        private static bool s_m1Open;
        private static bool s_m2Open;

        // A card at the left, and two modals 300px and 600px right under the same id inside a V.AnimatePresence,
        // each with a one-second fade out as its exit.
        [Component]
        private static VNode PresencePairRender()
        {
            var (_, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            VNode Modal(string key, int left) => V.Motion(key: key, name: key, layoutId: "card", variants: s_fade,
                animate: "visible", exit: "hidden", transition: s_slowTween,
                className: $"absolute left-[{left}px] top-[0px] w-[100px] h-[100px]");
            var modals = new List<VNode>();
            if (s_m1Open) modals.Add(Modal("m1", 300));
            if (s_m2Open) modals.Add(Modal("m2", 600));
            var children = new List<VNode>();
            if (s_cardMounted)
            {
                children.Add(V.Motion(key: "card", name: "card", layoutId: "card", transition: s_slowTween,
                    className: "absolute left-[0px] top-[0px] w-[100px] h-[100px]"));
            }
            children.Add(V.AnimatePresence(key: "presence", children: modals.ToArray()));
            return V.Div(children: children.ToArray());
        }

        // Mounts PresencePairRender with the card as given, then opens the first modal and the second, letting
        // each one's tween end.
        private MountedTree OpenBothModals(bool withCard)
        {
            (s_cardMounted, s_m1Open, s_m2Open) = (withCard, false, false);
            var mounted = V.Mount(Root, V.Component(PresencePairRender, key: "root"));
            Tick();
            s_m1Open = true;
            RenderShared(mounted);
            AdvancePast(1f);
            s_m2Open = true;
            RenderShared(mounted);
            AdvancePast(1f);
            return mounted;
        }

        [Test]
        public void Given_TwoModalsOverACard_When_BothStartTheirExits_Then_TheCardTweensFromTheLeadingOne()
        {
            // Arrange
            using var mounted = OpenBothModals(withCard: true);

            // Act
            (s_m1Open, s_m2Open) = (false, false);
            RenderShared(mounted);

            // Assert — drawn, and one frame into its tween back from 600px right.
            var card = Root.Q<VisualElement>("card");
            Assert.That((card.resolvedStyle.visibility, TranslateX(card) > 500f), Is.EqualTo((Visibility.Visible, true)));
        }

        [Test]
        public void Given_TwoModalsOverACard_When_TheOneBehindTheLeadStartsItsExit_Then_TheCardTakesTheLead()
        {
            // Arrange
            using var mounted = OpenBothModals(withCard: true);

            // Act — Framer's NodeStack.relegate promotes the card whether or not the exiting modal led.
            s_m1Open = false;
            RenderShared(mounted);
            AdvancePast(1f);

            // Assert
            Assert.That((Root.Q<VisualElement>("card").resolvedStyle.visibility, Root.Q<VisualElement>("m2").resolvedStyle.visibility),
                Is.EqualTo((Visibility.Visible, Visibility.Hidden)));
        }

        [Test]
        public void Given_TwoModalsExitingWhileTheLaterLeads_When_TheLaterComesBack_Then_TheOtherStaysHidden()
        {
            // Arrange — no card, so the later one keeps the lead through its exit.
            using var mounted = OpenBothModals(withCard: false);
            (s_m1Open, s_m2Open) = (false, false);
            RenderShared(mounted);

            // Act
            s_m2Open = true;
            RenderShared(mounted);

            // Assert
            Assert.That(Root.Q<VisualElement>("m1").resolvedStyle.visibility, Is.EqualTo(Visibility.Hidden));
        }

        // Runs an action once, on the first panel tick that updates bindings.
        private sealed class OneShotBinding : IBinding
        {
            private readonly Action _act;

            public OneShotBinding(Action act) => _act = act;

            public bool Done { get; private set; }

            public void PreUpdate() { }

            public void Update()
            {
                if (Done) return;
                Done = true;
                _act();
            }

            public void Release() { }
        }

        // Step 1 grows the outer one; step 2 mounts "late" inside it with the bundled stylesheet's scale-150; step 3
        // removes "lead", which leads "held"'s id.
        [Component]
        private static VNode PromotionInsideGrowingRender()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var children = new List<VNode>
            {
                V.Motion(key: "held", name: "held", layoutId: "held-box", transition: s_layoutSpring,
                    className: "left-[20px] top-[20px] w-[50px] h-[50px]"),
            };
            if (step >= 2)
            {
                children.Add(V.Motion(key: "late", name: "late", layoutId: "late-box", transition: s_layoutSpring,
                    className: "left-[20px] top-[80px] w-[50px] h-[50px] scale-150"));
            }
            if (step < 3)
            {
                children.Add(V.Motion(key: "lead", name: "lead", layoutId: "held-box", transition: s_layoutSpring,
                    className: "left-[80px] top-[20px] w-[50px] h-[50px]"));
            }
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "outer", layoutId: "outer-box", transition: s_layoutSpring,
                    className: $"left-[0px] top-[0px] w-[{(step == 0 ? 300 : 600)}px] h-[{(step == 0 ? 300 : 600)}px]",
                    children: children.ToArray()),
            });
        }

        [Test]
        public void Given_AMotionMountedInsideOneStillGrowing_When_ARenderInThePanelsTickPromotesAnotherBeforeItIsLaidOut_Then_ItIsDrawnAtItsOwnScale()
        {
            // Arrange — the outer one part way through growing.
            VelvetStyleUtilities.AttachTo(Root);
            using var mounted = V.Mount(Root, V.Component(PromotionInsideGrowingRender, key: "root"));
            Tick();
            s_setStep.Invoke(1);
            mounted.FlushStateForTest();
            for (var i = 0; i < 4; i++) Tick();

            // Act — inside a panel tick, after its scheduler has run the frame that steps the projections and before its
            // style and layout passes: one render mounts "late", and the next removes "lead", handing its id to "held"
            // before "late" is laid out. A panel tick updates bindings between the two, at most every tenth of a second.
            var act = new OneShotBinding(() =>
            {
                s_setStep.Invoke(2);
                mounted.FlushStateForTest();
                s_setStep.Invoke(3);
                mounted.FlushStateForTest();
            });
            Root.Add(new Label { binding = act });
            for (var i = 0; i < 20 && !act.Done; i++) Tick();

            // Assert — its own one and a half, drawn inside an outer one whose scale its own undoes.
            var late = Root.Q<VisualElement>("late");
            var outer = Root.Q<VisualElement>("outer");
            Assert.That(act.Done ? late.style.scale.value.value.x * outer.style.scale.value.value.x : float.NaN, Is.EqualTo(1.5f).Within(0.01f));
        }

        [Test]
        public void Given_ALayoutIdMotionMountedInsideOneStillGrowing_When_ItIsFirstLaidOut_Then_ThatFrameDrawsItAtItsOwnSize()
        {
            // Arrange
            using var mounted = GrowOuterPartWay(flipInnerLate: false, lateClasses: "");

            // Act — the frame it is first laid out on.
            s_setStep.Invoke(2);
            mounted.FlushStateForTest();
            Tick();

            // Assert — its own 50px, though the outer one is still drawn short of its layout.
            var outerWidth = DrawnBox(Root.Q<VisualElement>("outer")).width;
            var lateWidth = DrawnBox(Root.Q<VisualElement>("late")).width;
            Assert.That((outerWidth < 550f, Mathf.Abs(lateWidth - 50f) < 1f), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base's layout transition lands a move at once on a spring the scheduler rejects.
        // A spring with no stiffness must land the move rather than hold it at the old box.
        [Test]
        public void Given_ALayoutIdMotionOnASpringTheValidatorRejects_When_ItMoves_Then_ItLandsAtOnce()
        {
            // Arrange / Act — a spring with no stiffness.
            var element = MoveOn(new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 0f, Damping = 10f, Mass = 1f });

            // Assert
            Assert.That(element.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // Builds a parent Motion (a PURE COORDINATOR: it declares no `variants` of its own, only `animate` +
        // `transition` — the orchestration must key off the label it PROPAGATES, not off its own resolved
        // class, since a coordinator like this never gets a MotionAppliedClasses entry) with two inheriting
        // children. child0Animate, when non-null, gives c0 its OWN explicit `animate` (opting it out of
        // inheriting the parent's label, and so out of this stagger — see test (f)).
        private static VNode[] Tree(
            string parentLabel, StyleTransitionConfig parentTransition,
            string child0Animate = null, StyleTransitionConfig childTransition = null)
        {
            childTransition ??= new StyleTransitionConfig { DurationSec = 0.15f };
            return new VNode[]
            {
                V.Motion(key: "p", name: "p", animate: parentLabel, transition: parentTransition,
                    children: new VNode[]
                    {
                        V.Motion(key: "c0", name: "c0", variants: s_fade, animate: child0Animate, transition: childTransition),
                        V.Motion(key: "c1", name: "c1", variants: s_fade, transition: childTransition),
                    }),
            };
        }

        // Whether the element's inline transition-duration is currently set — the runtime-swap play's own
        // tell (see MotionRuntimeSwapTests), used here to confirm a claimed swap actually started/settled.
        private static bool InlineDurationIsSet(VisualElement element)
        {
            var duration = element.style.transitionDuration;
            return duration.keyword != StyleKeyword.Null && duration.value != null && duration.value.Count > 0;
        }

        [Test]
        public void Given_AParentLabelFlipWithStaggerChildren_When_TheChildrenInheritTheNewLabel_Then_EachSwapsOnItsOwnIncreasingSlot()
        {
            // Arrange — mount with the parent hidden and no initial, so nothing enters and nothing has swapped
            // yet.
            var transition = new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.2f, StaggerChildrenSec = 0.1f };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition));
            Assume.That(Root.Q<VisualElement>("c1").ClassListContains("opacity-100"), Is.False,
                "Precondition: no orchestrated swap has fired on mount");

            // Act — flip the parent's label (the render that actually triggers orchestration). Sample at
            // 256ms (past child 0's 200ms slot, short of child 1's 300ms one), then again once both have
            // elapsed.
            _reconciler.Reconcile(Root, Tree("hidden", transition), Tree("visible", transition));
            for (var i = 0; i < 16; i++) Tick();
            var c0SwappedAtMidpoint = Root.Q<VisualElement>("c0").ClassListContains("opacity-100");
            var c1SwappedAtMidpoint = Root.Q<VisualElement>("c1").ClassListContains("opacity-100");
            AdvancePast(0.3f);
            var c1SwappedLate = Root.Q<VisualElement>("c1").ClassListContains("opacity-100");

            // Assert — child 0 claims index 0 (200ms = delayChildren + 0*stagger) and has already swapped
            // by 256ms; child 1 claims index 1 (300ms = delayChildren + 1*stagger) and has not yet, only
            // swapping once its own later slot elapses.
            Assert.That((c0SwappedAtMidpoint, c1SwappedAtMidpoint, c1SwappedLate), Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_DelayChildrenSecWithNoStagger_When_TheParentLabelFlips_Then_BothChildrenSwapAtTheSameFixedSlot()
        {
            // Arrange — isolate delayChildren's own contribution (StaggerChildrenSec = 0, so the per-index
            // term vanishes and every inheriting child should swap at exactly the same fixed slot).
            var transition = new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.5f, StaggerChildrenSec = 0f };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition));

            // Act — flip the parent's label. Sample shortly before the shared 500ms slot (neither should
            // have swapped) and again once it has elapsed.
            _reconciler.Reconcile(Root, Tree("hidden", transition), Tree("visible", transition));
            for (var i = 0; i < 28; i++) Tick();
            var beforeSlot = (Root.Q<VisualElement>("c0").ClassListContains("opacity-100"),
                Root.Q<VisualElement>("c1").ClassListContains("opacity-100"));
            AdvancePast(0.2f);
            var afterSlot = (Root.Q<VisualElement>("c0").ClassListContains("opacity-100"),
                Root.Q<VisualElement>("c1").ClassListContains("opacity-100"));

            // Assert — both children are still un-swapped right up to the shared 500ms slot, then both
            // have swapped once it elapses: the same fixed delay regardless of stagger index.
            Assert.That((beforeSlot, afterSlot), Is.EqualTo(((false, false), (true, true))));
        }

        [Test]
        public void Given_WhenIsBeforeChildren_When_TheParentLabelFlips_Then_TheChildDoesNotSwapUntilTheParentsOwnDurationElapses()
        {
            // Arrange — no delayChildren/staggerChildren, isolating BeforeChildren's own contribution:
            // children wait for the parent's own 400ms swap to finish before starting theirs.
            var transition = new StyleTransitionConfig { DurationSec = 0.4f, When = TransitionWhen.BeforeChildren };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition));

            // Act — flip the parent's label. Sample shortly before the 400ms slot and again once it elapses.
            _reconciler.Reconcile(Root, Tree("hidden", transition), Tree("visible", transition));
            for (var i = 0; i < 23; i++) Tick();
            var beforeSlot = Root.Q<VisualElement>("c0").ClassListContains("opacity-100");
            AdvancePast(0.2f);
            var afterSlot = Root.Q<VisualElement>("c0").ClassListContains("opacity-100");

            // Assert — the inheriting child does not swap until exactly the parent's own DurationSec has
            // elapsed.
            Assert.That((beforeSlot, afterSlot), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_BeforeChildrenWithAParentDelay_When_TheLabelFlips_Then_TheChildWaitsForTheDelayAndTheDuration()
        {
            // Arrange — the parent's own swap spans [DelaySec, DelaySec + DurationSec]; BeforeChildren
            // means children start after it ENDS, so the parent's DelaySec must be part of the wait.
            var transition = new StyleTransitionConfig
            {
                DurationSec = 0.4f,
                DelaySec = 0.2f,
                When = TransitionWhen.BeforeChildren,
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition));

            // Act — flip the parent's label. Sample shortly before the 600ms slot (DelaySec + DurationSec)
            // and again once it elapses.
            _reconciler.Reconcile(Root, Tree("hidden", transition), Tree("visible", transition));
            for (var i = 0; i < 35; i++) Tick();
            var beforeSlot = Root.Q<VisualElement>("c0").ClassListContains("opacity-100");
            AdvancePast(0.2f);
            var afterSlot = Root.Q<VisualElement>("c0").ClassListContains("opacity-100");

            // Assert — 600ms = the parent's DelaySec (200) plus its DurationSec (400); the child does not
            // swap until both have elapsed.
            Assert.That((beforeSlot, afterSlot), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AnInheritingOrchestratorWithItsOwnChildStagger_When_TheAncestorLabelFlips_Then_TheGrandchildWaitsForBothDelays()
        {
            // Arrange — "mid" both CLAIMS a delay from "gp"'s orchestration (it inherits gp's label and
            // declares its own Variants, so it actually claims a stagger slot) and ESTABLISHES a fresh
            // orchestration frame for "gc" (its own Transition declares DelayChildrenSec). "gc"'s total delay
            // must be measured from render-commit time, not from when "mid"'s own already-delayed swap starts,
            // or the grandchild would start animating before its own parent's swap even begins.
            var midVariants = new Dictionary<string, MotionVariant> { ["hidden"] = "translate-x-0", ["visible"] = "translate-x-4" };
            VNode[] NestedTree(string label) => new VNode[]
            {
                V.Motion(key: "gp", name: "gp", animate: label,
                    transition: new StyleTransitionConfig { DurationSec = 0.1f, DelayChildrenSec = 0.5f },
                    children: new VNode[]
                    {
                        V.Motion(key: "mid", name: "mid", variants: midVariants,
                            transition: new StyleTransitionConfig { DurationSec = 0.1f, DelayChildrenSec = 0.25f },
                            children: new VNode[]
                            {
                                V.Motion(key: "gc", name: "gc", variants: s_fade,
                                    transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                            }),
                    }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), NestedTree("hidden"));
            Assume.That(Root.Q<VisualElement>("gc").ClassListContains("opacity-100"), Is.False,
                "Precondition: no orchestrated swap has fired on mount");

            // Act — flip the top ancestor's label. Sample shortly before the 750ms slot and again once it
            // elapses.
            _reconciler.Reconcile(Root, NestedTree("hidden"), NestedTree("visible"));
            for (var i = 0; i < 45; i++) Tick();
            var beforeSlot = Root.Q<VisualElement>("gc").ClassListContains("opacity-100");
            AdvancePast(0.2f);
            var afterSlot = Root.Q<VisualElement>("gc").ClassListContains("opacity-100");

            // Assert — 750ms = gp's delayChildren (500ms, claimed by "mid") + mid's OWN delayChildren
            // (250ms), folded together rather than measuring mid's fresh frame from zero; the grandchild
            // does not swap until both have elapsed.
            Assert.That((beforeSlot, afterSlot), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_InheritingChildrenWithNoOrchestrationOfTheirOwn_When_TheAncestorLabelFlips_Then_EachGrandchildSwapsWithItsOwnParent()
        {
            // Arrange — Framer numbers each variant parent's own children: mid0 and mid1 take gp's slots 0 and 1,
            // and each gc starts with its own mid rather than taking a later slot of gp's sequence.
            VNode Mid(int i) => V.Motion(key: "mid" + i, name: "mid" + i, variants: s_fade,
                transition: new StyleTransitionConfig { DurationSec = 0.05f },
                children: new VNode[]
                {
                    V.Motion(key: "gc" + i, name: "gc" + i, variants: s_fade,
                        transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                });
            VNode[] NestedTree(string label) => new VNode[]
            {
                V.Motion(key: "gp", name: "gp", animate: label,
                    transition: new StyleTransitionConfig { DurationSec = 0.1f, StaggerChildrenSec = 0.4f },
                    children: new[] { Mid(0), Mid(1) }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), NestedTree("hidden"));

            // Act — flip the ancestor, then sample past slot 0 and inside slot 1's 0.4s.
            _reconciler.Reconcile(Root, NestedTree("hidden"), NestedTree("visible"));
            for (var i = 0; i < 12; i++) Tick();

            // Assert — gc0 swapped with mid0, and mid1 and gc1 are both still parked on slot 1.
            Assert.That(
                (Root.Q<VisualElement>("mid0").ClassListContains("opacity-100"),
                 Root.Q<VisualElement>("gc0").ClassListContains("opacity-100"),
                 Root.Q<VisualElement>("mid1").ClassListContains("opacity-100"),
                 Root.Q<VisualElement>("gc1").ClassListContains("opacity-100")),
                Is.EqualTo((true, true, false, false)));
        }

        [Test]
        public void Given_AChildNamingItsOwnExitUnderAnOrchestrator_When_TheLabelFlips_Then_ItTakesNoSlotFromItsSibling()
        {
            // Arrange — Framer's isControllingVariants counts an own exit label, so own-exit is no variant child
            // and c1 is gp's first.
            VNode[] NestedTree(string label) => new VNode[]
            {
                V.Motion(key: "gp", name: "gp", animate: label,
                    transition: new StyleTransitionConfig { DurationSec = 0.1f, StaggerChildrenSec = 0.4f },
                    children: new VNode[]
                    {
                        V.Motion(key: "own-exit", name: "own-exit", variants: s_fade, exit: "hidden",
                            transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                        V.Motion(key: "c1", name: "c1", variants: s_fade,
                            transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                    }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), NestedTree("hidden"));

            // Act
            _reconciler.Reconcile(Root, NestedTree("hidden"), NestedTree("visible"));
            for (var i = 0; i < 12; i++) Tick();

            // Assert
            Assert.That(Root.Q<VisualElement>("c1").ClassListContains("opacity-100"), Is.True);
        }

        // GREEN_ON_BASE(characterization): a Motion with neither variants nor an animate already passed the
        // orchestration through to the children below it.
        [Test]
        public void Given_AMotionWithNoVariantsBetweenTheOrchestratorAndItsChildren_When_TheLabelFlips_Then_TheChildrenKeepTheOrchestratorsSequence()
        {
            // Arrange — Framer registers a variant child with the nearest variant node above it, so c0 and c1
            // are gp's first and second children however many plain Motions sit between.
            VNode[] NestedTree(string label) => new VNode[]
            {
                V.Motion(key: "gp", name: "gp", animate: label,
                    transition: new StyleTransitionConfig { DurationSec = 0.1f, StaggerChildrenSec = 0.4f },
                    children: new VNode[]
                    {
                        V.Motion(key: "plain", name: "plain", children: new VNode[]
                        {
                            V.Motion(key: "c0", name: "c0", variants: s_fade,
                                transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                            V.Motion(key: "c1", name: "c1", variants: s_fade,
                                transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                        }),
                    }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), NestedTree("hidden"));

            // Act
            _reconciler.Reconcile(Root, NestedTree("hidden"), NestedTree("visible"));
            for (var i = 0; i < 12; i++) Tick();

            // Assert
            Assert.That(
                (Root.Q<VisualElement>("c0").ClassListContains("opacity-100"),
                 Root.Q<VisualElement>("c1").ClassListContains("opacity-100")),
                Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): a Motion with its own animate already kept an outer orchestration
        // from the children it drives.
        [Test]
        public void Given_AMotionWithItsOwnAnimateAndNoVariantsUnderAnOrchestrator_When_BothLabelsFlip_Then_ItsChildSwapsWithoutTheOuterDelay()
        {
            // Arrange — coord drives gc with its own label, so gc is coord's child and not gp's.
            VNode[] NestedTree(string label) => new VNode[]
            {
                V.Motion(key: "gp", name: "gp", animate: label,
                    transition: new StyleTransitionConfig { DurationSec = 0.1f, DelayChildrenSec = 0.5f },
                    children: new VNode[]
                    {
                        V.Motion(key: "coord", name: "coord", animate: label, children: new VNode[]
                        {
                            V.Motion(key: "gc", name: "gc", variants: s_fade,
                                transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                        }),
                    }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), NestedTree("hidden"));

            // Act — well short of gp's 500ms.
            _reconciler.Reconcile(Root, NestedTree("hidden"), NestedTree("visible"));
            for (var i = 0; i < 6; i++) Tick();

            // Assert
            Assert.That(Root.Q<VisualElement>("gc").ClassListContains("opacity-100"), Is.True);
        }

        [Test]
        public void Given_ASwapDeclaringAfterChildren_When_ItPropagatesALabelToItsChildren_Then_TheUnorchestratedWaitIsDiagnosed()
        {
            // Arrange — AfterChildren is accepted by the config but not yet orchestrated for label
            // propagation, so the swap that would have to wait says so instead of quietly animating early.
            // StaggerChildrenSec is set too, which opens the orchestration gate on its own: that leaves this
            // case measuring which When value is diagnosed rather than whether the gate above it opened.
            var diagnosed = 0;
            void OnLog(string condition, string stackTrace, UnityEngine.LogType type)
            {
                if (type == UnityEngine.LogType.Warning && condition.Contains("When = AfterChildren"))
                {
                    diagnosed++;
                }
            }

            VNode[] Tree(string label) => new VNode[]
            {
                V.Motion(key: "p", animate: label,
                    transition: new StyleTransitionConfig
                    {
                        DurationSec = DurationSec,
                        StaggerChildrenSec = 0.4f,
                        When = TransitionWhen.AfterChildren,
                    },
                    children: new VNode[]
                    {
                        V.Motion(key: "c", variants: s_fade,
                            transition: new StyleTransitionConfig { DurationSec = DurationSec }),
                    }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden"));

            // Act
            UnityEngine.Application.logMessageReceived += OnLog;
            try
            {
                _reconciler.Reconcile(Root, Tree("hidden"), Tree("visible"));
            }
            finally
            {
                UnityEngine.Application.logMessageReceived -= OnLog;
            }

            // Assert — said once, for the one parent whose propagated label changed.
            Assert.That(diagnosed, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildWithItsOwnExplicitAnimate_When_TheParentLabelFlips_Then_ItNeverPlaysButItsSiblingIsDelayed()
        {
            // Arrange — c0 declares its OWN explicit animate ("visible", fixed across both trees below),
            // opting it out of inheriting the parent's ambient label and so out of this stagger; c1 declares no
            // own animate and inherits normally, so it MUST still be delayed regardless of which stagger index
            // it claims (c0 never calls into the shared counter at all, since it never satisfies the ambient-
            // following gate) — DelayChildrenSec alone (no stagger) makes that unambiguous.
            var transition = new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.2f };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition, child0Animate: "visible"));

            // Act — flip the parent's label. c1's own resolved variant changes (hidden -> visible) and must
            // wait for its claimed 200ms slot; c0's never changes (fixed at "visible" throughout), so no
            // runtime-swap play is ever triggered for it at all.
            _reconciler.Reconcile(Root, Tree("hidden", transition, child0Animate: "visible"),
                Tree("visible", transition, child0Animate: "visible"));
            var c1SwappedImmediately = Root.Q<VisualElement>("c1").ClassListContains("opacity-100");
            AdvancePast(0.2f);
            var c0NeverPlayed = !InlineDurationIsSet(Root.Q<VisualElement>("c0"));
            var c1SwappedAfterItsSlot = Root.Q<VisualElement>("c1").ClassListContains("opacity-100");

            // Assert — c0 (own explicit animate) never got a runtime-swap play at all; c1 (ambient-inheriting)
            // did not swap immediately and only reached its target once its claimed delay elapsed.
            Assert.That((c0NeverPlayed, c1SwappedImmediately, c1SwappedAfterItsSlot), Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_AnOrchestratedSwapOnAPanel_When_ItsTransitionWouldHaveFinished_Then_TheInlineTransitionStylesClearAutomatically()
        {
            // Arrange — the same parent-flip scenario as the tests above; c0's runtime-swap play is claimed
            // behind a non-zero orchestrated delay.
            var transition = new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.2f, StaggerChildrenSec = 0.1f };
            var childTransition = new StyleTransitionConfig { DurationSec = 0.1f };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition, childTransition: childTransition));
            _reconciler.Reconcile(Root, Tree("hidden", transition, childTransition: childTransition),
                Tree("visible", transition, childTransition: childTransition));
            var c0 = Root.Q<VisualElement>("c0");
            Assume.That(InlineDurationIsSet(c0), Is.True, "Precondition: the runtime-swap play set its inline transition");

            // Act — advance the simulated clock well past this child's claimed delay (200ms) + its own swap
            // duration (100ms).
            AdvancePast(0.2f + 0.1f);

            // Assert — the completion cleanup fired and released the inline transition styles.
            Assert.That(InlineDurationIsSet(c0), Is.False);
        }

        [Test]
        public void Given_AStandaloneMotionWithInitial_When_Mounted_Then_ItStartsAtTheInitialVariant()
        {
            // Arrange / Act — no AnimatePresence anywhere: initial/animate must still drive the mount enter.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(name: "m", variants: s_fade, initial: "hidden", animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = DurationSec }),
            });

            // Assert — starts at variants[initial]=opacity-0; variants[animate]=opacity-100 is stripped during
            // the from-frame (swapped back in, and kept on completion — see the next two tests).
            var element = Root.Q<VisualElement>("m");
            Assert.That((element.ClassListContains("opacity-0"), element.ClassListContains("opacity-100")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AStandaloneMotionWithoutInitial_When_Mounted_Then_ItStartsAtTheAnimateVariantWithNoTweenScheduled()
        {
            // Arrange / Act — no `initial` declared, so there is no starting pose to enter FROM.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(name: "m", variants: s_fade, animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = DurationSec }),
            });

            // Assert — rests directly at variants[animate], and (unlike the `initial` case above) no transition
            // was scheduled: no inline transition-duration was ever applied.
            var element = Root.Q<VisualElement>("m");
            Assert.That(
                (element.ClassListContains("opacity-100"), element.style.transitionDuration.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Component]
        private static VNode StandaloneHostRender()
        {
            var (_, bump) = Hooks.UseState(0);
            s_bump = bump;
            return V.Motion(name: "m", variants: s_fade, initial: "hidden", animate: "visible",
                transition: new StyleTransitionConfig { DurationSec = DurationSec });
        }

        [Test]
        public void Given_AStandaloneMotionThatFinishedEntering_When_AnUnrelatedStateChangeReRenders_Then_ItKeepsTheAnimateVariant()
        {
            // Arrange — mount under a component (so a self-contained state update can re-render it), and let
            // the enter complete: it rests at variants[animate], persistently.
            using var mounted = V.Mount(Root, V.Component(StandaloneHostRender, key: "host"));
            AdvancePast(DurationSec);
            var element = Root.Q<VisualElement>("m");
            Assume.That(
                (element.ClassListContains("opacity-100"), element.ClassListContains("opacity-0")),
                Is.EqualTo((true, false)),
                "Precondition: the enter completed and rests at variants[animate]");

            // Act — an UNRELATED state change re-renders the same Motion node through PatchMotion (not
            // CreateElement again), which resolves the applied classes from Animate/ambient only.
            s_bump.Invoke(1);
            Tick();

            // Assert — the patch never replays `initial`: the element keeps resting at variants[animate].
            Assert.That(
                (element.ClassListContains("opacity-100"), element.ClassListContains("opacity-0")),
                Is.EqualTo((true, false)));
        }

        // The dangerous shape is production's own: the mount runs inside the panel's timer tick (the
        // batch scheduler's drain is itself a scheduled item), the enter's swap is registered on a
        // freshly attached element, and a zero-delay swap item then becomes runnable in the very tick
        // that mounted the element.
        private VisualElement StartEnterInsideATimerTick(StyleAnimationScheduler scheduler)
        {
            var element = new VisualElement();
            Root.Add(element);
            element.AddToClassList("opacity-100");
            Tick();
            element.schedule.Execute(() =>
            {
                scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                    durationSec: 0.3f, easing: EasingMode.EaseInOut, delaySec: 0f);
            });
            return element;
        }

        [Test]
        public void Given_AVariantEnterStartedInsideATimerTick_When_ThatTickEnds_Then_TheFromStateIsStillApplied()
        {
            // Arrange — mirror production: the enter's step 1 (strip to-classes, apply from-classes,
            // schedule the swap) runs inside the panel's own timer tick.
            var scheduler = new StyleAnimationScheduler();
            var element = StartEnterInsideATimerTick(scheduler);

            // Act — the single tick that both starts the enter and drains the timer queue.
            Tick();

            // Assert — the from-state must survive the tick that started the enter; a swap that ran
            // in the same tick strips it before the panel computes it once, so the transition sees
            // no change and the enter degenerates to an instant jump.
            Assert.That(element.ClassListContains("opacity-0"), Is.True);
        }

        [Test]
        public void Given_AVariantEnterStartedInsideATimerTick_When_TheNextTickRuns_Then_TheSwapReachesTheAnimateState()
        {
            // Arrange — same production shape as above.
            var scheduler = new StyleAnimationScheduler();
            var element = StartEnterInsideATimerTick(scheduler);
            Tick();
            Assume.That(element.ClassListContains("opacity-0"), Is.True,
                "Precondition: the from-state survived the starting tick");

            // Act — the next tick is where the deferred swap belongs.
            Tick();

            // Assert — the swap did fire on the following tick (the enter must still make progress,
            // not park the from-state forever).
            Assert.That(element.ClassListContains("opacity-0"), Is.False);
        }

        [Component]
        private static VNode LateMountHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "late-" + key, key: key.ToString(), variants: s_fade,
                    initial: "hidden", animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }));
            }
            return V.Div(name: "host", children: children.ToArray());
        }

        [Test]
        public void Given_AMotionMountedByATimerTickDrain_When_ThatTickEnds_Then_TheFromStateIsStillApplied()
        {
            // Arrange — mount the host and settle, then dirty the store WITHOUT a manual drain, so
            // the new Motion's whole mount (create detached -> play enter -> attach) happens inside
            // the panel's own timer tick via the batch scheduler's scheduled drain, exactly like
            // production. The enter's zero-delay swap item is attached mid-tick with its deadline
            // already reached.
            using var store = new SetStore("");
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(LateMountHost, key: "root"));
            Tick();
            store.Set("a");

            // Act — the single tick that both drains the batch (mounting the Motion) and the timer
            // queue (where the just-scheduled swap must NOT yet run).
            Tick();

            // Assert — the from-state survived its mounting tick; swapping in the same tick would
            // strip it before its first style pass and the enter would play as an instant jump.
            Assert.That(Root.Q<VisualElement>("late-a").ClassListContains("opacity-0"), Is.True);
        }
    }
}
