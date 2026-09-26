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
    /// Pins what an AnimatePresence keeps per key for the exits a removal plays, and that it lets go of it:
    /// once the key's leaves are dropped, whether its exit completed or it was removed at once, and once a
    /// re-entry takes back an exit that was still playing or had already completed. Each case reads the
    /// entries while the key holds them and again after, in one comparison.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresenceDescendantExitStateTests
    {
        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        // No channel a spring can drive, so a spring exit over this pair completes inside the removing pass.
        private static readonly Dictionary<string, MotionVariant> s_recolor = new()
        {
            ["hidden"] = "bg-red-500",
            ["visible"] = "bg-blue-500",
        };

        private readonly record struct KeySetState(string Keys);

        private sealed class KeySetStore : Store<KeySetState>
        {
            public KeySetStore(string initial) : base(new KeySetState(initial)) { }
            public void Set(string keys) => SetState(_ => new KeySetState(keys));
            protected override void ResetCore() => SetState(_ => new KeySetState("a"));
        }

        private static KeySetStore s_keyStore;
        private static string s_kind;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_keyStore = null;
            s_kind = null;
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private static VNode TimedMotion() => V.Motion(name: "item", variants: s_fade, animate: "visible",
            exit: "hidden", transition: new StyleTransitionConfig { DurationSec = 0.3f });

        [Component]
        private static VNode TimedRow() => TimedMotion();

        [Component]
        private static VNode SpringRow() => V.Motion(name: "item", variants: s_recolor, animate: "visible",
            exit: "hidden", transition: new StyleTransitionConfig { Type = TransitionType.Spring });

        private static VNode KeyedChild(string key) => s_kind switch
        {
            // An anchor that plays no exit, around a Motion that does.
            "nested" => V.Motion(key: key, variants: s_fade, animate: "visible", children: new[] { TimedMotion() }),
            // An anchor with nothing to play, so its removal is immediate.
            "instant" => V.Motion(key: key, name: "item", transition: StyleTransitionConfig.None),
            "timed-row" => V.Component(TimedRow, key: key),
            "spring-row" => V.Component(SpringRow, key: key),
            _ => throw new System.ArgumentOutOfRangeException(nameof(s_kind), s_kind, null),
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
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        private MountedTree MountSettled(string kind, KeySetStore keys)
        {
            s_kind = kind;
            s_keyStore = keys;
            var mounted = V.Mount(_sim.rootVisualElement, V.Component(PresenceHost, key: "root"));
            Frames(40);
            return mounted;
        }

        private void Frames(int count)
        {
            for (var i = 0; i < count; i++) _sim.FrameUpdateMs(16);
        }

        // The per-key entries the presence and the reconciler hold for the exits: the anchor's Motion element,
        // the key's roots, its wait, and the descendants registered with a wait.
        private static string Entries(MountedTree mounted)
        {
            var ctx = mounted.Root.Reconciler.Context;
            var state = ctx.PresenceStates.Values.Single();
            return $"{state.MotionElements.Count},{state.ChildRoots.Count},{state.ExitWaits.Count},"
                + $"{ctx.PresenceDescendantExitWaits.Count}";
        }

        [Test]
        public void Given_AKeyWhoseDescendantExited_When_ItsLeavesAreDropped_Then_NothingIsKeptForIt()
        {
            // Arrange
            using var keys = new KeySetStore("a");
            using var mounted = MountSettled("nested", keys);
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var whileExiting = Entries(mounted);

            // Act — well past the 300ms exit, and the render that drops the leaves.
            Frames(40);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(whileExiting + "|" + Entries(mounted), Is.EqualTo("1,1,1,1|0,0,0,0"));
        }

        [Test]
        public void Given_AKeyWithNothingToPlay_When_ItIsRemoved_Then_NothingIsKeptForIt()
        {
            // Arrange
            using var keys = new KeySetStore("a");
            using var mounted = MountSettled("instant", keys);
            var whileMounted = Entries(mounted);

            // Act
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(whileMounted + "|" + Entries(mounted), Is.EqualTo("1,1,0,0|0,0,0,0"));
        }

        [Test]
        public void Given_ADescendantExitPlaying_When_TheKeyReturns_Then_TheWaitAndItsRegistrationAreGone()
        {
            // Arrange
            using var keys = new KeySetStore("a");
            using var mounted = MountSettled("timed-row", keys);
            keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var whileExiting = Entries(mounted);

            // Act
            keys.Set("a");
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the component child has no anchor, so it keeps no Motion element either way.
            Assert.That(whileExiting + "|" + Entries(mounted), Is.EqualTo("0,1,1,1|0,1,0,0"));
        }

        [Test]
        public void Given_ADescendantExitCompleted_When_TheKeyReturnsBeforeTheDropRender_Then_TheWaitAndItsRegistrationAreGone()
        {
            // Arrange — one flush, so the spring exit completes and the render that would drop the leaves waits.
            using var keys = new KeySetStore("a");
            using var mounted = MountSettled("spring-row", keys);
            keys.Set(string.Empty);
            mounted.FlushStateForTest();
            var whileParked = Entries(mounted);

            // Act
            keys.Set("a");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(whileParked + "|" + Entries(mounted), Is.EqualTo("0,1,1,1|0,1,0,0"));
        }
    }
}
