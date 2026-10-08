using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <c>propagate</c> on an inner AnimatePresence against Framer's: while the enclosing presence's keyed child
    /// is leaving, the inner presence exits every child of its own through its own exit path, the enclosing child
    /// stays mounted until the inner presence's exits have completed, and the inner presence's onExitComplete runs
    /// once when its own exits do. The inner presence sits in several shapes, a component under an element, a
    /// component at the top of the keyed child, the presence written inline under one or two elements, and inside a
    /// Portal, because each reaches the leaving child's emission differently.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresencePropagateTests
    {
        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private readonly record struct KeySetState(string Keys);

        private sealed class KeySetStore : Store<KeySetState>
        {
            public KeySetStore(string initial) : base(new KeySetState(initial)) { }
            public void Set(string keys) => SetState(_ => new KeySetState(keys));
            protected override void ResetCore() => SetState(_ => new KeySetState("a"));
        }

        private readonly record struct FlagState(bool On);

        private sealed class FlagStore : Store<FlagState>
        {
            public FlagStore(bool initial) : base(new FlagState(initial)) { }
            public void Set(bool on) => SetState(_ => new FlagState(on));
            protected override void ResetCore() => SetState(_ => new FlagState(true));
        }

        private static KeySetStore s_outerKeys;
        private static KeySetStore s_innerKeys;
        private static FlagStore s_propagate;
        private static FlagStore s_show;
        private static FlagStore s_showOuter;
        private static KeySetStore s_midKeys;
        private static int s_mounts;
        private static int s_cleanups;
        private static string s_shape;
        private static string s_innerChild;
        private static bool s_middlePropagate;
        private static AnimatePresenceMode s_outerMode;
        private static VisualElement s_portalTarget;
        private static int s_outerCompleted;
        private static int s_innerCompleted;
        private static float s_innerDurationSec;
        private static readonly List<string> s_order = new();

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_outerKeys = null;
            s_innerKeys = null;
            s_propagate = null;
            s_show = null;
            s_showOuter = null;
            s_midKeys = null;
            s_mounts = 0;
            s_cleanups = 0;
            s_shape = null;
            s_innerChild = "motion";
            s_middlePropagate = false;
            s_outerMode = AnimatePresenceMode.Sync;
            s_portalTarget = null;
            s_outerCompleted = 0;
            s_innerCompleted = 0;
            s_innerDurationSec = 0.3f;
            s_order.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            s_outerKeys?.Dispose();
            s_innerKeys?.Dispose();
            s_propagate?.Dispose();
            s_show?.Dispose();
            s_showOuter?.Dispose();
            s_midKeys?.Dispose();
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private int HostChildCount => Root.Q<VisualElement>("host").childCount;

        private VisualElement InnerItem => Root.Q<VisualElement>("inner-x");

        // Declares an enter and an exit of its own, so either one playing is readable off its element.
        private static VNode TimedMotion(string name, string key) => V.Motion(name: name, key: key, variants: s_fade,
            initial: "hidden", animate: "visible", exit: "hidden",
            transition: new StyleTransitionConfig { DurationSec = s_innerDurationSec });

        // The inner presence's child for a key: the Motion itself, or a Motion one element below the keyed child.
        private static VNode InnerChild(string key) => s_innerChild switch
        {
            "motion" => TimedMotion("inner-" + key, key),
            "nested" => V.Div(key: key, children: new[] { TimedMotion("inner-" + key, null) }),
            // Plays no exit, so its removal is immediate.
            "plain" => V.Div(name: "inner-" + key, key: key),
            _ => throw new System.ArgumentOutOfRangeException(nameof(s_innerChild), s_innerChild, null),
        };

        private static VNode InnerPresence(string keys, bool propagate)
        {
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(InnerChild(key.ToString()));
            }
            return V.AnimatePresence(key: "inner", propagate: propagate, onExitComplete: () =>
                {
                    s_innerCompleted++;
                    s_order.Add("inner");
                },
                children: children.ToArray());
        }

        // The inner presence with its children and propagate flag read from stores in its own component.
        [Component]
        private static VNode StatefulInnerRender()
        {
            var keys = Hooks.UseStore(s_innerKeys, s => s.Keys);
            var propagate = Hooks.UseStore(s_propagate, s => s.On);
            return InnerPresence(keys, propagate);
        }

        // The stateful inner presence while s_show is on, and nothing once it is off.
        [Component]
        private static VNode RetirableInnerRender()
        {
            var shown = Hooks.UseStore(s_show, s => s.On);
            var keys = Hooks.UseStore(s_innerKeys, s => s.Keys);
            return shown ? InnerPresence(keys, true) : null;
        }

        // A presence between the outer one and the stateful inner one, with the inner one under an element.
        private static VNode MiddlePresence() => V.AnimatePresence(key: "middle", propagate: s_middlePropagate,
            children: new VNode[]
            {
                V.Div(key: "m", children: new[] { V.Component(StatefulInnerRender) }),
            });

        [Component]
        private static VNode MiddleTopRender() => MiddlePresence();

        // A presence between the outer one and the retirable inner one, whose child "x" holds the inner presence at
        // its top and is the one the case removes.
        [Component]
        private static VNode TripleRender()
        {
            var keys = Hooks.UseStore(s_midKeys, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Component(RetirableInnerRender, key: key.ToString()));
            }
            return V.AnimatePresence(key: "o1", children: children.ToArray());
        }

        // Counts its effect's mounts and cleanups, and renders a propagating presence whose children play no exit.
        [Component]
        private static VNode EffectfulRender()
        {
            Hooks.UseEffect(() =>
            {
                s_mounts++;
                return (System.Action)(() => s_cleanups++);
            }, System.Array.Empty<object>());
            return InnerPresence("x", true);
        }

        // The outer presence's child for a key, around the inner presence in the shape s_shape names.
        private static VNode OuterChild(string key, string innerKeys, bool propagate)
        {
            if (key == "b") return V.Div(name: "b-child", key: key);
            return s_shape switch
            {
                "wrapper" => V.Div(key: key, children: new[] { V.Component(StatefulInnerRender) }),
                "top" => V.Component(StatefulInnerRender, key: key),
                "inline" => V.Div(key: key, children: new[] { InnerPresence(innerKeys, propagate) }),
                "deep" => V.Div(key: key, children: new[]
                {
                    V.Div(children: new[] { InnerPresence(innerKeys, propagate) }),
                }),
                "portal" => V.Div(key: key, children: new[]
                {
                    V.Portal(s_portalTarget, new[] { V.Component(StatefulInnerRender) }),
                }),
                "retirable" => V.Div(key: key, children: new[] { V.Component(RetirableInnerRender) }),
                "triple" => V.Div(key: key, children: new[] { V.Component(TripleRender) }),
                "effectful" => V.Component(EffectfulRender, key: key),
                "retirable-top" => V.Component(RetirableInnerRender, key: key),
                "middle" => V.Div(key: key, children: new[] { MiddlePresence() }),
                "middle-top" => V.Component(MiddleTopRender, key: key),
                // Exits more slowly than the inner Motion, so the inner presence finishes first.
                "slow-outer" => V.Motion(name: "slow", key: key, variants: s_fade, animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 0.6f },
                    children: new[] { V.Component(StatefulInnerRender) }),
                _ => throw new System.ArgumentOutOfRangeException(nameof(s_shape), s_shape, null),
            };
        }

        [Component]
        private static VNode PropagateHost()
        {
            var outerKeys = Hooks.UseStore(s_outerKeys, s => s.Keys);
            var innerKeys = Hooks.UseStore(s_innerKeys, s => s.Keys);
            var propagate = Hooks.UseStore(s_propagate, s => s.On);
            var outerShown = Hooks.UseStore(s_showOuter, s => s.On);
            var children = new List<VNode>();
            foreach (var key in outerKeys)
            {
                children.Add(OuterChild(key.ToString(), innerKeys, propagate));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                outerShown
                    ? V.AnimatePresence(key: "outer", mode: s_outerMode, onExitComplete: () =>
                    {
                        s_outerCompleted++;
                        s_order.Add("outer");
                    }, children: children.ToArray())
                    : null,
            });
        }

        // The outer presence holding key "a" over the inner presence holding innerKeys, propagating, settled.
        private MountedTree MountSettled(string shape, string innerKeys = "x", bool innerShown = true)
        {
            s_shape = shape;
            s_outerKeys = new KeySetStore("a");
            s_innerKeys = new KeySetStore(innerKeys);
            s_propagate = new FlagStore(true);
            s_show = new FlagStore(innerShown);
            s_showOuter = new FlagStore(true);
            s_midKeys = new KeySetStore("x");
            if (shape == "portal")
            {
                s_portalTarget = new VisualElement { name = "target" };
                Root.Add(s_portalTarget);
            }
            var mounted = V.Mount(Root, V.Component(PropagateHost, key: "root"));
            Frames(40);
            return mounted;
        }

        private static void Drain(MountedTree mounted) => mounted.GetSchedulerForTest().DrainImmediateForTest();

        private void Frames(int count)
        {
            for (var i = 0; i < count; i++) _sim.FrameUpdateMs(16);
        }

        // The presences that hold a slot in an enclosing child's exit wait.
        private static int Registrations(MountedTree mounted)
            => mounted.Root.Reconciler.Context.PresenceStates.Values.Count(state => state.Registration != null);

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        [TestCase("deep")]
        public void Given_AnInnerPresenceThatPropagates_When_TheOuterKeyIsRemoved_Then_TheChildIsHeldUntilTheInnerExitCompletes(
            string shape)
        {
            // Arrange
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            var heldWhileExiting = HostChildCount;

            // Act — well past the inner Motion's 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((heldWhileExiting, HostChildCount), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_AnInnerPresenceInsideAPortal_When_TheOuterKeyIsRemoved_Then_TheChildIsHeldUntilTheInnerExitCompletes()
        {
            // Arrange — the inner presence mounted in the Portal's deferred drain, with no emission of the outer child
            // around it.
            using var mounted = MountSettled("portal");
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            var heldWhileExiting = (HostChildCount, InnerItem != null);

            // Act — well past the inner Motion's 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((heldWhileExiting, HostChildCount, InnerItem == null),
                Is.EqualTo(((1, true), 0, true)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnInnerPresenceThatPropagates_When_TheOuterKeyIsRemoved_Then_EachPresenceRunsItsOnExitCompleteOnce(
            string shape)
        {
            // Arrange
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);

            // Act — well past the inner Motion's 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((s_innerCompleted, s_outerCompleted), Is.EqualTo((1, 1)));
        }

        [Test]
        public void Given_AnOuterChildWithASlowerExitOfItsOwn_When_TheInnerExitFinishes_Then_TheInnerCallbackRunsWhileTheOuterChildIsHeld()
        {
            // Arrange — the inner Motion exits in 300ms, the outer one in 600ms.
            using var mounted = MountSettled("slow-outer");
            s_outerKeys.Set(string.Empty);
            Drain(mounted);

            // Act — past the inner exit and short of the outer one.
            Frames(25);
            Drain(mounted);

            // Assert
            Assert.That((HostChildCount, s_innerCompleted, s_outerCompleted), Is.EqualTo((1, 1, 0)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnInnerPresenceWithTwoChildren_When_TheOuterKeyIsRemoved_Then_BothExitAndTheInnerCallbackRunsOnce(
            string shape)
        {
            // Arrange
            using var mounted = MountSettled(shape, "xy");
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            var heldWhileExiting = HostChildCount;

            // Act — well past the 300ms exits.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((heldWhileExiting, HostChildCount, s_innerCompleted), Is.EqualTo((2, 0, 1)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnInnerChildWithAMotionBelowIt_When_TheOuterKeyIsRemoved_Then_TheChildIsHeldUntilThatMotionExits(
            string shape)
        {
            // Arrange — the inner presence's child is an element, and its Motion sits one level down.
            s_innerChild = "nested";
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            var heldWhileExiting = HostChildCount;

            // Act — well past the Motion's 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((heldWhileExiting, HostChildCount), Is.EqualTo((1, 0)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnInnerChildWhoseExitIsUnderWay_When_TheOuterKeyIsRemoved_Then_TheChildLeavesWhenThatExitEndsNotLater(
            string shape)
        {
            // Arrange — a 1s exit that began 20 frames (320ms) before the outer key leaves.
            s_innerDurationSec = 1f;
            using var mounted = MountSettled(shape);
            s_innerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(20);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            var heldWhileExiting = HostChildCount;

            // Act — 68 frames (1088ms) in all: 88ms past the first exit's end, 232ms short of a replay from rest.
            Frames(48);
            Drain(mounted);

            // Assert
            Assert.That((heldWhileExiting, HostChildCount), Is.EqualTo((1, 0)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnInnerExitUnderWayOnItsOwn_When_TheOuterKeyIsRemoved_Then_TheInnerCallbackRunsOnce(
            string shape)
        {
            // Arrange — y is exiting on its own, x is live and exits with the outer removal.
            using var mounted = MountSettled(shape, "xy");
            s_innerKeys.Set("x");
            Drain(mounted);
            Frames(5);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);

            // Act — well past both 300ms exits.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((HostChildCount, s_innerCompleted), Is.EqualTo((0, 1)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnOuterKeyAndAnInnerKeyRemovedInOneDrain_When_TheExitRuns_Then_ItPlaysOnceAndBothCallbacksRunOnce(
            string shape)
        {
            // Arrange
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            s_innerKeys.Set(string.Empty);

            // Act — one 300ms exit fits in 25 frames of 16ms.
            Drain(mounted);
            Frames(25);
            Drain(mounted);

            // Assert
            Assert.That((HostChildCount, s_innerCompleted, s_outerCompleted), Is.EqualTo((0, 1, 1)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnOuterChildLeavingWithAnInnerPresence_When_TheKeyReturnsMidExit_Then_TheInnerChildRestsVisibleAndNothingCompletes(
            string shape)
        {
            // Arrange
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(5);

            // Act — back before the exit ends, then well past where it would have.
            s_outerKeys.Set("a");
            Drain(mounted);
            Frames(40);
            Drain(mounted);

            // Assert
            var item = InnerItem;
            Assert.That(
                (HostChildCount, item?.ClassListContains("opacity-100"), item?.ClassListContains("opacity-0"),
                    s_innerCompleted, s_outerCompleted),
                Is.EqualTo((1, true, false, 0, 0)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnInnerChildTheInnerPresenceRemovedItself_When_TheOuterKeyIsRemovedAndReturns_Then_ItKeepsExitingAndIsDropped(
            string shape)
        {
            // Arrange — the inner child is on its way out when the outer key leaves, then the outer key comes back.
            using var mounted = MountSettled(shape);
            s_innerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(2);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(2);
            s_outerKeys.Set("a");
            Drain(mounted);

            // Act — well past the 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((InnerItem == null, HostChildCount), Is.EqualTo((true, 0)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnInnerKeyAddedAgainWhileTheOuterChildLeaves_When_TheExitRuns_Then_ItStaysExitingAndTheOuterChildLeaves(
            string shape)
        {
            // Arrange — the inner key left, the outer key left, and now the inner key is added back.
            using var mounted = MountSettled(shape);
            s_innerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(2);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(2);
            s_innerKeys.Set("x");
            Drain(mounted);

            // Act — well past the 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((InnerItem == null, HostChildCount), Is.EqualTo((true, 0)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        public void Given_AnInnerKeyNeverSeenBefore_When_ItIsAddedWhileTheOuterChildLeaves_Then_ItMountsAtItsInitialPose(
            string shape)
        {
            // Arrange
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(2);

            // Act
            s_innerKeys.Set("xz");
            Drain(mounted);

            // Assert — mounted with its initial pose, not the pose its animate label rests at.
            var added = Root.Q<VisualElement>("inner-z");
            Assert.That((added?.ClassListContains("opacity-0"), added?.ClassListContains("opacity-100")),
                Is.EqualTo((true, false)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        public void Given_AnInnerKeyAddedWhileTheOuterChildLeaves_When_ItsExitOutlastsTheOthers_Then_TheOuterChildWaitsForItAndEachCallbackRunsOnce(
            string shape)
        {
            // Arrange — 2s exits: x began its exit at the outer removal, z mounts leaving 160ms later.
            s_innerDurationSec = 2f;
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(10);
            s_innerKeys.Set("xz");
            Drain(mounted);

            // Act — 2080ms after the outer removal: x's exit is over and z's is not.
            Frames(120);
            Drain(mounted);
            var whileZExits = (HostChildCount, s_innerCompleted, s_outerCompleted);
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((whileZExits, (HostChildCount, s_innerCompleted, s_outerCompleted)),
                Is.EqualTo(((1, 0, 0), (0, 1, 1))));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnInnerChildThatLeftAndCameBackBeforeTheOuterKeyLeft_When_TheOuterKeyIsRemoved_Then_TheChildIsHeldUntilItsExitCompletes(
            string shape)
        {
            // Arrange — the inner key's own exit was cancelled by its return, so it is live when the outer key leaves.
            using var mounted = MountSettled(shape);
            s_innerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(3);
            s_innerKeys.Set("x");
            Drain(mounted);
            Frames(40);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            var heldWhileExiting = HostChildCount;

            // Act — well past the 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((heldWhileExiting, HostChildCount), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_AnInnerPresenceRetiredWhileTheOuterChildWaitsOnIt_When_TheExitCanNoLongerComplete_Then_TheOuterChildLeaves()
        {
            // Arrange — the outer removal waits on the inner child's exit, which has most of its 300ms left.
            using var mounted = MountSettled("retirable");
            s_innerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(5);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);

            // Act — the component rendering the inner presence stops rendering it.
            s_show.Set(false);
            Drain(mounted);
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That(HostChildCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_AnInnerPresenceRetiredBeforeTheOuterKeyIsRemoved_When_TheOuterKeyIsRemoved_Then_TheOuterChildLeaves()
        {
            // Arrange — the inner presence is gone before the outer key leaves.
            using var mounted = MountSettled("retirable");
            s_show.Set(false);
            Drain(mounted);
            Frames(40);

            // Act
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That(HostChildCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_AnInnerPresenceWaitedOn_When_ItStopsPropagatingMidExit_Then_TheOuterChildLeaves()
        {
            // Arrange — the outer removal waits on the inner child's exit.
            using var mounted = MountSettled("wrapper");
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(3);

            // Act — the inner presence re-renders alone with propagate off, well short of its exit's end.
            s_propagate.Set(false);
            Drain(mounted);

            // Assert
            Assert.That(HostChildCount, Is.EqualTo(0));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        public void Given_AnInnerPresenceThatStoppedPropagating_When_TheOuterKeyIsRemoved_Then_TheOuterChildLeavesAtOnce(
            string shape)
        {
            // Arrange
            using var mounted = MountSettled(shape);
            s_propagate.Set(false);
            Drain(mounted);

            // Act
            s_outerKeys.Set(string.Empty);
            Drain(mounted);

            // Assert
            Assert.That(HostChildCount, Is.EqualTo(0));
        }

        [TestCase("middle")]
        [TestCase("middle-top")]
        public void Given_APresenceBetweenThatDoesNotPropagate_When_TheOuterKeyIsRemoved_Then_TheOuterChildLeavesAtOnce(
            string shape)
        {
            // Arrange — the inner presence propagates, but the nearest presence above it does not.
            using var mounted = MountSettled(shape);

            // Act
            s_outerKeys.Set(string.Empty);
            Drain(mounted);

            // Assert
            Assert.That(HostChildCount, Is.EqualTo(0));
        }

        [TestCase("middle")]
        [TestCase("middle-top")]
        public void Given_APresenceBetweenThatPropagates_When_TheOuterKeyIsRemoved_Then_TheChildIsHeldUntilTheInnerExitCompletes(
            string shape)
        {
            // Arrange
            s_middlePropagate = true;
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            var heldWhileExiting = HostChildCount;

            // Act — well past the inner Motion's 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((heldWhileExiting, HostChildCount, s_innerCompleted), Is.EqualTo((1, 0, 1)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnOuterInWaitMode_When_TheKeyIsSwappedForAnotherWhileAnInnerPresenceExits_Then_TheNewChildEntersOnceItCompletes(
            string shape)
        {
            // Arrange
            s_outerMode = AnimatePresenceMode.Wait;
            using var mounted = MountSettled(shape);
            s_outerKeys.Set("b");
            Drain(mounted);
            var newChildWhileExiting = Root.Q<VisualElement>("b-child");

            // Act — well past the inner Motion's 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((newChildWhileExiting == null, Root.Q<VisualElement>("b-child") != null, InnerItem == null),
                Is.EqualTo((true, true, true)));
        }

        [TestCase("retirable")]
        [TestCase("retirable-top")]
        public void Given_AnInnerPresenceFirstMountedByARenderOfItsOwn_When_TheOuterKeyIsRemoved_Then_TheChildIsHeldUntilTheInnerExitCompletes(
            string shape)
        {
            // Arrange — the outer child is already present when the inner presence first mounts.
            using var mounted = MountSettled(shape, "x", innerShown: false);
            s_show.Set(true);
            Drain(mounted);
            Frames(40);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            var heldWhileExiting = HostChildCount;

            // Act — well past the inner Motion's 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((heldWhileExiting, HostChildCount), Is.EqualTo((1, 0)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        public void Given_AnInnerPresenceWhoseChildrenPlayNoExit_When_TheOuterKeyIsRemoved_Then_TheOuterChildLeavesAtOnceAndBothCallbacksRun(
            string shape)
        {
            // Arrange
            s_innerChild = "plain";
            using var mounted = MountSettled(shape);

            // Act
            s_outerKeys.Set(string.Empty);
            Drain(mounted);

            // Assert
            Assert.That((HostChildCount, s_outerCompleted, s_innerCompleted), Is.EqualTo((0, 1, 1)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnInnerExitThatWasTheLastThingTheOuterChildWaitedFor_When_ItCompletes_Then_TheOuterCallbackRunsBeforeTheInnerOne(
            string shape)
        {
            // Arrange
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);

            // Act — well past the inner Motion's 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That(string.Join(",", s_order), Is.EqualTo("outer,inner"));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_TheOuterPresenceUnmountedMidExit_When_TheInnerExitWasWaitedOn_Then_NothingIsLeftMountedOrRegistered(
            string shape)
        {
            // Arrange
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(3);

            // Act
            s_showOuter.Set(false);
            Drain(mounted);
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((HostChildCount, Registrations(mounted)), Is.EqualTo((0, 0)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        public void Given_PropagateTurnedOffThenOnMidExit_When_TheInnerPresenceIsWaitedOn_Then_TheOuterChildLeavesOnce(
            string shape)
        {
            // Arrange
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(3);
            s_propagate.Set(false);
            Drain(mounted);

            // Act
            s_propagate.Set(true);
            Drain(mounted);
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((HostChildCount, s_outerCompleted), Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_AnInnerPresenceFirstMountedInAnInnerChildOfANestedPresence_When_ThatChildIsRemoved_Then_ItIsHeldUntilTheInnerExitCompletes()
        {
            // Arrange — the nearest enclosing child is the middle presence's "x", not the outer presence's "a".
            using var mounted = MountSettled("triple", "x", innerShown: false);
            s_show.Set(true);
            Drain(mounted);
            Frames(40);
            s_midKeys.Set(string.Empty);
            Drain(mounted);
            var heldWhileExiting = InnerItem != null;

            // Act — well past the inner Motion's 300ms exit.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((heldWhileExiting, InnerItem == null), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AChildWhoseOnlyOutputIsAPresenceOfChildrenWithNoExit_When_TheKeyIsRemovedAndAddedAgain_Then_ItsEffectCleansUpOnceAndMountsAgain()
        {
            // Arrange
            s_innerChild = "plain";
            using var mounted = MountSettled("effectful");
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            var cleanupsAfterRemoval = s_cleanups;

            // Act
            s_outerKeys.Set("a");
            Drain(mounted);

            // Assert — the cleanup ran once at the removal, and the returning child mounted a fresh fiber.
            Assert.That((cleanupsAfterRemoval, s_cleanups, s_mounts), Is.EqualTo((1, 1, 2)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnOuterChildWaitedOnAnInnerPresence_When_TheChildIsDropped_Then_NoRegistrationIsKept(string shape)
        {
            // Arrange
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            var whileExiting = Registrations(mounted);

            // Act — well past the inner Motion's 300ms exit, and the render that drops the leaves.
            Frames(40);
            Drain(mounted);

            // Assert
            Assert.That((whileExiting, Registrations(mounted)), Is.EqualTo((1, 0)));
        }

        [TestCase("wrapper")]
        [TestCase("top")]
        [TestCase("inline")]
        public void Given_AnOuterChildWaitedOnAnInnerPresence_When_TheKeyReturnsMidExit_Then_NoRegistrationIsKept(string shape)
        {
            // Arrange
            using var mounted = MountSettled(shape);
            s_outerKeys.Set(string.Empty);
            Drain(mounted);
            Frames(3);

            // Act
            s_outerKeys.Set("a");
            Drain(mounted);

            // Assert
            Assert.That(Registrations(mounted), Is.EqualTo(0));
        }
    }
}
