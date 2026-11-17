using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Bounds the GC.Alloc blocks a setter's re-render of a component costs, from the setter through the immediate
    /// tier's scheduled drain and the layout commit, and specifies that the passive flush after it and a resume of a
    /// tree with no parked pass allocate nothing. The component re-renders on its own under a Provider, with a keyed
    /// child it renders inline, and both carry an effect whose deps each re-render changes. Their deps and texts
    /// come from static arrays, so a change of state builds neither. Two sibling components re-rendered by one drain
    /// cost no more than the two drained one after the other.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class RenderCommitAllocationTests
    {
        // The case asserts at most this many blocks, so a change that takes a further allocation off the path
        // leaves it green.
        private const int RenderAndCommitBlocks = 40;

        private static readonly ComponentContext<string> s_theme = ComponentContext<string>.Create("default");
        private static readonly object[][] s_deps = { new object[] { 0 }, new object[] { 1 } };
        private static readonly string[] s_texts = { "0", "1" };
        private static readonly Func<Action?> s_effect = static () => null;

        private static StateUpdater<int> s_set;
        private static int s_value;

        private MountedTree _mounted = null!;
        private ReconcilerContext _context = null!;
        private Action _runImmediateCallback = null!;
        private int _next;

        [SetUp]
        public void SetUp()
        {
            _mounted = V.Mount(new VisualElement(), V.Provider(s_theme, "dark", new VNode?[]
            {
                V.Component(Parent, key: "parent"),
            }));
            _context = _mounted.Root.Reconciler!.Context;
            var scheduler = _mounted.GetSchedulerForTest();
            var callback = typeof(FiberBatchScheduler).GetMethod(
                "RunImmediateCallback", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(FiberBatchScheduler), "RunImmediateCallback");
            _runImmediateCallback = (Action)callback.CreateDelegate(typeof(Action), scheduler);
            _next = 0;
            for (var i = 0; i < 16; i++)
            {
                ReRender();
                FlushPassiveEffects();
            }
        }

        [TearDown]
        public void TearDown() => _mounted.Dispose();

        [Component(Compiler = false)]
        private static VNode Parent()
        {
            var (value, set) = Hooks.UseState(0);
            s_set = set;
            s_value = value;
            Hooks.UseContext(s_theme);
            Hooks.UseEffect(s_effect, s_deps[value]);
            return V.Fragment(V.Label(text: s_texts[value]), V.Component(Child, key: "child"));
        }

        [Component(Compiler = false)]
        private static VNode Child()
        {
            Hooks.UseEffect(s_effect, s_deps[s_value]);
            return V.Label(text: s_texts[s_value]);
        }

        private void ReRender()
        {
            _next ^= 1;
            s_set.Invoke(_next);
            _runImmediateCallback();
        }

        private void FlushPassiveEffects() => FiberEffects.FlushPendingPassiveEffects(_context);

        [Test]
        public void Given_AComponentUnderAProvider_When_ASetterReRendersAndCommitsIt_Then_ItAllocatesNoMoreThanItsBudget()
        {
            // Arrange — the passive flush runs outside each window, so every window's commit arms a drain of its own.
            var canary = GCAllocationCanary.BlocksForKnownArray();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(FlushPassiveEffects, ReRender);

            // Assert — a probe stuck at zero reads as over budget.
            Assert.That(canary > 0 ? blocks : int.MaxValue, Is.LessThanOrEqualTo(RenderAndCommitBlocks));
        }

        [Test]
        public void Given_ACommitThatStagedAParentAndItsChild_When_ThePassiveEffectsFlush_Then_NothingIsAllocated()
        {
            // Arrange
            var canary = GCAllocationCanary.BlocksForKnownArray();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(ReRender, FlushPassiveEffects);

            // Assert — a probe stuck at zero reads as an allocation.
            Assert.That(canary > 0 ? blocks : -1, Is.Zero);
        }

        private static StateUpdater<int> s_setFirstRow;
        private static StateUpdater<int> s_setSecondRow;
        private static int s_firstRow;
        private static int s_secondRow;

        // Each re-render swaps its row for one of another key, so the drain removes one row and creates one.
        [Component(Compiler = false)]
        private static VNode FirstRow()
        {
            var (value, set) = Hooks.UseState(0);
            s_setFirstRow = set;
            return V.Label(key: s_texts[value], text: s_texts[value]);
        }

        [Component(Compiler = false)]
        private static VNode SecondRow()
        {
            var (value, set) = Hooks.UseState(0);
            s_setSecondRow = set;
            return V.Label(key: s_texts[value], text: s_texts[value]);
        }

        [Component(Compiler = false)]
        private static VNode TwoRows()
            => V.Fragment(V.Component(FirstRow, key: "first"), V.Component(SecondRow, key: "second"));

        // GREEN_ON_BASE(characterization): the base renders both rows in one drain for no more than two drains cost.
        // Allocating in Reconciler.BeginOrJoinPass for each render that joins the wave's pass reddens it.
        [Test]
        public void Given_TwoSiblingComponentsThatEachReplaceARow_When_OneDrainReRendersBoth_Then_ItAllocatesNoMoreThanTheTwoDrainedApart()
        {
            // Arrange — every run below flips each row once, and the warm-up fills the pools the rows draw on.
            using var rows = V.Mount(new VisualElement(), V.Component(TwoRows));
            var context = rows.Root.Reconciler!.Context;
            var callback = typeof(FiberBatchScheduler).GetMethod(
                "RunImmediateCallback", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(FiberBatchScheduler), "RunImmediateCallback");
            var runImmediateCallback = (Action)callback.CreateDelegate(typeof(Action), rows.GetSchedulerForTest());
            void FlushRowsPassiveEffects() => FiberEffects.FlushPendingPassiveEffects(context);
            void FlipBothInOneDrain()
            {
                s_setFirstRow.Invoke(s_firstRow ^= 1);
                s_setSecondRow.Invoke(s_secondRow ^= 1);
                runImmediateCallback();
            }
            void FlipEachInADrainOfItsOwn()
            {
                s_setFirstRow.Invoke(s_firstRow ^= 1);
                runImmediateCallback();
                FlushRowsPassiveEffects();
                s_setSecondRow.Invoke(s_secondRow ^= 1);
                runImmediateCallback();
            }
            for (var i = 0; i < 16; i++)
            {
                FlipBothInOneDrain();
                FlipEachInADrainOfItsOwn();
                FlushRowsPassiveEffects();
            }
            var canary = GCAllocationCanary.BlocksForKnownArray();

            // Act
            var together = GCAllocationProbe.MedianBlocksDuring(FlushRowsPassiveEffects, FlipBothInOneDrain);
            var apart = GCAllocationProbe.MedianBlocksDuring(FlushRowsPassiveEffects, FlipEachInADrainOfItsOwn);

            // Assert — a probe stuck at zero reads as over budget.
            Assert.That(canary > 0 ? together - apart : int.MaxValue, Is.LessThanOrEqualTo(0));
        }

        [Test]
        public void Given_ATreeWithNoParkedPass_When_AResumeOfItsRootRuns_Then_NothingIsAllocated()
        {
            // Arrange
            var canary = GCAllocationCanary.BlocksForKnownArray();
            var root = _mounted.Root;

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(() => FiberWorkLoop.ContinueReconcile(root));

            // Assert — a probe stuck at zero reads as an allocation.
            Assert.That(canary > 0 ? blocks : -1, Is.Zero);
        }
    }
}
