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
    /// Pins ownership of the single inline <c>transition-delay</c> slot that orchestrated
    /// (staggered) variant swaps park on an element. An enter/exit play starting on the same
    /// element must take the slot over — cancelling the parked swap — or a DelaySec=0 exit
    /// silently inherits the stale orchestration delay while its completion timeout is sized
    /// without it, and the ghost is dropped before any visible exit plays. And the parked clear
    /// must survive a transient keyed-reorder detach (the panel-root-host discipline every other
    /// must-fire timer in the scheduler follows), or the stale delay postpones every later
    /// transition on that element indefinitely.
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

        private static float InlineDelayMs(VisualElement element)
        {
            var delay = element.style.transitionDelay;
            return delay.keyword == StyleKeyword.Null || delay.value == null || delay.value.Count == 0
                ? 0f
                : delay.value[0].value;
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
        public void Given_AParkedOrchestrationDelay_When_TheChildStartsAPresenceExit_Then_TheStaleDelayDoesNotApply()
        {
            // Arrange — flip the coordinator's label so the inheriting child parks a 500ms delay.
            using var labels = new LabelStore();
            s_labelStore = labels;
            using var keys = new SetStore("x");
            s_keyStore = keys;
            using var mounted = V.Mount(Root, V.Component(OrchestratedPresence, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            labels.Set("visible");
            scheduler.DrainImmediateForTest();
            var item = Root.Q<VisualElement>("item-x");
            Assume.That(InlineDelayMs(item), Is.EqualTo(500f).Within(1e-3f),
                "Precondition: the orchestrated delay is parked on the child");

            // Act — remove the key inside the parked window; the exit (DelaySec 0) starts.
            keys.Set("");
            scheduler.DrainImmediateForTest();

            // Assert — the exit owns the delay slot now: the stale 500ms must not postpone it
            // (otherwise the completion timeout, sized without it, drops the ghost before any
            // visible exit plays).
            Assert.That(InlineDelayMs(Root.Q<VisualElement>("item-x")), Is.EqualTo(0f).Within(1e-3f));
        }

        // Plain (non-presence) orchestration for the reorder case: two inheriting keyed children.
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
        public void Given_AParkedDelayClear_When_TheChildIsReorderedDuringTheWindow_Then_TheDelayIsStillClearedEventually()
        {
            // Arrange — flip the label so c1 parks a 300ms delay with its clear scheduled out past
            // delay + duration.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), StaggerTree("hidden", "c0", "c1"));
            _reconciler.Reconcile(Root, StaggerTree("hidden", "c0", "c1"), StaggerTree("visible", "c0", "c1"));
            var c1 = Root.Q<VisualElement>("c1");
            Assume.That(InlineDelayMs(c1), Is.GreaterThan(0f), "Precondition: the stagger delay is parked");

            // Act — a keyed reorder transiently detaches/re-inserts the children inside the parked
            // window, then time advances well past the whole window.
            _reconciler.Reconcile(Root, StaggerTree("visible", "c0", "c1"), StaggerTree("visible", "c1", "c0"));
            AdvancePast(1.5f);

            // Assert — the inline delay is released; a dropped element-scheduled clear would leave
            // every later transition on this element starting 300ms late.
            Assert.That(InlineDelayMs(Root.Q<VisualElement>("c1")), Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void Given_AParkedDelayClear_When_TheReorderTransientlyDetachesTheDelayedChild_Then_TheDelayIsStillClearedEventually()
        {
            // Arrange — three inheriting children so the rotation below is FORCED to move the one that
            // actually parked a delay. The sibling test above swaps only two children, and the reconciler's
            // own LIS-based placement happens to keep ITS delayed child (c1) anchored in place — nothing
            // there ever transiently detaches it (see that test's own comment). c2 (index 2) claims the
            // largest stagger delay (600ms) and is the one non-anchor element this 3-way rotation moves via
            // RemoveFromHierarchy + re-Insert.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), StaggerTree("hidden", "c0", "c1", "c2"));
            _reconciler.Reconcile(Root, StaggerTree("hidden", "c0", "c1", "c2"), StaggerTree("visible", "c0", "c1", "c2"));
            Assume.That(InlineDelayMs(Root.Q<VisualElement>("c2")), Is.GreaterThan(0f),
                "Precondition: c2's stagger delay is parked");

            var c2Detached = false;
            Root.Q<VisualElement>("c2").RegisterCallback<DetachFromPanelEvent>(_ => c2Detached = true);

            // Act — a keyed rotation inside the parked window.
            _reconciler.Reconcile(Root, StaggerTree("visible", "c0", "c1", "c2"), StaggerTree("visible", "c2", "c0", "c1"));
            Assume.That(c2Detached, Is.True, "Precondition: the rotation transiently detached the delayed child");
            AdvancePast(0.6f + 0.15f);

            // Assert — the delay is still released; a clear timer parked on the detached element itself
            // (rather than the panel-root host) would have been silently dropped by the transient detach,
            // leaving this element's transition-delay stuck 600ms late on every later transition.
            Assert.That(InlineDelayMs(Root.Q<VisualElement>("c2")), Is.EqualTo(0f).Within(1e-3f));
        }
    }
}
