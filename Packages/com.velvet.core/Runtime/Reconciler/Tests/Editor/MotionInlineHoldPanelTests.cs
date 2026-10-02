using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The inline-hold cases that need a panel to run a swap or a spring: what a tween swap leaves behind once
    /// it has written its held pose, what an earlier swap's write does after a later render dropped the hold,
    /// what the reversal of a cancelled preset exit puts back when it is cancelled in turn, and what a spring
    /// exit started before the enter's swap reads for an axis no pose names.
    /// </summary>
    [TestFixture]
    internal sealed class MotionInlineHoldPanelTests : MotionSimulatedPanelTestsBase
    {
        private static readonly StyleTransitionConfig s_tween = new() { DurationSec = 0.3f };

        private readonly record struct PresenceState(string Keys, string Label);

        private sealed class PresenceStore : Store<PresenceState>
        {
            public PresenceStore() : base(new PresenceState("a", "full")) { }
            public void SetKeys(string keys) => SetState(s => s with { Keys = keys });
            public void SetLabel(string label) => SetState(s => s with { Label = label });
            protected override void ResetCore() => SetState(_ => new PresenceState("a", "full"));
        }

        private static PresenceStore s_store;

        public override void SetUp()
        {
            base.SetUp();
            s_store = null;
            s_child = null;
        }

        private static MotionNode Card(IReadOnlyDictionary<string, MotionVariant> variants, string label)
            => V.Motion(name: "card", variants: variants, animate: label, transition: s_tween);

        // Renders the card from one label to the next, the first mounting it.
        private VisualElement Render(IReadOnlyDictionary<string, MotionVariant> variants, string from, string to)
        {
            _reconciler.Reconcile(Root, new VNode[] { Card(variants, from) }, new VNode[] { Card(variants, to) });
            return Root.Q<VisualElement>("card");
        }

        private static float TranslateX(VisualElement element)
            => element.style.translate.keyword == StyleKeyword.Undefined
                ? element.style.translate.value.x.value
                : float.NaN;

        // GREEN_ON_BASE(characterization): the base writes every pose at the render, holding nothing back.
        // A swap that has written its held pose must leave no hold for a later render to diff from.
        [Test]
        public void Given_ATweenSwapThatHasWrittenItsPose_When_AZeroDurationSwapReturns_Then_TheFirstPoseIsWritten()
        {
            // Arrange
            var variants = new Dictionary<string, MotionVariant>
            {
                ["left"] = new MotionVariant("translate-x-[0px]", StyleTransitionConfig.None),
                ["right"] = new MotionVariant("translate-x-[40px]", s_tween),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[] { Card(variants, "left") });
            Render(variants, "left", "right");
            Tick();
            Tick();

            // Act
            var card = Render(variants, "right", "left");

            // Assert
            Assert.That(TranslateX(card), Is.EqualTo(0f));
        }

        // GREEN_ON_BASE(characterization): the base writes every pose at the render, holding nothing back.
        // An earlier tween's swap running after a later render dropped its hold must write nothing.
        [Test]
        public void Given_AHoldAZeroDurationSwapDropped_When_TheEarlierTweensSwapRuns_Then_TheLaterPoseStays()
        {
            // Arrange — the zero-duration swap starts no play, so the tween's is still pending.
            var variants = new Dictionary<string, MotionVariant>
            {
                ["left"] = "translate-x-[0px]",
                ["right"] = new MotionVariant("translate-x-[40px]", s_tween),
                ["far"] = new MotionVariant("translate-x-[80px]", StyleTransitionConfig.None),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[] { Card(variants, "left") });
            Render(variants, "left", "right");
            var card = Render(variants, "right", "far");

            // Act
            Tick();
            Tick();

            // Assert
            Assert.That(TranslateX(card), Is.EqualTo(80f));
        }

        private static Func<PresenceState, string, VNode> s_child;

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

        private MountedTree MountPresence(PresenceStore store, Func<PresenceState, string, VNode> child)
        {
            s_store = store;
            s_child = child;
            return V.Mount(Root, V.Component(PresenceHost, key: "root"));
        }

        private static readonly Dictionary<string, MotionVariant> s_levels = new()
        {
            ["full"] = "opacity-100",
            ["half"] = new MotionVariant("opacity-50", StyleTransitionConfig.None),
        };

        private static readonly StyleTransitionConfig s_presetExit = new()
        {
            DurationSec = 0.3f,
            ExitFromClass = "exit-from",
            ExitToClass = "exit-to",
        };

        // GREEN_ON_BASE(characterization): the base's cancel never hands a preset exit resting classes.
        // So the reversal it parks has none to put back when a later exit cancels it.
        [Test]
        public void Given_APresetExitCancelledAndItsLabelChangedSince_When_TheChildIsRemovedAgain_Then_TheOldLabelsClassIsNotPutBack()
        {
            // Arrange — removed, re-added mid-exit (which parks the exit's reversal), then moved to a label
            // whose zero-duration swap plays nothing that would cancel that reversal.
            using var store = new PresenceStore();
            using var mounted = MountPresence(store, (state, key) => V.Motion(key: key, name: "item",
                variants: s_levels, animate: state.Label, transition: s_presetExit));
            var scheduler = mounted.GetSchedulerForTest();
            store.SetKeys(string.Empty);
            scheduler.DrainImmediateForTest();
            store.SetKeys("a");
            scheduler.DrainImmediateForTest();
            store.SetLabel("half");
            scheduler.DrainImmediateForTest();

            // Act
            store.SetKeys(string.Empty);
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That(Root.Q<VisualElement>("item").ClassListContains("opacity-100"), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base's enter never writes initial's translate at all.
        // So an axis neither exit pose names stays at its resting 0 whatever the exit reads.
        [Test]
        public void Given_ASpringExitThatDeclaresADuration_When_TheChildIsRemovedBeforeItsEnterSwaps_Then_AnAxisNoPoseNamesStaysAtRest()
        {
            // Arrange — the enter starts from a y the resting and exit poses leave unnamed, and the spring
            // carries a duration, which describes it in place of its physics knobs.
            var variants = new Dictionary<string, MotionVariant>
            {
                ["away"] = "translate-x-[0px] translate-y-[40px]",
                ["rest"] = new MotionVariant("translate-x-[0px]", s_tween),
                ["gone"] = new MotionVariant("translate-x-[60px]",
                    new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.3f }),
            };
            using var store = new PresenceStore();
            using var mounted = MountPresence(store, (_, key) => V.Motion(key: key, name: "item", variants: variants,
                initial: "away", animate: "rest", exit: "gone", transition: s_tween));
            var item = Root.Q<VisualElement>("item");

            // Act — removed before the enter's swap, then a few frames of the spring.
            store.SetKeys(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Tick();
            Tick();
            Tick();

            // Assert
            Assert.That(item.style.translate.value.y.value, Is.EqualTo(0f));
        }
    }
}
