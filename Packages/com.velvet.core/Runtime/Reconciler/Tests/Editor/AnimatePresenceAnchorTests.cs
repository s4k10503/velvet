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
    /// Pins which Motions a keyed AnimatePresence child's removal plays an exit on, and what its first mount
    /// suppresses, for a keyed child that is the Motion itself or puts a Provider, a component, a memo, a
    /// Suspense, an element or another Motion around it: the Motions in the removed child's committed subtree
    /// exit and hold the child until they have, a coordinator's exit label and stagger reach the Motions that
    /// inherit its labels, an inner AnimatePresence's child is that presence's, and the presence's
    /// <c>initial: false</c> reaches the Motions its first render mounts.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresenceAnchorTests
    {
        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        // A coordinator whose exit pose carries only its children's stagger.
        private static readonly Dictionary<string, MotionVariant> s_coordinator = new()
        {
            ["visible"] = "",
            ["hidden"] = new MotionVariant("", new StyleTransitionConfig { StaggerChildrenSec = 0.1f }),
        };

        private static readonly ComponentContext<int> s_context = ComponentContext<int>.Create(0);

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
            public FlagStore() : base(new FlagState(true)) { }
            public void Set(bool on) => SetState(_ => new FlagState(on));
            protected override void ResetCore() => SetState(_ => new FlagState(true));
        }

        private static KeySetStore s_keyStore;
        private static FlagStore s_flagStore;
        private static string s_wrapper;
        private static int s_exitsCompleted;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_keyStore = null;
            s_flagStore = null;
            s_wrapper = null;
            s_exitsCompleted = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private int HostChildCount => Root.Q<VisualElement>("host").childCount;

        // Declares an enter and an exit of its own, so either one playing is readable off its element.
        private static VNode TimedMotion(string key) => V.Motion(name: "item", key: key, variants: s_fade,
            initial: "hidden", animate: "visible", exit: "hidden",
            transition: new StyleTransitionConfig { DurationSec = 0.3f });

        // Inherits its label from the coordinator above it, with no exit of its own.
        private static VNode InheritingMotion(int index) => V.Motion(name: "child" + index, variants: s_fade,
            transition: new StyleTransitionConfig { DurationSec = 0.05f });

        [Component]
        private static VNode MotionRender() => TimedMotion(null);

        // The timed Motion while s_flagStore is on, nothing once it is off.
        [Component]
        private static VNode ToggledMotionRender()
        {
            var on = Hooks.UseStore(s_flagStore, s => s.On);
            return on ? TimedMotion(null) : null;
        }

        [Component]
        private static VNode CoordinatorRender() => V.Motion(variants: s_coordinator, animate: "visible",
            exit: "hidden", children: new[] { InheritingMotion(0), InheritingMotion(1), InheritingMotion(2) });

        // The keyed child s_wrapper names: the same TimedMotion, or a wrapper around it. "nested" puts it inside
        // a Motion that plays no exit of its own.
        private static VNode KeyedChild(string key) => s_wrapper switch
        {
            "direct" => TimedMotion(key),
            "provider" => V.Provider(s_context, 1, new[] { TimedMotion(null) }, key: key),
            "component" => V.Component(MotionRender, key: key),
            "memo" => V.MemoizedWithKey(key, () => TimedMotion(null)),
            "suspense" => V.Suspense(V.Label(text: "loading"), new[] { TimedMotion(null) }, key: key),
            "element" => V.Div(key: key, children: new[] { TimedMotion(null) }),
            "nested" => V.Motion(key: key, variants: s_fade, animate: "visible",
                children: new[] { TimedMotion(null) }),
            "toggled" => V.Component(ToggledMotionRender, key: key),
            "coordinator" => V.Component(CoordinatorRender, key: key),
            "inner-presence" => V.Div(key: key, children: new VNode[]
            {
                V.AnimatePresence(key: "inner", children: new[] { TimedMotion("inner-item") }),
            }),
            _ => throw new System.ArgumentOutOfRangeException(nameof(s_wrapper), s_wrapper, null),
        };

        [Component]
        private static VNode PresenceHost()
        {
            var keys = Hooks.UseStore(s_keyStore, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(KeyedChild(key.ToString()));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false, children: children.ToArray(),
                    onExitComplete: () => s_exitsCompleted++),
            });
        }

        // The inline transition-duration the scheduler wrote on the timed Motion's element, in milliseconds, or
        // NaN when nothing holds the slot or the element is gone — the same reading MotionVariantTransitionTests
        // takes.
        private float ItemDurationMs()
        {
            var element = Root.Q<VisualElement>("item");
            if (element == null) return float.NaN;
            var duration = element.style.transitionDuration;
            return duration.keyword != StyleKeyword.Null && duration.value != null && duration.value.Count > 0
                ? duration.value[0].value
                : float.NaN;
        }

        private MountedTree MountSettled(string wrapper, string keys)
        {
            s_wrapper = wrapper;
            s_keyStore = new KeySetStore(keys);
            var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));
            Frames(40);
            return mounted;
        }

        private void Frames(int count)
        {
            for (var i = 0; i < count; i++) _sim.FrameUpdateMs(16);
        }

        // GREEN_ON_BASE(characterization): an anchor's removal already waited for its exit.
        [TestCase("direct")]
        [TestCase("provider")]
        public void Given_AnAnchorDeclaringExit_When_TheKeyIsRemoved_Then_TheChildIsHeldForItsExit(string wrapper)
        {
            // Arrange — settled first, so the removal is from rest.
            using var mounted = MountSettled(wrapper, "a");
            using var keys = s_keyStore;

            // Act
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — an exiting ghost still holds the host's slot.
            Assert.That(HostChildCount, Is.EqualTo(1));
        }

        [TestCase("component")]
        [TestCase("memo")]
        [TestCase("suspense")]
        [TestCase("element")]
        [TestCase("nested")]
        public void Given_ATimedMotionBehindAWrapper_When_TheKeyIsRemoved_Then_TheChildIsHeldForItsExit(string wrapper)
        {
            // Arrange — settled first, so the removal is from rest.
            using var mounted = MountSettled(wrapper, "a");
            using var keys = s_keyStore;

            // Act
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the child is held, and the Motion's own 300ms exit is what is playing on its element.
            Assert.That((HostChildCount, ItemDurationMs()), Is.EqualTo((1, 300f)));
        }

        [Test]
        public void Given_AComponentChildExiting_When_ItsMotionsExitCompletes_Then_TheChildIsRemoved()
        {
            // Arrange
            using var mounted = MountSettled("component", "a");
            using var keys = s_keyStore;
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var heldWhileExiting = HostChildCount;

            // Act — well past the 300ms exit.
            Frames(40);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — held while the exit played, then gone, with onExitComplete fired once.
            Assert.That((heldWhileExiting, HostChildCount, s_exitsCompleted), Is.EqualTo((1, 0, 1)));
        }

        [Test]
        public void Given_AComponentChildExiting_When_TheKeyReturnsBeforeTheExitCompletes_Then_ItsMotionRestsVisible()
        {
            // Arrange
            using var mounted = MountSettled("component", "a");
            using var keys = s_keyStore;
            var before = Root.Q<VisualElement>("item");
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Frames(3);

            // Act — back mid-exit, then past where the exit would have ended.
            keys.Set("a");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Frames(40);

            // Assert — the same element, back at its resting pose with the exit pose gone.
            var item = Root.Q<VisualElement>("item");
            Assert.That(
                (ReferenceEquals(item, before), item.ClassListContains("opacity-100"), item.ClassListContains("opacity-0")),
                Is.EqualTo((true, true, false)));
        }

        [Test]
        public void Given_AComponentChildExiting_When_ItsMotionUnmountsBeforeTheExitCompletes_Then_TheChildIsRemoved()
        {
            // Arrange
            using var flag = new FlagStore();
            s_flagStore = flag;
            using var mounted = MountSettled("toggled", "a");
            using var keys = s_keyStore;
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var heldWhileExiting = HostChildCount;

            // Act — the only exiting Motion leaves the tree, well inside its 300ms.
            flag.Set(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Frames(2);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — held while it exited, then nothing was left to wait for.
            Assert.That((heldWhileExiting, s_exitsCompleted), Is.EqualTo((1, 1)));
        }

        [Test]
        public void Given_ACoordinatorStaggeringItsExit_When_TheKeyIsRemoved_Then_ItsInheritingChildrenExitInTurn()
        {
            // Arrange
            using var mounted = MountSettled("coordinator", "a");
            using var keys = s_keyStore;

            // Act — past the first child's turn, short of the second's 100ms.
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Frames(4);

            // Assert — the exit label has reached the first child only.
            var swapped = new List<bool>();
            for (var i = 0; i < 3; i++)
            {
                swapped.Add(Root.Q<VisualElement>("child" + i)?.ClassListContains("opacity-0") == true);
            }
            Assert.That(string.Join(",", swapped), Is.EqualTo("True,False,False"));
        }

        // GREEN_ON_BASE(characterization): an inner presence's child never held the outer removal.
        [Test]
        public void Given_AnInnerAnimatePresence_When_TheOuterKeyIsRemoved_Then_TheInnerChildDoesNotHoldIt()
        {
            // Arrange
            using var mounted = MountSettled("inner-presence", "a");
            using var keys = s_keyStore;

            // Act
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the outer child leaves at once.
            Assert.That(HostChildCount, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): initial: false already suppressed an anchor's mount enter.
        [TestCase("direct")]
        [TestCase("provider")]
        public void Given_InitialFalseOnThePresence_When_AnAnchorMounts_Then_ItsEnterIsSuppressed(string wrapper)
        {
            // Arrange
            s_wrapper = wrapper;
            using var keys = new KeySetStore("a");
            s_keyStore = keys;

            // Act — an enter that plays writes its inline timing inside this reconcile.
            using var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));

            // Assert — mounted, with no enter timing on it.
            Assert.That((Root.Q<VisualElement>("item") != null, float.IsNaN(ItemDurationMs())), Is.EqualTo((true, true)));
        }

        [TestCase("component")]
        [TestCase("memo")]
        [TestCase("suspense")]
        [TestCase("element")]
        public void Given_InitialFalseOnThePresence_When_ATimedMotionBehindAWrapperMounts_Then_ItsEnterIsSuppressed(
            string wrapper)
        {
            // Arrange
            s_wrapper = wrapper;
            using var keys = new KeySetStore("a");
            s_keyStore = keys;

            // Act — an enter that plays writes its inline timing inside this reconcile.
            using var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));

            // Assert — mounted, with no enter timing on it.
            Assert.That((Root.Q<VisualElement>("item") != null, float.IsNaN(ItemDurationMs())), Is.EqualTo((true, true)));
        }
    }
}
