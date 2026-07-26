using System;
using System.Collections.Generic;
using NUnit.Framework;
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

        private static readonly Dictionary<string, string> s_fade = new()
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
            s_bump = null;
            s_store = null;
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
            // Arrange — mount with the parent hidden (orchestration only ever starts from a PATCH-time label
            // change, never on mount — see FiberNodePatcher.PatchMotion), so nothing has swapped yet.
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
            var midVariants = new Dictionary<string, string> { ["hidden"] = "translate-x-0", ["visible"] = "translate-x-4" };
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
