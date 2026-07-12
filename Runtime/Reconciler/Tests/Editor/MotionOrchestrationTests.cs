using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <c>StyleTransitionConfig.StaggerChildrenSec</c> / <c>DelayChildrenSec</c> / <c>When</c>: PLAIN
    /// parent → child variant-tree orchestration — no AnimatePresence involved, just a Motion's <c>animate</c>
    /// label flipping while descendants inherit it via <c>variants</c> (see
    /// <c>MotionVariantPropagationTests.Given_AChildInheritingHidden_...</c>). A descendant that follows the
    /// ambient label (no own <c>animate</c>) claims a sequential slot — <c>DelayChildrenSec + StaggerChildrenSec
    /// * index</c> (plus the parent's own <c>DurationSec</c> when <c>When == BeforeChildren</c>) — ADDED on top
    /// of whatever it already declares via its own <c>Transition.DelaySec</c>. That claim rides as the
    /// <c>StyleAnimationScheduler</c> runtime-swap play's <c>additionalDelaySec</c> (see
    /// <c>FiberNodePatcher.PatchMotion</c> / <c>MotionRuntimeSwapTests</c>): the claim delays the SWAP itself —
    /// the target classes land only once the slot elapses — rather than parking an inline
    /// <c>transition-delay</c> for utility classes that may not even declare a transition. A descendant with
    /// its OWN explicit <c>animate</c> opts out entirely (Framer parity: an explicit override disconnects a
    /// component from its parent's variant propagation) — and, since its own resolved variant never changes
    /// as a result, never plays a swap at all.
    /// </summary>
    /// <remarks>
    /// Needs a REAL (simulated) panel — see <see cref="MotionSimulatedPanelTestsBase"/> — for the orchestrated
    /// swap, which only fires once a panel ticks its scheduler against its own clock (the batchmode EditMode
    /// PlayerLoop never does).
    /// </remarks>
    [TestFixture]
    internal sealed class MotionOrchestrationTests : MotionSimulatedPanelTestsBase
    {
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

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
    }
}
