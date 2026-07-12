using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <c>StyleTransitionConfig.StaggerChildrenSec</c> / <c>DelayChildrenSec</c> / <c>When</c>: PLAIN
    /// parent → child variant-tree orchestration — no AnimatePresence involved, just a Motion's <c>animate</c>
    /// label flipping while descendants inherit it via <c>variants</c> (see
    /// <c>MotionVariantPropagationTests.Given_AChildInheritingHidden_...</c>). A descendant that follows the
    /// ambient label (no own <c>animate</c>) claims a sequential slot and gets an inline
    /// <c>transition-delay</c> of <c>DelayChildrenSec + StaggerChildrenSec * index</c> (plus the parent's own
    /// <c>DurationSec</c> when <c>When == BeforeChildren</c>), ADDED on top of whatever it already declares via
    /// its own <c>Transition.DelaySec</c>. A descendant with its OWN explicit <c>animate</c> opts out entirely
    /// (Framer parity: an explicit override disconnects a component from its parent's variant propagation).
    /// The delay is delivered as a plain inline style (the class swap itself is a synchronous class diff, not a
    /// scheduler-driven enter/exit) and is cleared once its own swap's transition would have finished, so a
    /// later, unrelated patch on the same element does not inherit a stale delay.
    /// </summary>
    /// <remarks>
    /// Needs a REAL (simulated) panel: the orchestrated delay clears via <c>schedule.Execute().ExecuteLater(ms)</c>,
    /// which only fires once a panel ticks its scheduler against its clock (the batchmode EditMode PlayerLoop
    /// never does) — off-panel, the same clear fires IMMEDIATELY instead (mirroring
    /// <c>StyleAnimationScheduler.CancelExit</c>'s own off-panel-immediate-clear contract for a reversal
    /// cleanup), which would make the delay unobservable synchronously. <see cref="EditorPanelSimulator"/> ticks
    /// the panel deterministically instead (see <see cref="Tick"/> / <see cref="AdvancePast"/>).
    /// </remarks>
    [TestFixture]
    internal sealed class MotionOrchestrationTests
    {
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private EditorPanelSimulator _sim;
        private Reconciler _reconciler;

        [SetUp]
        public void SetUp()
        {
            // Simulated time (and the per-frame step) are process-static and not auto-reset between tests;
            // reset both so this fixture's frame accounting starts from a known clock regardless of what a
            // sibling simulator-based fixture left behind.
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            _reconciler = new Reconciler();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler?.Dispose();
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        // One frame (a real-frame-sized scheduler tick).
        private void Tick() => _sim.FrameUpdateMs(16);

        // Advances well past the given duration so any scheduled callback due by then fires; the +0.2s margin
        // absorbs the scheduler's internal grace period without coupling to its exact value.
        private void AdvancePast(float seconds)
        {
            var steps = (int)((seconds + 0.2f) * 1000f / 16f) + 1;
            for (var i = 0; i < steps; i++) Tick();
        }

        // Builds a parent Motion (a PURE COORDINATOR: it declares no `variants` of its own, only `animate` +
        // `transition` — the orchestration must key off the label it PROPAGATES, not off its own resolved
        // class, since a coordinator like this never gets a MotionAppliedClasses entry) with two inheriting
        // children. child0Animate, when non-null, gives c0 its OWN explicit `animate` (opting it out of
        // inheriting the parent's label, and so out of this stagger — see test (d)).
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

        // Reads the element's inline transition-delay as milliseconds; unset (StyleKeyword.Null, or an empty
        // list) reads as 0, matching CSS's own "no delay" default.
        private static float InlineDelayMs(VisualElement element)
        {
            var delay = element.style.transitionDelay;
            return delay.keyword == StyleKeyword.Null || delay.value == null || delay.value.Count == 0
                ? 0f
                : delay.value[0].value;
        }

        [Test]
        public void Given_AParentLabelFlipWithStaggerChildren_When_TheChildrenInheritTheNewLabel_Then_EachGetsAnIncreasingDelay()
        {
            // Arrange — mount with the parent hidden (orchestration only ever starts from a PATCH-time label
            // change, never on mount — see FiberNodePatcher.PatchMotion), so nothing carries a delay yet.
            var transition = new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.2f, StaggerChildrenSec = 0.1f };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition));
            Assume.That(InlineDelayMs(Root.Q<VisualElement>("c1")), Is.EqualTo(0f),
                "Precondition: no orchestrated delay is applied on mount");

            // Act — flip the parent's label (the render that actually triggers orchestration).
            _reconciler.Reconcile(Root, Tree("hidden", transition), Tree("visible", transition));

            // Assert — child 0 claims index 0 (200ms = delayChildren + 0*stagger), child 1 claims index 1
            // (300ms = delayChildren + 1*stagger), each ADDED on top of the child's own (unset, 0) DelaySec.
            var c0 = InlineDelayMs(Root.Q<VisualElement>("c0"));
            var c1 = InlineDelayMs(Root.Q<VisualElement>("c1"));
            Assert.That((c0, c1), Is.EqualTo((200f, 300f)).Within(1e-3f));
        }

        [Test]
        public void Given_DelayChildrenSecWithNoStagger_When_TheParentLabelFlips_Then_BothChildrenGetTheSameFixedDelay()
        {
            // Arrange — isolate delayChildren's own contribution (StaggerChildrenSec = 0, so the per-index term
            // vanishes and every inheriting child should be delayed by exactly the same fixed amount).
            var transition = new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.5f, StaggerChildrenSec = 0f };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition));

            // Act
            _reconciler.Reconcile(Root, Tree("hidden", transition), Tree("visible", transition));

            // Assert — both children are delayed by the same fixed 500ms, regardless of stagger index.
            var c0 = InlineDelayMs(Root.Q<VisualElement>("c0"));
            var c1 = InlineDelayMs(Root.Q<VisualElement>("c1"));
            Assert.That((c0, c1), Is.EqualTo((500f, 500f)).Within(1e-3f));
        }

        [Test]
        public void Given_WhenIsBeforeChildren_When_TheParentLabelFlips_Then_TheParentsOwnDurationIsAddedToTheChildDelay()
        {
            // Arrange — no delayChildren/staggerChildren, isolating BeforeChildren's own contribution: children
            // wait for the parent's own 400ms swap to finish before starting theirs.
            var transition = new StyleTransitionConfig { DurationSec = 0.4f, When = TransitionWhen.BeforeChildren };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition));

            // Act
            _reconciler.Reconcile(Root, Tree("hidden", transition), Tree("visible", transition));

            // Assert — the inheriting child is delayed by exactly the parent's own DurationSec.
            Assert.That(InlineDelayMs(Root.Q<VisualElement>("c0")), Is.EqualTo(400f).Within(1e-3f));
        }

        [Test]
        public void Given_BeforeChildrenWithAParentDelay_When_TheLabelFlips_Then_ChildrenWaitForTheDelayAndTheDuration()
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

            // Act
            _reconciler.Reconcile(Root, Tree("hidden", transition), Tree("visible", transition));

            // Assert — 600ms = the parent's DelaySec (200) plus its DurationSec (400).
            Assert.That(InlineDelayMs(Root.Q<VisualElement>("c0")), Is.EqualTo(600f).Within(1e-3f));
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
            Assume.That(InlineDelayMs(Root.Q<VisualElement>("mid")), Is.EqualTo(0f),
                "Precondition: no orchestrated delay is applied on mount");

            // Act — flip the top ancestor's label.
            _reconciler.Reconcile(Root, NestedTree("hidden"), NestedTree("visible"));

            // Assert — 750ms = gp's delayChildren (500ms, claimed by "mid") + mid's OWN delayChildren (250ms),
            // folded together rather than measuring mid's fresh frame from zero.
            Assert.That(InlineDelayMs(Root.Q<VisualElement>("gc")), Is.EqualTo(750f).Within(1e-3f));
        }

        [Test]
        public void Given_AChildWithItsOwnExplicitAnimate_When_TheParentLabelFlips_Then_ItIsNotDelayedButItsSiblingIs()
        {
            // Arrange — c0 declares its OWN explicit animate ("visible", fixed across both trees below),
            // opting it out of inheriting the parent's ambient label and so out of this stagger; c1 declares no
            // own animate and inherits normally, so it MUST still be delayed regardless of which stagger index
            // it claims (c0 never calls into the shared counter at all, since it never satisfies the ambient-
            // following gate) — DelayChildrenSec alone (no stagger) makes that unambiguous.
            var transition = new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.2f };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition, child0Animate: "visible"));

            // Act
            _reconciler.Reconcile(Root, Tree("hidden", transition, child0Animate: "visible"),
                Tree("visible", transition, child0Animate: "visible"));

            // Assert — c0 (own explicit animate) carries no delay; c1 (ambient-inheriting) does.
            var c0HasNoDelay = InlineDelayMs(Root.Q<VisualElement>("c0")) == 0f;
            var c1IsDelayed = InlineDelayMs(Root.Q<VisualElement>("c1")) > 0f;
            Assert.That((c0HasNoDelay, c1IsDelayed), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AnOrchestratedDelayOnAPanel_When_TheSwapsTransitionWouldHaveFinished_Then_TheInlineDelayClearsAutomatically()
        {
            // Arrange — the same parent-flip scenario as the tests above, applying a non-zero inline delay to c0.
            var transition = new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.2f, StaggerChildrenSec = 0.1f };
            var childTransition = new StyleTransitionConfig { DurationSec = 0.1f };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree("hidden", transition, childTransition: childTransition));
            _reconciler.Reconcile(Root, Tree("hidden", transition, childTransition: childTransition),
                Tree("visible", transition, childTransition: childTransition));
            var c0 = Root.Q<VisualElement>("c0");
            Assume.That(InlineDelayMs(c0), Is.GreaterThan(0f), "Precondition: the swap applied a non-zero inline delay");

            // Act — advance the simulated clock well past this child's delay (200ms) + its own duration (100ms).
            AdvancePast(0.2f + 0.1f);

            // Assert — the deferred clear fired and removed the inline transition-delay.
            Assert.That(c0.style.transitionDelay.keyword, Is.EqualTo(StyleKeyword.Null));
        }
    }
}
