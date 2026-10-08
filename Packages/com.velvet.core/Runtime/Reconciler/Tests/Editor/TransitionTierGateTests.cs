using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which tier renders the Transition-lane work a fiber still holds when the immediate tier reaches
    /// its buffered entry — the shape a store-driven parent and a child deferring the same value produce, where
    /// one update queues both and the parent's render subsumes the child and re-requests its deferred value.
    /// <list type="bullet">
    /// <item>The immediate drain renders that child once, inside the parent's pass, and leaves the deferred
    /// value to the delayed tier.</item>
    /// <item>The delayed drain renders it once more, committing the deferred value and leaving nothing pending,
    /// behind Transition-lane work queued before it.</item>
    /// <item>The same holds for immediate work a delayed drain's commit spawns, and for an entry whose
    /// Transition lane no tier held.</item>
    /// <item>A fiber whose Transition lane sits behind Normal work still drains in the immediate tier, so
    /// starvation promotion reaches it there.</item>
    /// <item>Unmounting the child before the delayed drain takes it off that tier.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class TransitionTierGateTests
    {
        private VisualElement _root;

        private static StateUpdater<string> s_setQuery;
        private static StateUpdater<bool> s_setShowChild;
        private static StateUpdater<int> s_setChildTick;
        private static ComponentFiber s_childFiber;
        private static ComponentFiber s_bystanderFiber;
        private static int s_childRenders;
        private static bool s_writeOnBystanderCommit;
        private static readonly List<string> s_renderLog = new();

        private static StateUpdater<string> s_starvedSetValue;
        private static TransitionStarter s_starvedStart;
        private static ComponentFiber s_starvedFiber;

        [SetUp]
        public void SetUp()
        {
            FiberWorkLoop.IsInDiscreteEvent = false;
            _root = new VisualElement();
            s_setQuery = default;
            s_setShowChild = default;
            s_setChildTick = default;
            s_childFiber = null;
            s_bystanderFiber = null;
            s_childRenders = 0;
            s_writeOnBystanderCommit = false;
            s_renderLog.Clear();
            s_starvedSetValue = default;
            s_starvedStart = default;
            s_starvedFiber = null;
        }

        [Test]
        public void Given_AChildQueuedBesideTheParentThatSubsumesIt_When_TheImmediateDrainRuns_Then_ItRendersOnlyInsideTheParentsPass()
        {
            // Arrange
            using var mounted = MountTree();
            var rendersBefore = s_childRenders;
            QueueParentAndChild();

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the label rides along because the parent subsumes the child whether or not the child
            // queued an entry of its own, and the child's own update is what shows that it did
            Assert.That((s_childRenders - rendersBefore, ChildText()), Is.EqualTo((1, "alpha:1")),
                "The child's own entry leaves the deferred value it re-requested to the delayed tier");
        }

        [Test]
        public void Given_AChildSubsumedByAnImmediateDrain_When_TheDelayedDrainRuns_Then_ItRendersOnceAndCommitsTheDeferredValue()
        {
            // Arrange
            using var mounted = MountTree();
            QueueParentAndChild();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var rendersBefore = s_childRenders;

            // Act
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That((s_childRenders - rendersBefore, ChildText()), Is.EqualTo((1, "beta:1")),
                "The delayed drain is where the deferred value commits");
        }

        // GREEN_ON_BASE(characterization): the base also left the child clean once both drains ran.
        // Ignore `immediateTier` in the gate, so the delayed drain skips the child too, and this reddens.
        [Test]
        public void Given_AChildSubsumedByAnImmediateDrain_When_TheDelayedDrainRuns_Then_NothingIsLeftPending()
        {
            // Arrange
            using var mounted = MountTree();
            var scheduler = mounted.GetSchedulerForTest();
            QueueParentAndChild();
            scheduler.DrainImmediateForTest();

            // Act
            scheduler.DrainDelayedForTest();

            // Assert — the label rides along because a tree nothing was queued on is clean as well
            Assert.That(
                (ChildText(), s_childFiber.IsDirty, s_childFiber.LaneQueue.Count,
                 scheduler.ImmediatePendingCount, scheduler.DelayedPendingCount),
                Is.EqualTo(("beta:1", false, 0, 0, 0)),
                "The deferred commit leaves the child clean and neither tier holding it");
        }

        [Test]
        public void Given_ATransitionAlreadyQueued_When_TheDelayedDrainAlsoFindsTheSubsumingPairQueued_Then_TheDeferredValueRendersBehindIt()
        {
            // Arrange — the bystander's Transition lane is enrolled first, and it sits shallower than the child
            using var mounted = MountTree();
            s_bystanderFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            QueueParentAndChild();
            s_renderLog.Clear();

            // Act — the delayed drain commits the immediate tier's queue before its own
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(string.Join(", ", s_renderLog), Is.EqualTo("child:alpha, bystander, child:beta"),
                "The deferred value joins the delayed queue rather than rendering in the immediate pass ahead of it");
        }

        [Test]
        public void Given_ADelayedDrainWhoseCommitQueuesTheSubsumingPair_When_ItsBoundaryPassRuns_Then_TheDeferredValueWaitsForTheNextDelayedDrain()
        {
            // Arrange — the bystander's layout effect writes both states during the delayed drain's commit
            using var mounted = MountTree();
            s_writeOnBystanderCommit = true;
            s_bystanderFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Act
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert — the tick shows the boundary pass ran; the deferred half shows what it withheld
            Assert.That(ChildText(), Is.EqualTo("alpha:1"),
                "The boundary pass commits the urgent work and hands the deferred value to the delayed tier");
        }

        [Test]
        public void Given_AnImmediateEntryWhoseOnlyLaneIsATransitionNoTierHolds_When_TheImmediateDrainSkipsIt_Then_TheDelayedDrainRendersIt()
        {
            // Arrange — Normal enrols the immediate tier; a Transition added to the already-dirty fiber enrols
            // nothing, and removing the Normal underneath it leaves no tier holding that lane
            using var mounted = MountTree();
            var scheduler = mounted.GetSchedulerForTest();
            s_setChildTick.Invoke(1);
            s_childFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_childFiber.Lanes.Queue.Remove(FiberUpdatePriority.Normal);
            var rendersBefore = s_childRenders;
            scheduler.DrainImmediateForTest();
            var rendersInImmediateDrain = s_childRenders - rendersBefore;

            // Act
            scheduler.DrainDelayedForTest();

            // Assert — the immediate drain's count rides along because with the Normal lane left in place the
            // delayed drain renders the child too, through the enrolment FlushState makes
            Assert.That((rendersInImmediateDrain, s_childRenders - rendersBefore - rendersInImmediateDrain),
                Is.EqualTo((0, 1)),
                "The skip enrols the entry on the delayed tier itself");
        }

        // GREEN_ON_BASE(characterization): the base also promoted a starved lane in the immediate drain.
        // Gate on `LaneQueue.Contains(FiberUpdatePriority.Transition)` rather than on `Min` and this reddens.
        [Test]
        public void Given_ATransitionStarvedBehindNormalWork_When_TheThresholdImmediateDrainRuns_Then_ThePromotedWorkDrainsThere()
        {
            // Arrange — only the immediate tier drains, so nothing but promotion can reach the Transition lane
            using var mounted = V.Mount(_root, V.Component(StarvedRender, key: "starved"));
            var scheduler = mounted.GetSchedulerForTest();
            s_starvedStart.Invoke(() => s_starvedSetValue.Invoke("transition"));
            for (var i = 0; i < StarvationThreshold.Read() - 1; i++)
            {
                s_starvedSetValue.Invoke($"normal-{i}");
                scheduler.DrainImmediateForTest();
            }
            s_starvedSetValue.Invoke("normal-last");
            var starvedBefore = s_starvedFiber.LaneQueue.Contains(FiberUpdatePriority.Transition);

            // Act
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That(
                (starvedBefore, s_starvedFiber.LaneQueue.Contains(FiberUpdatePriority.Transition),
                 s_starvedFiber.IsDirty),
                Is.EqualTo((true, false, false)),
                "A fiber holding Normal work flushes in the immediate drain, where its starved lane is promoted");
        }

        // GREEN_ON_BASE(characterization): the base also dropped an unmounted child from the delayed tier.
        // Delete `_delayedOrder.Remove(fiber)` from `FiberBatchScheduler.Remove` and this reddens.
        [Test]
        public void Given_AChildTheImmediateDrainHandedToTheDelayedTier_When_ItUnmountsFirst_Then_TheDelayedTierNoLongerHoldsIt()
        {
            // Arrange
            using var mounted = MountTree();
            var scheduler = mounted.GetSchedulerForTest();
            QueueParentAndChild();
            scheduler.DrainImmediateForTest();
            var heldBefore = scheduler.DelayedPendingCount;
            s_setShowChild.Invoke(false);

            // Act
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That((heldBefore, scheduler.DelayedPendingCount), Is.EqualTo((1, 0)),
                "Unmounting the child takes it off the delayed tier before that tier drains");
        }

        private string ChildText() => _root.Q<Label>("child-out").text;

        private MountedTree MountTree()
            => V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Component(BystanderRender, key: "bystander"),
                V.Component(ParentRender, key: "parent"),
            }));

        // The child's entry is queued first; the drain sorts the parent ahead of it either way.
        private static void QueueParentAndChild()
        {
            s_setChildTick.Invoke(1);
            s_setQuery.Invoke("beta");
        }

        // Compiler = false throughout, as FlushCoalescingParityTests does, so these scheduler cases do not also
        // depend on the memoization axis.
        [Component(Compiler = false)]
        private static VNode BystanderRender()
        {
            s_bystanderFiber = FiberAmbientStack.Current;
            s_renderLog.Add("bystander");
            Hooks.UseLayoutEffect(() =>
            {
                if (s_writeOnBystanderCommit)
                {
                    s_writeOnBystanderCommit = false;
                    QueueParentAndChild();
                }
                return (Action)null;
            });
            return V.Label();
        }

        [Component(Compiler = false)]
        private static VNode ParentRender()
        {
            var (query, setQuery) = Hooks.UseState("alpha");
            var (showChild, setShowChild) = Hooks.UseState(true);
            s_setQuery = setQuery;
            s_setShowChild = setShowChild;
            return V.Div(children: new VNode[]
            {
                V.Label(text: query),
                showChild ? V.Component(ChildRender, query, key: "child") : null,
            });
        }

        [Component(Compiler = false)]
        private static VNode ChildRender(string query)
        {
            s_childFiber = FiberAmbientStack.Current;
            s_childRenders++;
            var (tick, setTick) = Hooks.UseState(0);
            s_setChildTick = setTick;
            var deferred = Hooks.UseDeferredValue(query);
            s_renderLog.Add($"child:{deferred}");
            return V.Label(name: "child-out", text: $"{deferred}:{tick}");
        }

        [Component(Compiler = false)]
        private static VNode StarvedRender()
        {
            s_starvedFiber = FiberAmbientStack.Current;
            var (value, setValue) = Hooks.UseState("initial");
            var (_, start) = Hooks.UseTransition();
            s_starvedSetValue = setValue;
            s_starvedStart = start;
            return V.Label(text: value);
        }
    }
}
