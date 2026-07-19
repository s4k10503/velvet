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
    /// Pins the bezier driver's lifecycle against a live (simulated) panel — the coverage the panel-free driver
    /// units cannot give, the bezier sibling of <see cref="MotionSpringLifecycleTests"/>. A bezier tween must:
    /// start once its element is attached (both production call sites play the enter BEFORE insertion); play a
    /// presence exit; leave a resolver-backed resting value (e.g. <c>translate-x-4</c>, inline-only, no USS rule)
    /// intact after completing; hand off to a fresh full-duration reversal on a cancel while ticking; scrub its
    /// inline pose when its subtree is torn down mid-exit (a pooled element must not keep receiving tick writes);
    /// complete an exit whose variants animate no bezier channel exactly once; release a cancel that lands before
    /// its delayed tick started; and never hand off to a reversal on a teardown cancel.
    /// </summary>
    [TestFixture]
    internal sealed class BezierTweenLifecycleTests
    {
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private static readonly Dictionary<string, string> s_slide = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100 translate-x-4",
        };

        // Colors are not a bezier channel: an exit between these labels resolves to an empty plan.
        private static readonly Dictionary<string, string> s_paint = new()
        {
            ["visible"] = "bg-white",
            ["hidden"] = "bg-black",
        };

        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore(string initial) : base(new SetState(initial)) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState("a"));
        }

        private readonly record struct ToggleState(bool Show);

        private sealed class ToggleStore : Store<ToggleState>
        {
            public ToggleStore() : base(new ToggleState(true)) { }
            public void Set(bool show) => SetState(_ => new ToggleState(show));
            protected override void ResetCore() => SetState(_ => new ToggleState(true));
        }

        private static SetStore s_store;
        private static ToggleStore s_outerStore;
        private static Dictionary<string, string> s_hostVariants;
        private static StyleTransitionConfig s_hostTransition;
        private static Action s_onExitComplete;

        private EditorPanelSimulator _sim;
        private Reconciler _reconciler;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            _reconciler = new Reconciler();
            s_store = null;
            s_outerStore = null;
            s_hostVariants = null;
            s_hostTransition = null;
            s_onExitComplete = null;
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

        // A bezier is a fixed-duration model (unlike a spring), so DurationSec must be positive. The curve is
        // left at Tailwind's own default (BezierX1..Y2 defaults).
        private static StyleTransitionConfig Bezier(float delaySec = 0f) => new()
        {
            Type = TransitionType.Bezier,
            DurationSec = 0.3f,
            DelaySec = delaySec,
        };

        [Component]
        private static VNode PresenceHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_hostVariants, animate: "visible", exit: "hidden",
                    transition: s_hostTransition));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false,
                    onExitComplete: s_onExitComplete, children: children.ToArray()),
            });
        }

        [Component]
        private static VNode OuterGate()
        {
            var show = Hooks.UseStore(s_outerStore, s => s.Show);
            return V.Div(name: "outer", children: show
                ? new VNode[] { V.Component(PresenceHost, key: "host") }
                : Array.Empty<VNode>());
        }

        [Test]
        public void Given_AStandaloneBezierEnter_When_ThePanelTicks_Then_TheElementSettlesAtItsAnimatePose()
        {
            // Arrange / Act — the enter plays inside CreateElement (detached); the panel then attaches the
            // element and ticks. The bezier must start once attached, not silently give up.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(key: "m", name: "m", variants: s_fade,
                    initial: "hidden", animate: "visible", transition: Bezier()),
            });
            var m = Root.Q<VisualElement>("m");
            Assume.That(m, Is.Not.Null, "Precondition: the motion mounted");
            AdvancePast(1f);

            // Assert — the tween ran to rest instead of freezing at the pinned from-pose.
            Assert.That(m.resolvedStyle.opacity, Is.EqualTo(1f).Within(1e-2f));
        }

        [Test]
        public void Given_ABezierConfigWithDuration_When_APresenceChildExits_Then_TheGhostStaysWhileTheTweenPlays()
        {
            // Arrange
            s_hostVariants = s_fade;
            s_hostTransition = Bezier();
            using var store = new SetStore("a");
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(PresenceHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();

            // Act — remove the key; a bezier exit must play, so the ghost stays mounted.
            store.Set("");
            scheduler.DrainImmediateForTest();

            // Assert — the child is still present as an exiting ghost, not instant-removed.
            Assert.AreEqual(1, Root.Q<VisualElement>("host").childCount);
        }

        [Test]
        public void Given_ABezierWhoseRestingVariantUsesAResolverValue_When_ItSettles_Then_TheValueIsKept()
        {
            // Arrange / Act — translate-x-4 exists only as a resolver-applied inline style (no USS rule), so the
            // completion path must leave the element at 16px, not clear it to identity.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(key: "m", name: "m", variants: s_slide,
                    initial: "hidden", animate: "visible", transition: Bezier()),
            });
            var m = Root.Q<VisualElement>("m");
            AdvancePast(1f);

            // Assert — the resolver-owned resting translate survives the tween's completion.
            Assert.That(m.resolvedStyle.translate.x, Is.EqualTo(16f).Within(0.5f));
        }

        [Test]
        public void Given_ABezierExitCancelledMidTick_When_TheReversalCompletes_Then_TheInlinePoseIsReleased()
        {
            // Arrange — drives StyleAnimationScheduler directly on a REAL attached element, so this exercises the
            // reversal hand-off (Retarget + move into the enter map) on a tick that is actually running.
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            Root.Add(element);
            element.AddToClassList("opacity-100");
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier,
                DurationSec = 0.3f,
                ExitFromClass = "opacity-100",
                ExitToClass = "opacity-0",
            };
            scheduler.PlayExit(element, config, onComplete: null, restoreFromOnCancel: true);
            Tick();
            Assume.That(element.style.opacity.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: the exit's tick is actively writing an inline opacity override");

            // Act — cancel mid-tick (an exit reversal), then let the reversal run its full duration.
            scheduler.CancelExit(element);
            AdvancePast(1f);

            // Assert — the reversal ran to completion and released the inline pose (no dead pending entry
            // pinning stale inline opacity).
            Assert.That(element.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AMidExitBezierSubtree_When_ItIsTornDown_Then_TheDetachedElementCarriesNoInlinePose()
        {
            // Arrange — a bezier exit is in flight when the whole presence subtree unmounts.
            s_hostVariants = s_fade;
            s_hostTransition = Bezier();
            using var store = new SetStore("a");
            s_store = store;
            using var outer = new ToggleStore();
            s_outerStore = outer;
            using var mounted = V.Mount(Root, V.Component(OuterGate, key: "outer"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            store.Set("");
            scheduler.DrainImmediateForTest();
            Tick();
            var item = Root.Q<VisualElement>("item-a");
            Assume.That(item, Is.Not.Null, "Precondition: the ghost is exiting");

            // Act — tear the subtree down mid-exit (the element heads back to the pool).
            outer.Set(false);
            scheduler.DrainImmediateForTest();

            // Assert — teardown scrubs the tween's inline pose immediately; no reversal may keep ticking styles
            // onto (and eventually null-clearing) a pooled element.
            Assert.That(item.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ABezierExitWithNoAnimatableChannel_When_TheKeyIsRemoved_Then_TheGhostDropsAndExitCompleteFiresOnce()
        {
            // Arrange — the exit variant only changes colors (not a bezier channel), so the plan is empty and the
            // exit must complete once, not replay against the per-pass exit bookkeeping forever.
            s_hostVariants = s_paint;
            s_hostTransition = new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 0.5f };
            var exitCompleteCalls = 0;
            s_onExitComplete = () => exitCompleteCalls++;
            using var store = new SetStore("a");
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(PresenceHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();

            // Act — remove the key and drain several times (the completion re-render included).
            store.Set("");
            for (var i = 0; i < 6; i++)
            {
                scheduler.DrainImmediateForTest();
            }

            // Assert — the ghost is gone and onExitComplete fired exactly once (no re-render loop).
            Assert.That((Root.Q<VisualElement>("host").childCount, exitCompleteCalls),
                Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_ABezierExitCancelledBeforeItsDelayedTickEverStarted_When_TimeAdvances_Then_NoDeadRetargetLingersForever()
        {
            // Arrange — a 0.4s delay means the recurring tick has not started (Bezier.Tick is still null) by the
            // time the cancel below lands, so the reversal branch must NOT try to hand off to a tick that nothing
            // would ever start.
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            Root.Add(element);
            element.AddToClassList("opacity-100");
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier,
                DurationSec = 0.3f,
                DelaySec = 0.4f,
                ExitFromClass = "opacity-100",
                ExitToClass = "opacity-0",
            };
            scheduler.PlayExit(element, config, onComplete: null, restoreFromOnCancel: true);
            Tick();
            Assume.That(element.style.opacity.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: the exit's ApplyCurrentValues wrote an inline opacity override");

            // Act — cancel while still parked behind the delay, then let plenty of time pass.
            scheduler.CancelExit(element);
            AdvancePast(1f);

            // Assert — the inline opacity override is released immediately by the cancel instead of waiting on a
            // tick that nothing would ever start.
            Assert.That(element.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AnActivelyTickingBezierExit_When_CancelledForTeardown_Then_ItNeverHandsOffToAReversal()
        {
            // Arrange — no delay, so the panel-root tick is already running when the teardown cancel lands.
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            Root.Add(element);
            element.AddToClassList("opacity-100");
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier,
                DurationSec = 0.3f,
                ExitFromClass = "opacity-100",
                ExitToClass = "opacity-0",
            };
            scheduler.PlayExit(element, config, onComplete: null, restoreFromOnCancel: true);
            Tick();
            Assume.That(element.style.opacity.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: the exit's tick is actively writing an inline opacity override");

            // Act — the element is being torn down for good, not merely interrupted.
            scheduler.CancelExitForTeardown(element);

            // Assert — the teardown cancel finalized immediately instead of parking a reversal whose recurring
            // tick would keep writing inline styles onto this (about to be pooled) element.
            Assert.That(element.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }
    }
}
