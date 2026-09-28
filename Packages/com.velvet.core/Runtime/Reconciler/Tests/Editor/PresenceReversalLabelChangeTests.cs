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
    /// keep, and keeps its transition, and lets go of a property the pose names. Also pins that a re-entry's
    /// classic enter that plays nothing leaves the pose swap the same render's inherited label change starts to
    /// run: it tweens, and lands the pose's classes and its translate; and that one that plays still lands the
    /// pose's translate, as an enter from an initial pose does.
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
        private static StyleTransitionConfig s_followerTransition;

        public override void SetUp()
        {
            base.SetUp();
            s_store = null;
            s_child = null;
            s_followerTransition = StyleTransitionConfig.None;
            s_initialTransition = null;
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

        // Mounts the follower at hidden, removes it, runs removedFrames frames, then adds it back in the render that
        // moves the parent's label to visible.
        private MountedTree MountFollowerAndReAddAtVisible(PresenceStore store,
            IReadOnlyDictionary<string, MotionVariant> poses, int removedFrames = 1,
            System.Action<MountedTree> beforeReAdd = null)
        {
            s_store = store;
            s_followerPoses = poses;
            var mounted = V.Mount(Root, V.Component(FollowerHost, key: "root"));
            Tick();
            store.SetKeys(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            for (var i = 0; i < removedFrames; i++)
            {
                Tick();
            }
            beforeReAdd?.Invoke(mounted);
            store.Set("a", "visible");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            return mounted;
        }

        // A presence child following its parent's label, on s_followerTransition: its removal is held by a
        // descendant's timed exit, and its re-entry plays the classic enter on that transition.
        [Component]
        private static VNode FollowerHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            var children = new List<VNode>();
            foreach (var key in state.Keys)
            {
                children.Add(V.Motion(key: key.ToString(), name: "item", transition: s_followerTransition,
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

        private static float TranslateX(VisualElement element) => element.style.translate.value.x.value;

        // The Fade exit's 0.2s runs out inside these frames and the descendant's 0.35s does not, so the key is still
        // exiting while the follower itself has stopped.
        private const int FramesPastTheFadeExit = 20;

        [Test]
        public void Given_AFollowerWhoseFadeExitCompletedWhileADescendantHoldsIt_When_ItReturnsAsItsInheritedLabelChanges_Then_ItsPoseTranslateIsWritten()
        {
            // Arrange
            s_followerTransition = StyleTransition.Fade;
            using var store = new PresenceStore("hidden");
            var followerStopped = false;

            // Act
            using var mounted = MountFollowerAndReAddAtVisible(store,
                FollowerPoses("translate-x-[0px]", "translate-x-[40px]"), FramesPastTheFadeExit,
                m =>
                {
                    var item = Root.Q<VisualElement>("item");
                    followerStopped = item != null
                        && !m.Root.Reconciler.Context.StyleAnimationScheduler.IsExiting(item);
                });
            AdvancePast(0.35f);

            // Assert — gated on the follower's own exit having completed: a re-add inside it starts no swap.
            var translateX = followerStopped ? TranslateX(Root.Q<VisualElement>("item")) : float.NaN;
            Assert.That(translateX, Is.EqualTo(40f));
        }

        [Test]
        public void Given_ASpringFollowerHeldByADescendantsExit_When_ItReturnsAsItsInheritedLabelChanges_Then_ItsPoseTranslateIsWritten()
        {
            // Arrange
            s_followerTransition = new StyleTransitionConfig { Type = TransitionType.Spring };
            using var store = new PresenceStore("hidden");

            // Act
            using var mounted = MountFollowerAndReAddAtVisible(store,
                FollowerPoses("translate-x-[0px]", "translate-x-[40px]"));
            AdvancePast(2f);

            // Assert
            Assert.That(TranslateX(Root.Q<VisualElement>("item")), Is.EqualTo(40f));
        }

        [Test]
        public void Given_ABezierFollowerHeldByADescendantsExit_When_ItReturnsAsItsInheritedLabelChanges_Then_ItsPoseTranslateIsWritten()
        {
            // Arrange
            s_followerTransition = new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 0.3f };
            using var store = new PresenceStore("hidden");

            // Act
            using var mounted = MountFollowerAndReAddAtVisible(store,
                FollowerPoses("translate-x-[0px]", "translate-x-[40px]"));
            AdvancePast(2f);

            // Assert
            Assert.That(TranslateX(Root.Q<VisualElement>("item")), Is.EqualTo(40f));
        }

        [Test]
        public void Given_ASpringFollowerHeldByADescendantsExit_When_ItReturnsAsItsInheritedLabelChanges_Then_ItsPoseSwapTweens()
        {
            // Arrange
            s_followerTransition = new StyleTransitionConfig { Type = TransitionType.Spring };
            using var store = new PresenceStore("hidden");

            // Act
            using var mounted = MountFollowerAndReAddAtVisible(store, FollowerPoses("opacity-0", "opacity-50"));

            // Assert
            Assert.That(InlineDurationIsSet(Root.Q<VisualElement>("item")), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base's classic enter that plays nothing cancels no pending swap.
        // So the translate the pose swap holds back is not written before that swap runs.
        [Test]
        public void Given_AFollowerHeldByADescendantsExit_When_ItReturnsAsItsInheritedLabelChanges_Then_ItsPoseTranslateWaitsForTheSwap()
        {
            // Arrange
            using var store = new PresenceStore("hidden");

            // Act
            using var mounted = MountFollowerAndReAddAtVisible(store,
                FollowerPoses("translate-x-[0px]", "translate-x-[40px]"));

            // Assert
            Assert.That(TranslateX(Root.Q<VisualElement>("item")), Is.EqualTo(0f));
        }

        [Test]
        public void Given_ASpringExitReversalRunning_When_TheLabelChangesToAZeroDurationPoseNamingOpacity_Then_TheReversalLetsGoOfOpacity()
        {
            // Arrange
            var variants = new Dictionary<string, MotionVariant>
            {
                ["lit"] = "opacity-100",
                ["dim"] = new MotionVariant("opacity-50", StyleTransitionConfig.None),
                ["gone"] = "opacity-0",
            };
            using var store = new PresenceStore("lit");
            using var mounted = MountRemoveAndReAdd(store, (state, key) => V.Motion(key: key, name: "item",
                variants: variants, animate: state.Label, exit: "gone",
                transition: new StyleTransitionConfig { Type = TransitionType.Spring }));
            var item = Root.Q<VisualElement>("item");
            var drivenBefore = item.style.opacity.keyword == StyleKeyword.Undefined;

            // Act
            store.SetLabel("dim");
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — with the reversal writing opacity before, so a release is what clears the slot.
            Assert.That((drivenBefore, item.style.opacity.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ASpringExitReversalRunning_When_TheLabelChangesToAZeroDurationPoseNamingTranslate_Then_TheTranslateStaysAfterTheReversalEnds()
        {
            // Arrange
            var variants = new Dictionary<string, MotionVariant>
            {
                ["lit"] = "translate-x-[0px]",
                ["dim"] = new MotionVariant("translate-x-[20px]", StyleTransitionConfig.None),
                ["gone"] = "translate-x-[-40px]",
            };
            using var store = new PresenceStore("lit");
            using var mounted = MountRemoveAndReAdd(store, (state, key) => V.Motion(key: key, name: "item",
                variants: variants, animate: state.Label, exit: "gone",
                transition: new StyleTransitionConfig { Type = TransitionType.Spring }));

            // Act
            store.SetLabel("dim");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(2f);

            // Assert
            Assert.That(TranslateX(Root.Q<VisualElement>("item")), Is.EqualTo(20f));
        }

        private static IReadOnlyDictionary<string, MotionVariant> s_initialPoses;
        private static StyleTransitionConfig s_initialTransition;

        // A presence child with its own label and an initial pose, whose removal a descendant's timed exit holds.
        [Component]
        private static VNode InitialPoseHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            var children = new List<VNode>();
            foreach (var key in state.Keys)
            {
                children.Add(V.Motion(key: key.ToString(), name: "item", initial: "start", animate: state.Label,
                    variants: s_initialPoses, transition: s_initialTransition,
                    children: new VNode[]
                    {
                        V.Motion(name: "inner", variants: new Dictionary<string, MotionVariant>
                        {
                            ["rest"] = "scale-100",
                            ["gone"] = "scale-50",
                        }, animate: "rest", exit: "gone", transition: s_tween),
                    }));
            }
            return V.AnimatePresence(key: "presence", children: children.ToArray());
        }

        [Test]
        public void Given_AChildWithAnInitialPoseWhoseExitCompleted_When_ItReturnsAtALabelWhoseTranslateThatPoseShares_Then_ItRestsAtThatTranslate()
        {
            // Arrange — the initial pose carries the new label's translate, so the enter holds none of its own.
            s_initialPoses = new Dictionary<string, MotionVariant>
            {
                ["start"] = new MotionVariant("opacity-0 translate-x-[40px]", s_tween),
                ["hidden"] = new MotionVariant("opacity-100 translate-x-[10px]", s_tween),
                ["visible"] = new MotionVariant("opacity-100 translate-x-[40px]", s_tween),
            };
            using var store = new PresenceStore("hidden");
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(InitialPoseHost, key: "root"));
            AdvancePast(1f);
            store.SetKeys(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var scheduler = mounted.Root.Reconciler.Context.StyleAnimationScheduler;
            var inner = Root.Q<VisualElement>("inner");
            for (var i = 0; i < 100 && scheduler.IsExiting(inner); i++)
            {
                Tick();
            }

            // Act — before the render the completed exit schedules, which would drop the child.
            store.Set("a", "visible");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(1f);

            // Assert
            Assert.That(TranslateX(Root.Q<VisualElement>("item")), Is.EqualTo(40f));
        }

        [Test]
        public void Given_AChildReAddedMidItsOwnExitAtANewLabel_When_ALaterSpringPoseLeavesTranslateUnnamed_Then_NoTranslateComesBack()
        {
            // Arrange — the child's own tween plays a classic exit, and it returns at another translate inside it.
            s_initialPoses = new Dictionary<string, MotionVariant>
            {
                ["start"] = new MotionVariant("translate-x-[0px]", s_tween),
                ["hidden"] = new MotionVariant("translate-x-[10px]", s_tween),
                ["visible"] = new MotionVariant("translate-x-[40px]", s_tween),
                ["dim"] = new MotionVariant("opacity-50", new StyleTransitionConfig { Type = TransitionType.Spring }),
            };
            s_initialTransition = s_tween;
            using var store = new PresenceStore("hidden");
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(InitialPoseHost, key: "root"));
            AdvancePast(1f);
            store.SetKeys(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Tick();
            var item = Root.Q<VisualElement>("item");
            var exiting = mounted.Root.Reconciler.Context.StyleAnimationScheduler.IsExiting(item);
            store.Set("a", "visible");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(1f);

            // Act
            store.SetLabel("dim");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(2f);

            // Assert — with the child exiting at the re-add, the shape the case is named for.
            Assert.That((exiting, item.style.translate.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ASpringExitReversalUnderALandedZeroDurationPose_When_ATimedPoseFollows_Then_OnlyThatPosesClassIsCarried()
        {
            // Arrange — the reversal still moves a translate the zero-duration pose leaves unnamed.
            var variants = new Dictionary<string, MotionVariant>
            {
                ["lit"] = "opacity-100 translate-x-[0px] tag-lit",
                ["dim"] = new MotionVariant("opacity-50 tag-dim", StyleTransitionConfig.None),
                ["next"] = "opacity-0 tag-next",
                ["gone"] = "opacity-0 translate-x-[-40px]",
            };
            using var store = new PresenceStore("lit");
            using var mounted = MountRemoveAndReAdd(store, (state, key) => V.Motion(key: key, name: "item",
                variants: variants, animate: state.Label, exit: "gone",
                transition: new StyleTransitionConfig { Type = TransitionType.Spring }));
            store.SetLabel("dim");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Tick();

            // Act
            store.SetLabel("next");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            AdvancePast(2f);

            // Assert
            var tags = Root.Q<VisualElement>("item").GetClasses().Where(c => c.StartsWith("tag-")).OrderBy(c => c);
            Assert.That(string.Join(" ", tags), Is.EqualTo("tag-next"));
        }
    }
}
