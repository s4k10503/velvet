using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a label change into a zero-duration pose does to the reversal a presence child's exit cancel
    /// leaves running when its key comes back mid-exit: the reversal keeps the classes its cancel was told to
    /// keep, and keeps its transition. Also pins that a re-entry's classic enter that plays nothing leaves the
    /// pose swap the same render's inherited label change starts to run: it tweens, and lands the pose's classes
    /// and its translate.
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
            public void Set(string keys, string label) => SetState(_ => new PresenceState(keys, label));
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

        private static readonly StyleTransitionConfig s_tween = new() { DurationSec = 0.35f };

        private static IReadOnlyDictionary<string, MotionVariant> s_followerPoses;

        private static Dictionary<string, MotionVariant> FollowerPoses(string hidden, string visible) => new()
        {
            ["hidden"] = new MotionVariant(hidden, s_tween),
            ["visible"] = new MotionVariant(visible, s_tween),
        };

        // Mounts the follower at hidden, removes it, then adds it back in the render that moves the parent's label
        // to visible.
        private MountedTree MountFollowerAndReAddAtVisible(PresenceStore store,
            IReadOnlyDictionary<string, MotionVariant> poses)
        {
            s_store = store;
            s_followerPoses = poses;
            var mounted = V.Mount(Root, V.Component(FollowerHost, key: "root"));
            Tick();
            store.SetKeys(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Tick();
            store.Set("a", "visible");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            return mounted;
        }

        // A presence child following its parent's label, whose own transition plays nothing: its removal is held
        // by a descendant's timed exit, and its re-entry plays the classic enter on that transition.
        [Component]
        private static VNode FollowerHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            var children = new List<VNode>();
            foreach (var key in state.Keys)
            {
                children.Add(V.Motion(key: key.ToString(), name: "item", transition: StyleTransitionConfig.None,
                    variants: s_followerPoses,
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", variants: new Dictionary<string, MotionVariant>
                        {
                            ["rest"] = "scale-100",
                            ["gone"] = "scale-50",
                        }, animate: "rest", exit: "gone", transition: s_tween),
                    }));
            }
            return V.Motion(name: "parent", animate: state.Label, children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
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

        // GREEN_ON_BASE(characterization): the base's classic enter that plays nothing cancels no pending swap.
        // So a follower re-added in the render that changes its inherited label lands on the new pose.
        [Test]
        public void Given_AFollowerHeldByADescendantsExit_When_ItReturnsAsItsInheritedLabelChanges_Then_ItRestsAtTheNewPose()
        {
            // Arrange
            using var store = new PresenceStore("hidden");

            // Act
            using var mounted = MountFollowerAndReAddAtVisible(store, FollowerPoses("opacity-0", "opacity-50"));
            AdvancePast(0.35f);

            // Assert
            var classes = Root.Q<VisualElement>("item").GetClasses()
                .Where(c => c.StartsWith("opacity-") || c.StartsWith("anim-"));
            Assert.That(string.Join(" ", classes), Is.EqualTo("opacity-50"));
        }

        // GREEN_ON_BASE(characterization): the base's classic enter that plays nothing cancels no pending swap.
        // So the swap the follower's label change starts keeps the transition it tweens on.
        [Test]
        public void Given_AFollowerHeldByADescendantsExit_When_ItReturnsAsItsInheritedLabelChanges_Then_ItsPoseSwapTweens()
        {
            // Arrange
            using var store = new PresenceStore("hidden");

            // Act
            using var mounted = MountFollowerAndReAddAtVisible(store, FollowerPoses("opacity-0", "opacity-50"));

            // Assert
            Assert.That(InlineDurationIsSet(Root.Q<VisualElement>("item")), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base's classic enter that plays nothing cancels no pending swap.
        // So the swap runs and writes the translate its pose holds back for it.
        [Test]
        public void Given_AFollowerHeldByADescendantsExit_When_ItReturnsAsItsInheritedLabelChanges_Then_ItsPoseTranslateIsWritten()
        {
            // Arrange
            using var store = new PresenceStore("hidden");

            // Act
            using var mounted = MountFollowerAndReAddAtVisible(store,
                FollowerPoses("translate-x-[0px]", "translate-x-[40px]"));
            AdvancePast(0.35f);

            // Assert
            Assert.That(Root.Q<VisualElement>("item").style.translate.value.x.value, Is.EqualTo(40f));
        }
    }
}
