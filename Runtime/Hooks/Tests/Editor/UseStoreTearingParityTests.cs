using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the cross-tier tearing guard of <see cref="Hooks.UseStore"/>: every reader of the same
    /// store within one batch drain wave observes the SAME snapshot, even when an ancestor lands on the
    /// immediate tier (Normal lane, this frame) and a descendant on the delayed tier (Transition lane,
    /// +DeferredDelayMs) and the store mutates between their tier drains. This guarantees
    /// snapshot consistency (no tearing within a commit), and the convergence
    /// to the latest snapshot once the mutation's follow-up render reaches the next immediate drain.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>UseStoreTests</c> for the store / component shape and <c>AutoBatchingTests</c> for the
    /// tier access (<c>mounted.Root.Reconciler.Context.BatchScheduler</c>, DrainImmediate/DrainDelayed for
    /// test). The UIToolkit scheduler does not advance in EditMode, so the two tier drains are simulated
    /// directly and the store is mutated between them to open the tearing window.
    /// </remarks>
    [TestFixture]
    internal sealed class UseStoreTearingParityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Reset();
        }

        private static FiberBatchScheduler Scheduler(MountedTree mounted)
            => mounted.Root.Reconciler.Context.BatchScheduler;

        [Test]
        public void Given_AncestorImmediateAndDescendantDelayed_When_StoreMutatesBetweenDrains_Then_BothObserveSameSnapshot()
        {
            // Arrange — ancestor and descendant both read the same store value.
            using var store = new TestCounterStore(initial: 0);
            s_store = store;
            using var mounted = MountAncestorDescendant();
            Assume.That((s_ancestorValue, s_descendantValue), Is.EqualTo((0, 0)),
                "Precondition: both read the initial snapshot on mount");
            Assume.That(s_ancestorFiber, Is.Not.Null);
            Assume.That(s_descendantFiber, Is.Not.Null);

            // Ancestor re-renders on the Normal lane (immediate tier); descendant on the Transition lane
            // (delayed tier). They now sit on different tiers separated by DeferredDelayMs.
            s_ancestorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_descendantFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Act — the immediate tier commits the ancestor; the store then mutates inside the window before
            // the delayed tier commits the descendant.
            Scheduler(mounted).DrainImmediateForTest();
            var ancestorAfterImmediate = s_ancestorValue;
            store.SetValue(1);
            Scheduler(mounted).DrainDelayedForTest();

            // Assert — the descendant observes the same snapshot the ancestor committed in this wave, not the
            // newer live store.Current. Without the tearing guard the descendant reads 1 while the ancestor
            // committed 0 (torn for up to DeferredDelayMs).
            Assert.AreEqual(ancestorAfterImmediate, s_descendantValue,
                "The delayed-tier descendant observes the same store snapshot the immediate-tier ancestor committed");
            Assert.AreEqual(0, s_descendantValue,
                "Both readers stay pinned to the snapshot of the wave the immediate drain opened");
        }

        [Test]
        public void Given_StoreMutatedMidWave_When_NextImmediateDrains_Then_BothConvergeToLatestSnapshot()
        {
            // Arrange
            using var store = new TestCounterStore(initial: 0);
            s_store = store;
            using var mounted = MountAncestorDescendant();
            s_ancestorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_descendantFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            Scheduler(mounted).DrainImmediateForTest();
            store.SetValue(1);
            Scheduler(mounted).DrainDelayedForTest();
            Assume.That((s_ancestorValue, s_descendantValue), Is.EqualTo((0, 0)),
                "Precondition: this wave stayed pinned to the old snapshot");

            // Act — the mutation re-scheduled both readers; the next immediate drain opens a fresh wave that
            // re-pins to the now-current snapshot.
            Scheduler(mounted).DrainImmediateForTest();
            Scheduler(mounted).DrainDelayedForTest();

            // Assert
            Assert.AreEqual((1, 1), (s_ancestorValue, s_descendantValue),
                "After the mutation's follow-up render, both readers converge to the latest snapshot");
        }

        private MountedTree MountAncestorDescendant()
        {
            var tree = V.Div(name: "host", children: new VNode[]
            {
                V.Component(AncestorRender, key: "ancestor"),
                V.Component(DescendantRender, key: "descendant"),
            });
            return V.Mount(_root, tree);
        }

        private static Store<int> s_store;
        private static int s_ancestorValue;
        private static int s_descendantValue;
        private static ComponentFiber s_ancestorFiber;
        private static ComponentFiber s_descendantFiber;

        private static void Reset()
        {
            s_store = null;
            s_ancestorValue = 0;
            s_descendantValue = 0;
            s_ancestorFiber = null;
            s_descendantFiber = null;
        }

        [Component]
        private static VNode AncestorRender()
        {
            s_ancestorFiber = FiberAmbientStack.Current;
            s_ancestorValue = Hooks.UseStore(s_store, s => s);
            return V.Label(text: s_ancestorValue.ToString());
        }

        [Component]
        private static VNode DescendantRender()
        {
            s_descendantFiber = FiberAmbientStack.Current;
            s_descendantValue = Hooks.UseStore(s_store, s => s);
            return V.Label(text: s_descendantValue.ToString());
        }

        private sealed class TestCounterStore : Store<int>
        {
            public TestCounterStore(int initial) : base(initial) { }
            public void SetValue(int v) => SetState(_ => v);
            protected override void ResetCore() => SetState(_ => 0);
        }
    }
}
