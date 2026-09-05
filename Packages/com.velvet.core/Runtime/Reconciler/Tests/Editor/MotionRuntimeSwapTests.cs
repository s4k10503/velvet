using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the runtime variant-swap path — a mounted Motion whose effective animate label changes,
    /// directly or through label inheritance — to the SAME transition semantics as mount enters and
    /// presence exits: the swap must ride the Motion's own <see cref="StyleTransitionConfig"/>
    /// (inline duration/easing plus any orchestrated stagger delay for tweens; the spring integrator
    /// for spring configs) instead of applying the class diff instantly and relying on whatever
    /// transition utilities the element happens to declare. Framer applies <c>transition</c> to
    /// every animate update, so a config-carrying label flip that snaps is a parity break. Because
    /// the swap is deferred (not applied inside the patch), an interrupted flip must still settle at
    /// the latest resting variant with the inline slots released, and a stagger-parked swap must
    /// survive a keyed reorder's transient detach (the panel-root-host discipline every other
    /// must-fire timer follows). Also pins ownership of a pending swap play (an orchestrated
    /// staggerChildren/delayChildren claim) against a presence exit starting on the SAME element: the
    /// swap is scheduled as an ordinary pending enter, so the existing enter/exit self-cancel
    /// discipline — <c>CancelEnter</c>, already called before <c>PlayExit</c> in
    /// <c>GeneralPathReconciler</c> — is the whole ownership mechanism, and an exit starting while a
    /// stagger-parked swap is still scheduled must cancel it and play on its own schedule instead of
    /// inheriting the stale delay.
    /// </summary>
    [TestFixture]
    internal sealed class MotionRuntimeSwapTests
    {
        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private readonly record struct LabelState(string Label);

        private sealed class LabelStore : Store<LabelState>
        {
            public LabelStore() : base(new LabelState("hidden")) { }
            public void Set(string label) => SetState(_ => new LabelState(label));
            protected override void ResetCore() => SetState(_ => new LabelState("hidden"));
        }

        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore(string initial) : base(new SetState(initial)) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState("x"));
        }

        private static LabelStore s_labelStore;
        private static SetStore s_keyStore;

        private EditorPanelSimulator _sim;
        private Reconciler _reconciler;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            // The spring case reads resolvedStyle across the swap, so the utility classes the
            // variants name must actually resolve (opacity-0 -> 0); the inline-slot and class-list
            // assertions elsewhere don't care, and no element here declares transition utilities.
            VelvetStyleUtilities.AttachTo(_sim.rootVisualElement);
            _reconciler = new Reconciler();
            s_labelStore = null;
            s_keyStore = null;
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler?.Dispose();
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private void Tick() => _sim.FrameUpdateMs(16);

        private void AdvancePast(float seconds)
        {
            var steps = (int)((seconds + 0.2f) * 1000f / 16f) + 1;
            for (var i = 0; i < steps; i++) Tick();
        }

        private static bool InlineDurationIsSet(VisualElement element)
        {
            var duration = element.style.transitionDuration;
            return duration.keyword != StyleKeyword.Null && duration.value != null && duration.value.Count > 0;
        }

        [Component]
        private static VNode LabeledBox()
        {
            var label = Hooks.UseStore(s_labelStore, s => s.Label);
            return V.Div(name: "wrap", children: new VNode[]
            {
                V.Motion(key: "m", name: "m", variants: s_fade, animate: label,
                    transition: new StyleTransitionConfig { DurationSec = 0.35f }),
            });
        }

        [Component]
        private static VNode SpringBox()
        {
            var label = Hooks.UseStore(s_labelStore, s => s.Label);
            return V.Div(name: "wrap", children: new VNode[]
            {
                V.Motion(key: "m", name: "m", variants: s_fade, animate: label,
                    transition: new StyleTransitionConfig
                    {
                        Type = TransitionType.Spring,
                        Stiffness = 170f,
                        Damping = 26f,
                    }),
            });
        }

        [Component]
        private static VNode StaggeredRow()
        {
            var label = Hooks.UseStore(s_labelStore, s => s.Label);
            return V.Motion(key: "p", name: "p", animate: label,
                transition: new StyleTransitionConfig { DurationSec = 0.2f, StaggerChildrenSec = 0.5f },
                children: new VNode[]
                {
                    V.Motion(key: "c0", name: "c0", variants: s_fade,
                        transition: new StyleTransitionConfig { DurationSec = 0.3f }),
                    V.Motion(key: "c1", name: "c1", variants: s_fade,
                        transition: new StyleTransitionConfig { DurationSec = 0.3f }),
                });
        }

        [Test]
        public void Given_AMountedMotionWithAConfigDuration_When_ItsAnimateLabelFlips_Then_TheSwapCarriesTheConfigsInlineTransition()
        {
            // Arrange — no transition utilities anywhere: the config alone must drive the tween.
            using var labels = new LabelStore();
            s_labelStore = labels;
            using var mounted = V.Mount(Root, V.Component(LabeledBox, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();

            // Act — flip the label at runtime.
            labels.Set("visible");
            scheduler.DrainImmediateForTest();

            // Assert — the swap rides an inline transition written from the config (Framer applies
            // `transition` to every animate update); a bare class diff would leave the slot null and
            // the flip would snap instantly.
            Assert.That(InlineDurationIsSet(Root.Q<VisualElement>("m")), Is.True);
        }

        [Test]
        public void Given_ARuntimeFlipInFlight_When_TheLabelFlipsBack_Then_TheElementSettlesAtTheOriginalVariantWithInlineSlotsReleased()
        {
            // Arrange — start hidden -> visible, then reverse before it finishes.
            using var labels = new LabelStore();
            s_labelStore = labels;
            using var mounted = V.Mount(Root, V.Component(LabeledBox, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            labels.Set("visible");
            scheduler.DrainImmediateForTest();
            Tick();

            // Act — interrupt with the reverse flip, then let everything play out.
            labels.Set("hidden");
            scheduler.DrainImmediateForTest();
            AdvancePast(1.0f);

            // Assert — the element rests at the latest variant (hidden), carries no leftover class
            // from the interrupted target pose, and the swap's inline transition is released so later
            // unrelated class changes don't inherit it.
            var m = Root.Q<VisualElement>("m");
            Assert.That(
                (m.ClassListContains("opacity-0"), m.ClassListContains("opacity-100"), InlineDurationIsSet(m)),
                Is.EqualTo((true, false, false)));
        }

        [Test]
        public void Given_AnOrchestratedChildSwap_When_TheCoordinatorFlips_Then_TheSwapTweensOnTheConfigAndFiresOnItsStaggerSlot()
        {
            // Arrange — two inheriting children under a staggering coordinator; no transition
            // utilities, so the children's own configs must drive their tweens.
            using var labels = new LabelStore();
            s_labelStore = labels;
            using var mounted = V.Mount(Root, V.Component(StaggeredRow, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();

            // Act — flip the coordinator's label; c0 claims slot 0, c1 claims the 0.5s slot. Sample
            // between the two slots (128ms) and after both (past 0.5s).
            labels.Set("visible");
            scheduler.DrainImmediateForTest();
            var c1 = Root.Q<VisualElement>("c1");
            var durationSetAtStart = InlineDurationIsSet(c1);
            for (var i = 0; i < 8; i++) Tick();
            var c0SwappedEarly = Root.Q<VisualElement>("c0").ClassListContains("opacity-100");
            var c1SwappedEarly = c1.ClassListContains("opacity-100");
            AdvancePast(0.5f);
            var c1SwappedLate = c1.ClassListContains("opacity-100");

            // Assert — the child's swap rides its config's inline transition, and the stagger delays
            // the SWAP itself (the target classes land only when the slot elapses) instead of
            // pre-swapping the classes and parking a CSS delay for utilities that may not exist.
            Assert.That((durationSetAtStart, c0SwappedEarly, c1SwappedEarly, c1SwappedLate),
                Is.EqualTo((true, true, false, true)));
        }

        [Test]
        public void Given_ASpringConfiguredMotion_When_ItsAnimateLabelFlipsAtRuntime_Then_TheSwapPassesThroughIntermediateOpacity()
        {
            // Arrange — a spring config on a mounted Motion; Framer springs every animate update.
            using var labels = new LabelStore();
            s_labelStore = labels;
            using var mounted = V.Mount(Root, V.Component(SpringBox, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            var m = Root.Q<VisualElement>("m");
            Assume.That(m.resolvedStyle.opacity, Is.LessThan(0.05f),
                "Precondition: the motion rests at the hidden variant");

            // Act — flip the label and tick the panel while the spring integrates.
            labels.Set("visible");
            scheduler.DrainImmediateForTest();
            var sawIntermediate = false;
            for (var i = 0; i < 60 && !sawIntermediate; i++)
            {
                Tick();
                var opacity = m.resolvedStyle.opacity;
                if (opacity > 0.05f && opacity < 0.95f)
                {
                    sawIntermediate = true;
                }
            }

            // Assert — the runtime swap is spring-driven (an instant class diff never shows an
            // intermediate value).
            Assert.That(sawIntermediate, Is.True);
        }

        // Plain (reconciler-driven) orchestration tree for the reorder case, mirroring the keyed
        // 3-way rotation that transiently detaches the child with the largest parked stagger slot.
        private static VNode[] StaggerTree(string label, params string[] order)
        {
            var children = new VNode[order.Length];
            for (var i = 0; i < order.Length; i++)
            {
                children[i] = V.Motion(key: order[i], name: order[i], variants: s_fade,
                    transition: new StyleTransitionConfig { DurationSec = 0.15f });
            }
            return new VNode[]
            {
                V.Motion(key: "p", name: "p", animate: label,
                    transition: new StyleTransitionConfig { DurationSec = 0.1f, StaggerChildrenSec = 0.3f },
                    children: children),
            };
        }

        [Test]
        public void Given_AnOrchestratedSwapParkedBehindItsStagger_When_AKeyedReorderTransientlyDetachesTheChild_Then_TheSwapStillReachesTheTargetVariant()
        {
            // Arrange — flip the label so c2 (index 2) parks the largest stagger slot (600ms); its
            // swap has not fired yet when the rotation below transiently detaches it.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), StaggerTree("hidden", "c0", "c1", "c2"));
            _reconciler.Reconcile(Root, StaggerTree("hidden", "c0", "c1", "c2"), StaggerTree("visible", "c0", "c1", "c2"));
            var c2Detached = false;
            Root.Q<VisualElement>("c2").RegisterCallback<DetachFromPanelEvent>(_ => c2Detached = true);

            // Act — a keyed rotation inside the parked window, then time advances past the whole
            // stagger + duration span.
            _reconciler.Reconcile(Root, StaggerTree("visible", "c0", "c1", "c2"), StaggerTree("visible", "c2", "c0", "c1"));
            Assume.That(c2Detached, Is.True, "Precondition: the rotation transiently detached the delayed child");
            AdvancePast(0.6f + 0.15f + 0.5f);

            // Assert — the deferred swap still fired: a swap timer parked on the detached element
            // itself (rather than the panel-root host) would be silently dropped, freezing the child
            // at its stale variant forever.
            Assert.That(Root.Q<VisualElement>("c2").ClassListContains("opacity-100"), Is.True);
        }

        // A coordinator whose label flip orchestrates ONE inheriting presence child: the child both
        // claims a stagger delay (it has no own animate) and is a keyed AnimatePresence child whose
        // removal takes the classic preset exit.
        [Component]
        private static VNode OrchestratedPresence()
        {
            var label = Hooks.UseStore(s_labelStore, s => s.Label);
            var keys = Hooks.UseStore(s_keyStore, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_fade,
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }));
            }
            return V.Motion(key: "coord", name: "coord", animate: label,
                transition: new StyleTransitionConfig { DurationSec = 0.2f, DelayChildrenSec = 0.5f },
                children: new VNode[]
                {
                    V.AnimatePresence(key: "presence", initial: false, children: children.ToArray()),
                });
        }

        [Test]
        public void Given_AParkedOrchestrationSwap_When_TheChildStartsAPresenceExit_Then_TheExitCompletesOnItsOwnScheduleWithoutTheStaleClaim()
        {
            // Arrange — flip the coordinator's label so the inheriting child's runtime-swap play is claimed
            // behind a 500ms stagger slot (its own deferred swap has not fired yet).
            using var labels = new LabelStore();
            s_labelStore = labels;
            using var keys = new SetStore("x");
            s_keyStore = keys;
            using var mounted = V.Mount(Root, V.Component(OrchestratedPresence, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            labels.Set("visible");
            scheduler.DrainImmediateForTest();
            var swapClaimedBeforeExit = InlineDurationIsSet(Root.Q<VisualElement>("item-x"));

            // Act — remove the key inside the parked window; the exit (its own 0.3s duration, no delay of
            // its own) must cancel the parked swap and start immediately.
            keys.Set("");
            scheduler.DrainImmediateForTest();
            AdvancePast(0.3f);
            scheduler.DrainImmediateForTest();

            // Assert — the ghost is already gone well before the original 500ms claim would have elapsed:
            // the exit ran on its own schedule instead of being postponed by the cancelled swap. The claim is
            // asserted with it, because an exit that never had a claim to cancel finishes on time anyway.
            Assert.That((swapClaimedBeforeExit, Root.Q<VisualElement>("item-x") == null),
                Is.EqualTo((true, true)));
        }
    }
}
