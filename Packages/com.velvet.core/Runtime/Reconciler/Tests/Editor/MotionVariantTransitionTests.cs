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
    /// Pins <see cref="MotionVariant.Transition"/>: a pose carrying its own
    /// <see cref="StyleTransitionConfig"/> drives the swap INTO that pose at each of the four call sites
    /// that play one — a standalone mount enter, a presence mount enter, a runtime <c>animate</c> label
    /// change, and an AnimatePresence exit — with the Motion's own <c>transition:</c> as the default a pose
    /// replaces. The first three read the pose whatever it applies; the exit reads it only where the pose
    /// applies a class, so a classless exit pose leaves the classic exit on the Motion's own config, and a
    /// case each way pins the split. The presence enter and the exit share one declaration, 0.5s in against
    /// 0.05s out over a Motion whose own transition declares 0.2s, which is the asymmetry a single per-node
    /// config cannot express.
    /// Three gates decide how a removal is treated, and each reads the same chain, so a Motion whose own
    /// transition is <c>StyleTransitionConfig.None</c> still exits on the timing its exit pose declares:
    /// the ghost gate, the reversed-stagger count and <c>mode: Wait</c> are pinned one case each. The
    /// orchestration frame a label change establishes for inheriting descendants is measured off the same
    /// resolved span.
    /// Durations are read back as the inline transition-duration the scheduler wrote — a declared value in
    /// milliseconds, never a measured one — and no case reads <c>resolvedStyle</c>, so the bundled
    /// stylesheet is deliberately not attached.
    /// </summary>
    [TestFixture]
    internal sealed class MotionVariantTransitionTests
    {
        private static readonly StyleTransitionConfig s_nodeDefault = new() { DurationSec = 0.2f };

        // 0.5s in against 0.05s out: each pose carries the timing for swaps INTO it, and both differ from
        // the node default above, so a reading names which of the three configs was consulted.
        private static readonly Dictionary<string, MotionVariant> s_asymmetric = new()
        {
            ["hidden"] = new MotionVariant("opacity-0", new StyleTransitionConfig { DurationSec = 0.05f }),
            ["visible"] = new MotionVariant("opacity-100", new StyleTransitionConfig { DurationSec = 0.5f }),
        };

        // Only the exit pose is timed; the node opts out of animation entirely. Each of the three removal
        // gates reads that pose: without it the ghost gate reaps the child before the exit play, and the
        // other two disagree with the play that does happen.
        private static readonly Dictionary<string, MotionVariant> s_exitTimedOnly = new()
        {
            ["hidden"] = new MotionVariant("opacity-0", new StyleTransitionConfig { DurationSec = 0.5f }),
            ["visible"] = "opacity-100",
        };

        // The coordinator's own resting pose carries a span an order of magnitude longer than the node's, so
        // an inheriting descendant's BeforeChildren wait names which of the two it was measured from.
        private static readonly Dictionary<string, MotionVariant> s_coordinatorPoses = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = new MotionVariant("opacity-100",
                new StyleTransitionConfig { DurationSec = 0.5f, When = TransitionWhen.BeforeChildren }),
        };

        private static readonly Dictionary<string, MotionVariant> s_plainFade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        // The destination pose applies no class of its own and still carries a timing — the "null or empty
        // applies nothing" spelling the XML documents. The source pose is classed, so a swap into this one
        // still changes which classes are applied and the play fires.
        // The child stagger lives on the coordinator's destination pose; its node transition declares
        // none, so a frame built from the node's config would start both children on the same slot.
        private static readonly Dictionary<string, MotionVariant> s_staggerOnThePose = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = new MotionVariant("opacity-100",
                new StyleTransitionConfig { DurationSec = 0.02f, StaggerChildrenSec = 0.4f }),
        };

        private static readonly Dictionary<string, MotionVariant> s_classlessTimedPose = new()
        {
            ["hidden"] = "opacity-0",
            ["shown"] = new MotionVariant(null, new StyleTransitionConfig { DurationSec = 0.5f }),
        };

        // The classless-but-timed shape of s_classlessTimedPose above, reached as an `exit` target instead
        // of an `animate` one, where the rule inverts: applying no class makes it no variant exit at all,
        // so this 0.5s is the number a reading must NOT find.
        private static readonly Dictionary<string, MotionVariant> s_classlessTimedExit = new()
        {
            ["visible"] = "opacity-100",
            ["gone"] = new MotionVariant(null, new StyleTransitionConfig { DurationSec = 0.5f }),
        };

        private readonly record struct LabelState(string Label);

        private sealed class LabelStore : Store<LabelState>
        {
            public LabelStore() : base(new LabelState("hidden")) { }
            public void Set(string label) => SetState(_ => new LabelState(label));
            protected override void ResetCore() => SetState(_ => new LabelState("hidden"));
        }

        private readonly record struct KeySetState(string Keys);

        private sealed class KeySetStore : Store<KeySetState>
        {
            public KeySetStore(string initial) : base(new KeySetState(initial)) { }
            public void Set(string keys) => SetState(_ => new KeySetState(keys));
            protected override void ResetCore() => SetState(_ => new KeySetState("a"));
        }

        private static LabelStore s_labelStore;
        private static KeySetStore s_keyStore;
        private static AnimatePresenceMode s_mode;
        private static float s_staggerSec;
        private static int s_staggerDirection;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_labelStore = null;
            s_keyStore = null;
            s_mode = AnimatePresenceMode.Sync;
            s_staggerSec = 0f;
            s_staggerDirection = 1;
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private void Tick() => _sim.FrameUpdateMs(16);

        private void Advance(float seconds)
        {
            var steps = (int)(seconds * 1000f / 16f) + 1;
            for (var i = 0; i < steps; i++) Tick();
        }

        // The inline transition-duration currently applied to this element, in milliseconds — the scheduler
        // authors its TimeValue entries in TimeUnit.Millisecond, and reading style rather than resolvedStyle
        // returns exactly what it wrote. float.NaN when no play holds the slot, so a case that samples at the
        // wrong moment fails instead of reporting a plausible number.
        private static float InlineDurationMs(VisualElement element)
        {
            var duration = element.style.transitionDuration;
            return duration.keyword != StyleKeyword.Null && duration.value != null && duration.value.Count > 0
                ? duration.value[0].value
                : float.NaN;
        }

        // Which pose of s_exitTimedOnly / s_plainFade this element currently carries, with "gone" for an
        // element that is no longer in the tree — a removal and a parked swap are different findings, and a
        // bool pair would report them the same way.
        private static string PoseOf(VisualElement element) =>
            element == null ? "gone" : element.ClassListContains("opacity-100") ? "visible" : "hidden";

        [Component]
        private static VNode AsymmetricPresence()
        {
            var keys = Hooks.UseStore(s_keyStore, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_asymmetric, initial: "hidden", animate: "visible", exit: "hidden",
                    transition: s_nodeDefault));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        [Component]
        private static VNode LabeledBox()
        {
            var label = Hooks.UseStore(s_labelStore, s => s.Label);
            return V.Div(name: "wrap", children: new VNode[]
            {
                V.Motion(key: "m", name: "m", variants: s_asymmetric, animate: label,
                    transition: s_nodeDefault),
            });
        }

        [Component]
        private static VNode ClasslessPoseBox()
        {
            var label = Hooks.UseStore(s_labelStore, s => s.Label);
            return V.Div(name: "wrap", children: new VNode[]
            {
                V.Motion(key: "m", name: "m", variants: s_classlessTimedPose, animate: label,
                    transition: s_nodeDefault),
            });
        }

        [Component]
        private static VNode ClasslessExitPresence()
        {
            var keys = Hooks.UseStore(s_keyStore, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_classlessTimedExit, animate: "visible", exit: "gone",
                    transition: s_nodeDefault));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        [Component]
        private static VNode ExitTimedPresence()
        {
            var keys = Hooks.UseStore(s_keyStore, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_exitTimedOnly, animate: "visible", exit: "hidden",
                    transition: StyleTransitionConfig.None));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false, staggerSec: s_staggerSec,
                    staggerDirection: s_staggerDirection, mode: s_mode, children: children.ToArray()),
            });
        }

        [Component]
        private static VNode StaggerCoordinator()
        {
            var label = Hooks.UseStore(s_labelStore, s => s.Label);
            return V.Motion(key: "p", name: "p", variants: s_staggerOnThePose, animate: label,
                transition: new StyleTransitionConfig { DurationSec = 0.02f },
                children: new VNode[]
                {
                    V.Motion(key: "c0", name: "c0", variants: s_plainFade,
                        transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                    V.Motion(key: "c1", name: "c1", variants: s_plainFade,
                        transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                });
        }

        [Component]
        private static VNode BeforeChildrenCoordinator()
        {
            var label = Hooks.UseStore(s_labelStore, s => s.Label);
            return V.Motion(key: "p", name: "p", variants: s_coordinatorPoses, animate: label,
                transition: new StyleTransitionConfig
                {
                    DurationSec = 0.02f,
                    When = TransitionWhen.BeforeChildren,
                },
                children: new VNode[]
                {
                    V.Motion(key: "c", name: "c", variants: s_plainFade,
                        transition: new StyleTransitionConfig { DurationSec = 0.05f }),
                });
        }

        [Test]
        public void Given_AnEnterWhoseTargetPoseCarriesATransition_When_TheMotionMounts_Then_TheEnterRidesThatPosesDuration()
        {
            // Arrange — variants[visible] declares 0.5s; the Motion's own transition declares 0.2s.
            using var keys = new KeySetStore("a");
            s_keyStore = keys;

            // Act — the presence mount enter plays inside the reconcile, writing its inline timing there.
            using var mounted = V.Mount(Root, V.Component(AsymmetricPresence, key: "root"));

            // Assert — the target pose's own 500ms, not the node's 200ms.
            Assert.That(InlineDurationMs(Root.Q<VisualElement>("item-a")), Is.EqualTo(500f).Within(1e-3f));
        }

        [Test]
        public void Given_AStandaloneMotionEnter_When_ItsTargetPoseCarriesATransition_Then_TheEnterRidesThatPosesDuration()
        {
            // Arrange — the same variants map, on a Motion outside any AnimatePresence: the mount enter is
            // dispatched from the element-creation path rather than the presence expansion, and each resolves
            // its own timing.
            using var reconciler = new Reconciler();

            // Act
            reconciler.Reconcile(Root, System.Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(key: "m", name: "m", variants: s_asymmetric, initial: "hidden", animate: "visible",
                    transition: s_nodeDefault),
            });

            // Assert — the target pose's own 500ms, not the node's 200ms.
            Assert.That(InlineDurationMs(Root.Q<VisualElement>("m")), Is.EqualTo(500f).Within(1e-3f));
        }

        [Test]
        public void Given_NeitherThePoseNorTheNodeCarryingATransition_When_AnInitialIsDeclared_Then_TheUnplayableEnterIsDiagnosed()
        {
            // Arrange — the enter resolves its classes and has nothing to play them on. `V.Motion`
            // cannot build this, always resolving a transition and falling back to the Fade preset, so
            // the node is constructed directly; an unexpected Warning fails no Unity test on its own,
            // which is why the count is captured and asserted rather than left to LogAssert.
            var diagnosed = 0;
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.Contains("initial is set but has no resolvable enter"))
                {
                    diagnosed++;
                }
            }
            Application.logMessageReceived += OnLog;

            // Act
            try
            {
                using var mounted = V.Mount(Root, new MotionNode
                {
                    Name = "m",
                    Variants = s_plainFade,
                    Initial = "hidden",
                    Animate = "visible",
                });
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            // Assert — diagnosed once, rather than mounting inert or playing on a config it has not got.
            Assert.That(diagnosed, Is.EqualTo(1));
        }

        [Test]
        public void Given_AnExitPoseCarryingATransition_When_TheKeyIsRemoved_Then_TheExitRidesThatPosesDuration()
        {
            // Arrange — the same declaration the enter case reads: variants[hidden] declares 0.05s, the
            // node 0.2s, and the pose the element is leaving 0.5s. Settled first, so this is a removal from
            // rest rather than an interrupted enter.
            using var keys = new KeySetStore("a");
            s_keyStore = keys;
            using var mounted = V.Mount(Root, V.Component(AsymmetricPresence, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Advance(0.6f);

            // Act — remove the key; the exit play writes its inline timing inside the reconcile.
            keys.Set(string.Empty);
            scheduler.DrainImmediateForTest();

            // Assert — the exit pose's own 50ms: neither the node's 200ms nor the resting pose's 500ms.
            Assert.That(InlineDurationMs(Root.Q<VisualElement>("item-a")), Is.EqualTo(50f).Within(1e-3f));
        }

        [Test]
        public void Given_AnExitPoseApplyingNoClassButCarryingATransition_When_TheKeyIsRemoved_Then_TheExitRidesTheNodesDuration()
        {
            // Arrange — variants[gone] applies no class and declares 0.5s against the node's 0.2s. The
            // Motion declares no `initial` label, so no enter play holds the duration slot and the reading
            // below is the removal's own write.
            using var keys = new KeySetStore("a");
            s_keyStore = keys;
            using var mounted = V.Mount(Root, V.Component(ClasslessExitPresence, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();

            // Act — remove the key; the exit play writes its inline timing inside the reconcile.
            keys.Set(string.Empty);
            scheduler.DrainImmediateForTest();

            // Assert — the node's 200ms, not the pose's 500ms: an exit pose applying no class is no variant
            // exit.
            Assert.That(InlineDurationMs(Root.Q<VisualElement>("item-a")), Is.EqualTo(200f).Within(1e-3f));
        }

        [Test]
        public void Given_APoseCarryingATransition_When_TheAnimateLabelFlipsToIt_Then_TheSwapRidesThatPosesDuration()
        {
            // Arrange — mounted resting at variants[hidden] (no `initial`, so nothing plays on mount).
            using var labels = new LabelStore();
            s_labelStore = labels;
            using var mounted = V.Mount(Root, V.Component(LabeledBox, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();

            // Act — flip the label; the runtime swap plays inside the patch.
            labels.Set("visible");
            scheduler.DrainImmediateForTest();

            // Assert — the DESTINATION pose's 500ms, not the source pose's 50ms and not the node's 200ms.
            Assert.That(InlineDurationMs(Root.Q<VisualElement>("m")), Is.EqualTo(500f).Within(1e-3f));
        }

        [Test]
        public void Given_APoseApplyingNoClassButCarryingATransition_When_TheAnimateLabelFlipsToIt_Then_TheSwapRidesThatPosesDuration()
        {
            // Arrange — mounted resting at variants[hidden]; variants[shown] applies no class and declares
            // 0.5s against the node's 0.2s, so the reading names which config the swap consulted.
            using var labels = new LabelStore();
            s_labelStore = labels;
            using var mounted = V.Mount(Root, V.Component(ClasslessPoseBox, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();

            // Act
            labels.Set("shown");
            scheduler.DrainImmediateForTest();

            // Assert — the destination pose's 500ms. A pose is found either way, so what it applies cannot
            // decide whose timing plays.
            Assert.That(InlineDurationMs(Root.Q<VisualElement>("m")), Is.EqualTo(500f).Within(1e-3f));
        }

        [Test]
        public void Given_APoseApplyingNoClassButCarryingATransition_When_TheMotionMounts_Then_TheEnterRidesThatPosesDuration()
        {
            // Arrange — the same pose reached by the mount enter rather than a label change.
            using var reconciler = new Reconciler();

            // Act
            reconciler.Reconcile(Root, System.Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(key: "m", name: "m", variants: s_classlessTimedPose, initial: "hidden",
                    animate: "shown", transition: s_nodeDefault),
            });

            // Assert — the destination pose's 500ms, the same answer the swap above has to give.
            Assert.That(InlineDurationMs(Root.Q<VisualElement>("m")), Is.EqualTo(500f).Within(1e-3f));
        }

        [Test]
        public void Given_AMotionTransitionOfNone_When_ItsExitPoseDeclaresADuration_Then_TheRemovalIsHeldAsAnExitingGhost()
        {
            // Arrange — the node opts out of animation (StyleTransitionConfig.None); only variants[hidden],
            // the exit pose, carries a duration.
            using var keys = new KeySetStore("a");
            s_keyStore = keys;
            using var mounted = V.Mount(Root, V.Component(ExitTimedPresence, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();

            // Act
            keys.Set(string.Empty);
            scheduler.DrainImmediateForTest();

            // Assert — still mounted as a ghost: a gate reading only the node's config sees DurationSec 0
            // and reaps the leaf in this very reconcile, so the exit pose's timing never plays.
            Assert.That(Root.Q<VisualElement>("host").childCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_AReversedExitStagger_When_OnlyTheExitPoseDeclaresADuration_Then_TheLastChildLeavesFirst()
        {
            // Arrange — two children, both leaving at once, under a reversed stagger. The reversal is
            // computed from how many of them animate their exit, which is decided by the exit pose here.
            using var keys = new KeySetStore("ab");
            s_keyStore = keys;
            s_staggerSec = 0.3f;
            s_staggerDirection = -1;
            using var mounted = V.Mount(Root, V.Component(ExitTimedPresence, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();

            // Act — remove both, then sample inside the first stagger slot.
            keys.Set(string.Empty);
            scheduler.DrainImmediateForTest();
            Advance(0.15f);

            // Assert — b's swap fired and a's is still parked behind the 0.3s slot. An uncounted exit
            // collapses the reversal to forward order, which is the same two readings the other way round.
            Assert.That(
                (PoseOf(Root.Q<VisualElement>("item-a")), PoseOf(Root.Q<VisualElement>("item-b"))),
                Is.EqualTo(("visible", "hidden")));
        }

        [Test]
        public void Given_WaitModeAndAnExitTimedByItsPoseAlone_When_ANewKeyArrives_Then_TheNewChildIsWithheld()
        {
            // Arrange — mode: Wait holds a brand-new child back while any previously committed child is
            // still exiting, and whether that exit animates is decided by the exit pose here.
            using var keys = new KeySetStore("a");
            s_keyStore = keys;
            s_mode = AnimatePresenceMode.Wait;
            using var mounted = V.Mount(Root, V.Component(ExitTimedPresence, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();

            // Act — swap the key set entirely: a leaves, b arrives.
            keys.Set("b");
            scheduler.DrainImmediateForTest();

            // Assert — a's ghost holds the slot and b has not mounted yet.
            Assert.That(
                (PoseOf(Root.Q<VisualElement>("item-a")), PoseOf(Root.Q<VisualElement>("item-b"))),
                Is.EqualTo(("visible", "gone")));
        }

        [Test]
        public void Given_BeforeChildrenOnACoordinatorsPose_When_ItsLabelFlips_Then_ChildrenWaitOutThatPosesSpan()
        {
            // Arrange — the coordinator's resting pose declares a 0.5s BeforeChildren span while the node's
            // own transition declares 0.02s. Both orchestrate; only the span differs.
            using var labels = new LabelStore();
            s_labelStore = labels;
            using var mounted = V.Mount(Root, V.Component(BeforeChildrenCoordinator, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();

            // Act — flip the coordinator's label, then sample inside the pose's span and past the node's.
            labels.Set("visible");
            scheduler.DrainImmediateForTest();
            Advance(0.2f);

            // Assert — the coordinator's own swap has landed and the inheriting child is still parked. The
            // parent term is what separates "still waiting" from "nothing was orchestrated at all".
            Assert.That(
                (PoseOf(Root.Q<VisualElement>("p")), PoseOf(Root.Q<VisualElement>("c"))),
                Is.EqualTo(("visible", "hidden")));
        }

        [Test]
        public void Given_StaggerChildrenOnACoordinatorsPose_When_ItsLabelFlips_Then_ItsChildrenTakeSeparateSlots()
        {
            // Arrange — the stagger is declared only by the destination pose. The case above pins the
            // BeforeChildren term of the same frame, which the node's own config also carries; this one
            // pins the stagger term, which it does not, so a frame read off the node cannot separate them.
            using var labels = new LabelStore();
            s_labelStore = labels;
            using var mounted = V.Mount(Root, V.Component(StaggerCoordinator, key: "root"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();

            // Act — flip, then sample past the first child's slot and inside the second's 0.4s one.
            labels.Set("visible");
            scheduler.DrainImmediateForTest();
            Advance(0.2f);

            // Assert — separate slots. Both children swapping together is the frame having been built
            // without the pose's stagger.
            Assert.That(
                (PoseOf(Root.Q<VisualElement>("c0")), PoseOf(Root.Q<VisualElement>("c1"))),
                Is.EqualTo(("visible", "hidden")));
        }

        [Test]
        public void Given_APresenceChildTimedOnlyByItsPose_When_ItMounts_Then_TheEnterRidesThatPosesDuration()
        {
            // Arrange — the node carries no transition at all, which `V.Motion` cannot build (it falls back
            // to the Fade preset), so the node is constructed directly. The only timing is the target pose's.
            using var reconciler = new Reconciler();

            // Act
            reconciler.Reconcile(Root, System.Array.Empty<VNode>(), new VNode[]
            {
                V.AnimatePresence(key: "presence", children: new VNode[]
                {
                    new MotionNode
                    {
                        Key = "item",
                        Name = "item",
                        Variants = s_asymmetric,
                        Initial = "hidden",
                        Animate = "visible",
                    },
                }),
            });

            // Assert — the pose's 500ms. A gate asking only whether the NODE is timed skips this enter
            // entirely, which drops OnEnterComplete with it.
            Assert.That(InlineDurationMs(Root.Q<VisualElement>("item")), Is.EqualTo(500f).Within(1e-3f));
        }
    }
}
