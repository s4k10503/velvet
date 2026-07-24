using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the resting-variant pose across presence interruptions of a variant Motion. A variant
    /// Motion's resting state is its <c>variants[animate]</c> classes, and two interruption windows
    /// used to strip it: an exit starting mid-enter cancelled the enter without restoring the resting
    /// classes the enter's strip had removed (visible whenever a classic, non-variant exit follows),
    /// and a re-entry landing after a completed exit's class swap — but before its drop render — found
    /// the still-attached element at <c>variants[exit]</c> with nothing downstream putting the resting
    /// pose back.
    /// </summary>
    [TestFixture]
    internal sealed class MotionVariantInterruptionPoseTests
    {
        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore() : base(new SetState("a")) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState("a"));
        }

        private static SetStore s_store;
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };
        // Color classes on purpose: MotionSpringClassParser recognizes no spring-animatable channel in
        // them, so a spring exit over this pair completes SYNCHRONOUSLY (the swap lands, no tick) —
        // the only EditMode-reachable way to park a presence key in its completed-exit window.
        private static readonly Dictionary<string, string> s_recolor = new()
        {
            ["hidden"] = "bg-red-500",
            ["visible"] = "bg-blue-500",
        };
        // A third label distinct from the exit's: with initial == exit the initial→animate replay would
        // wash the exit residue out coincidentally, hiding a missing restoration.
        private static readonly Dictionary<string, string> s_recolorWithStart = new()
        {
            ["hidden"] = "bg-red-500",
            ["start"] = "bg-green-500",
            ["visible"] = "bg-blue-500",
        };

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        [Component]
        private static VNode EnterInterruptHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                // initial + animate, NO exit label: the exit that interrupts the enter is the classic
                // preset path, which re-adds no variant classes of its own.
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_fade, initial: "hidden", animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        [Component]
        private static VNode CompletedExitHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_recolor, animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { Type = TransitionType.Spring }));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        [Component]
        private static VNode CompletedExitWithInitialHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_recolorWithStart, initial: "start", animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { Type = TransitionType.Spring }));
            }
            return V.Div(name: "host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        [Test]
        public void Given_AVariantEnterInFlight_When_AClassicExitInterruptsIt_Then_TheRestingVariantIsRestored()
        {
            // Arrange — mount plays the initial→animate enter; its strip leaves the element at
            // variants[initial] until the (scheduler-driven) swap, which EditMode never fires.
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(EnterInterruptHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<VisualElement>("item-a").ClassListContains("opacity-0"), Is.True,
                "Precondition: the enter is in flight at its initial pose");

            // Act — remove the key: the exit cancels the in-flight enter, and with no exit label the
            // CLASSIC exit follows, which has no variant from-classes to re-add.
            store.Set("");
            scheduler.DrainImmediateForTest();

            // Assert — the enter's cancel must restore the resting variants[animate] it had stripped,
            // so the ghost plays its exit from the resting pose instead of a bare, variant-less one.
            var item = _root.Q<VisualElement>("item-a");
            Assert.AreEqual((false, true),
                (item.ClassListContains("opacity-0"), item.ClassListContains("opacity-100")),
                "Cancelling an in-flight variant enter restores the resting variant");
        }

        [Test]
        public void Given_ACompletedVariantExit_When_TheKeyIsReAddedBeforeTheDropRender_Then_TheRestingVariantIsRestored()
        {
            // Arrange — remove the key with ONE flush (not a drain): the spring exit completes
            // synchronously (the swap lands the element at variants[exit]) and schedules the ghost-drop
            // re-render, which this single flush deliberately leaves pending.
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(CompletedExitHost, key: "host"));
            store.Set("");
            mounted.FlushStateForTest();
            Assume.That(_root.Q<VisualElement>("item-a")?.ClassListContains("bg-red-500"), Is.True,
                "Precondition: the completed exit parked the still-attached element at variants[exit]");

            // Act — re-add the key inside the completed-exit window; the re-entry reproduces the SAME
            // still-attached element.
            store.Set("a");
            mounted.FlushStateForTest();

            // Assert — the re-entry must put the resting pose back: no pending animation is left to
            // cancel, and the class diff cannot restore it (the resting set is still recorded as applied).
            var item = _root.Q<VisualElement>("item-a");
            Assert.AreEqual((false, true),
                (item.ClassListContains("bg-red-500"), item.ClassListContains("bg-blue-500")),
                "A re-entry after a completed exit restores the resting variant");
        }

        [Test]
        public void Given_ACompletedVariantExitWithInitial_When_TheKeyIsReAddedBeforeTheDropRender_Then_TheExitPoseIsFullyReplaced()
        {
            // Arrange — same completed-exit window as above, with an `initial` label DISTINCT from the
            // exit's: the re-entry replays initial→animate, and only an explicit restoration removes the
            // exit classes first (the replay's own strip touches the resting label, not the exit's).
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(CompletedExitWithInitialHost, key: "host"));
            store.Set("");
            mounted.FlushStateForTest();
            Assume.That(_root.Q<VisualElement>("item-a")?.ClassListContains("bg-red-500"), Is.True,
                "Precondition: the completed exit parked the still-attached element at variants[exit]");

            // Act
            store.Set("a");
            mounted.FlushStateForTest();

            // Assert — the exit residue is gone and the (synchronously settled) spring replay landed the
            // element at its resting variant.
            var item = _root.Q<VisualElement>("item-a");
            Assert.AreEqual((false, true),
                (item.ClassListContains("bg-red-500"), item.ClassListContains("bg-blue-500")),
                "The re-entry replaces the exit pose before replaying the enter");
        }
    }
}
