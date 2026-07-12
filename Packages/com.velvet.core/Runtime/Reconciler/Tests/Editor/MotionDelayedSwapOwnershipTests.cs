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
    /// Pins ownership of a pending runtime variant-swap play (an orchestrated staggerChildren/delayChildren
    /// claim — see <c>MotionRuntimeSwapTests</c>) against a presence exit starting on the SAME element. The
    /// swap is scheduled as an ordinary pending ENTER (<c>StyleAnimationScheduler</c>'s own bookkeeping), so
    /// the existing enter/exit self-cancel discipline — <c>CancelEnter</c>, already called before
    /// <c>PlayExit</c> in <c>GeneralPathReconciler</c> — is the whole ownership mechanism: an exit starting
    /// while a stagger-parked swap is still scheduled must cancel it and play on its OWN schedule, with no
    /// stale delay of any kind postponing it. Unlike the parked-inline-transition-delay mechanism this
    /// superseded, there is no separate inline style left to race here at all — the claimed delay is purely a
    /// scheduler-side timing offset on the deferred swap, never written to the element.
    /// </summary>
    [TestFixture]
    internal sealed class MotionDelayedSwapOwnershipTests
    {
        private static readonly Dictionary<string, string> s_fade = new()
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

        // Whether the element's inline transition-duration is currently set — the runtime-swap play's own
        // tell (see MotionRuntimeSwapTests), used here to confirm the claimed swap actually started.
        private static bool InlineDurationIsSet(VisualElement element)
        {
            var duration = element.style.transitionDuration;
            return duration.keyword != StyleKeyword.Null && duration.value != null && duration.value.Count > 0;
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
            Assume.That(InlineDurationIsSet(Root.Q<VisualElement>("item-x")), Is.True,
                "Precondition: the child's runtime-swap play started, claimed behind its 500ms stagger slot");

            // Act — remove the key inside the parked window; the exit (its own 0.3s duration, no delay of
            // its own) must cancel the parked swap and start immediately.
            keys.Set("");
            scheduler.DrainImmediateForTest();
            AdvancePast(0.3f);
            scheduler.DrainImmediateForTest();

            // Assert — the ghost is already gone well before the original 500ms claim would have elapsed:
            // the exit ran on its own schedule instead of being postponed by the cancelled swap.
            Assert.That(Root.Q<VisualElement>("item-x"), Is.Null);
        }
    }
}
