using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a label change into a zero-duration pose does to the reversal a presence child's exit cancel
    /// leaves running when its key comes back mid-exit: the reversal keeps the classes its cancel was told to
    /// keep, and keeps its transition.
    /// </summary>
    [TestFixture]
    internal sealed class PresenceReversalLabelChangeTests : MotionSimulatedPanelTestsBase
    {
        private readonly record struct PresenceState(string Keys, string Label);

        private sealed class PresenceStore : Store<PresenceState>
        {
            public PresenceStore(string label) : base(new PresenceState("a", label)) { }
            public void SetKeys(string keys) => SetState(s => s with { Keys = keys });
            public void SetLabel(string label) => SetState(s => s with { Label = label });
            protected override void ResetCore() => SetState(_ => new PresenceState("a", "lit"));
        }

        private static PresenceStore s_store;
        private static System.Func<PresenceState, string, VNode> s_child;

        public override void SetUp()
        {
            base.SetUp();
            s_store = null;
            s_child = null;
        }

        [Component]
        private static VNode PresenceHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            var children = new List<VNode>();
            foreach (var key in state.Keys)
            {
                children.Add(s_child(state, key.ToString()));
            }
            return V.AnimatePresence(key: "presence", children: children.ToArray());
        }

        // Mounts key "a", removes it, runs frames into its exit, then adds it back, which cancels the exit into
        // a reversal.
        private MountedTree MountRemoveAndReAdd(PresenceStore store, System.Func<PresenceState, string, VNode> child)
        {
            s_store = store;
            s_child = child;
            var mounted = V.Mount(Root, V.Component(PresenceHost, key: "root"));
            Tick();
            store.SetKeys(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Tick();
            Tick();
            Tick();
            store.SetKeys("a");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            return mounted;
        }

        private static bool InlineDurationIsSet(VisualElement element)
        {
            var duration = element.style.transitionDuration;
            return duration.keyword != StyleKeyword.Null && duration.value != null && duration.value.Count > 0;
        }

        // GREEN_ON_BASE(characterization): the base's zero-duration swap leaves a pending reversal alone.
        // So the class the Motion's own className shares with its resting pose stays through the swap.
        [Test]
        public void Given_ASpringExitReversalRunning_When_TheLabelChangesToAZeroDurationPose_Then_TheMotionsOwnClassSharedWithThePoseStays()
        {
            // Arrange — the Motion's own className repeats a class of its resting pose.
            var variants = new Dictionary<string, MotionVariant>
            {
                ["lit"] = "opacity-100 tagged",
                ["dim"] = new MotionVariant("opacity-50", StyleTransitionConfig.None),
                ["gone"] = "opacity-0",
            };
            using var store = new PresenceStore("lit");
            using var mounted = MountRemoveAndReAdd(store, (state, key) => V.Motion(key: key, name: "item",
                className: "tagged", variants: variants, animate: state.Label, exit: "gone",
                transition: new StyleTransitionConfig { Type = TransitionType.Spring }));

            // Act
            store.SetLabel("dim");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(2f);

            // Assert
            Assert.That(Root.Q<VisualElement>("item").ClassListContains("tagged"), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base's zero-duration swap leaves a pending reversal alone.
        // So a preset exit's reversal keeps the transition it fades back on.
        [Test]
        public void Given_APresetExitReversalRunning_When_TheLabelChangesToAZeroDurationPose_Then_TheReversalKeepsItsTransition()
        {
            // Arrange — no exit label, so the removal plays the preset exit, and the poses name no opacity.
            var variants = new Dictionary<string, MotionVariant>
            {
                ["lit"] = "tag-a",
                ["dim"] = new MotionVariant("tag-b", StyleTransitionConfig.None),
            };
            var presetExit = new StyleTransitionConfig
            {
                DurationSec = 0.3f,
                ExitFromClass = "exit-from",
                ExitToClass = "exit-to",
            };
            using var store = new PresenceStore("lit");
            using var mounted = MountRemoveAndReAdd(store, (state, key) => V.Motion(key: key, name: "item",
                variants: variants, animate: state.Label, transition: presetExit));

            // Act
            store.SetLabel("dim");
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(InlineDurationIsSet(Root.Q<VisualElement>("item")), Is.True);
        }
    }
}
