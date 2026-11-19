using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where <see cref="Hooks.UseOptimistic{TState,TAction}"/> takes a settled transition's entries
    /// down when the transition's last work commits: in that commit, for every component that holds them.
    /// <list type="bullet">
    /// <item>A holder the drain landing the work does not render, and the component reading the value it
    /// holds, show the pass-through state by the time the drain's layout effects run.</item>
    /// <item>So do a holder the drain's pass rendered before the work of a descendant it subsumes settled the
    /// transition, and its reader.</item>
    /// <item>So do they when the flush runs outside a drain, whose layout effects run inline, and when the
    /// settle is the terminal slice of a time-sliced reconcile, or the abort of one whose resumed slice throws
    /// into a boundary above it.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// What a commit shows is read from the labels in the tree by a layout effect, which a drain runs after
    /// every fiber it flushes has rendered. Each case compares that reading with the one taken before the
    /// drain, so a value the entry never reached cannot satisfy it.
    /// </remarks>
    [TestFixture]
    internal sealed class UseOptimisticRevertCommitTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            AsyncActionsInFlightTestAccess.ResetForTest();
            s_observedRoot = _root;
            s_seenAtCommit.Clear();
            s_add = null;
            s_start = default;
            s_setTick = default;
            s_setParentTick = default;
            s_fiber = null;
            s_refuse = false;
            s_seenInFallback.Clear();
        }

        [TearDown]
        public void TearDown() => AsyncActionsInFlightTestAccess.ResetForTest();

        [Test]
        public void Given_AHolderTheLandingDrainDoesNotRender_When_TheTransitionsLastWorkCommits_Then_TheHolderAndItsReaderShowThePassthroughAtThatCommit()
        {
            // Arrange — the transition's write is on a sibling, so the drain landing it never renders the holder
            using var mounted = V.Mount(_root, V.Component(OutsideRootRender, key: "outside-root"));
            var scheduler = mounted.GetSchedulerForTest();
            s_start.Invoke(() =>
            {
                s_add.Invoke("sent");
                s_setTick.Invoke(1);
            });
            scheduler.DrainImmediateForTest();
            var shownBefore = ReadLabels();
            var seenBefore = s_seenAtCommit.Count;

            // Act
            scheduler.DrainDelayedForTest();

            // Assert
            Assert.That($"{shownBefore}>{s_seenAtCommit.Skip(seenBefore).FirstOrDefault()}",
                Is.EqualTo("base+sent|base+sent>base|base"),
                "The drain's layout effects already read the holder and its reader without the entry");
        }

        [Test]
        public void Given_AHolderRenderedAheadOfASubsumedDescendant_When_ThatDescendantsWorkSettlesTheTransition_Then_TheHolderAndItsReaderShowThePassthroughAtThatCommit()
        {
            // Arrange — the parent's write and the last child's both belong to the transition, so the parent's
            // pass renders the holder before the child's subsumption is what settles it
            using var mounted = V.Mount(_root, V.Component(SubsumedParentRender, key: "subsumed-parent"));
            var scheduler = mounted.GetSchedulerForTest();
            s_start.Invoke(() =>
            {
                s_add.Invoke("sent");
                s_setParentTick.Invoke(1);
                s_setTick.Invoke(1);
            });
            scheduler.DrainImmediateForTest();
            var shownBefore = ReadLabels();
            var seenBefore = s_seenAtCommit.Count;

            // Act
            scheduler.DrainDelayedForTest();

            // Assert
            Assert.That($"{shownBefore}>{s_seenAtCommit.Skip(seenBefore).FirstOrDefault()}",
                Is.EqualTo("base+sent|base+sent>base|base"),
                "A holder rendered before the settle is rendered again without the entry within the same drain");
        }

        [Test]
        public void Given_AFlushOutsideEveryDrain_When_ItSettlesTheTransition_Then_TheHolderAndItsReaderShowThePassthroughAtThatCommit()
        {
            // Arrange — outside a drain the flush's layout effects run inline, ahead of anything the scheduler owes
            using var mounted = V.Mount(_root, V.Component(OutsideRootRender, key: "outside-root"));
            var sibling = s_fiber;
            s_start.Invoke(() =>
            {
                s_add.Invoke("sent");
                s_setTick.Invoke(1);
            });
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var shownBefore = ReadLabels();
            var seenBefore = s_seenAtCommit.Count;

            // Act
            FiberWorkLoop.FlushState(sibling);

            // Assert
            Assert.That($"{shownBefore}>{s_seenAtCommit.Skip(seenBefore).FirstOrDefault()}",
                Is.EqualTo("base+sent|base+sent>base|base"),
                "A flush made outside a drain reverts its holders ahead of its own layout effects");
        }

        [Test]
        public void Given_ATimeSlicedTransitionReconcile_When_ItsTerminalSliceSettlesTheTransition_Then_TheHolderAndItsReaderShowThePassthroughAtThatCommit()
        {
            // Arrange — the slice parks, and the holder sits outside the fiber whose slices commit the work
            using var mounted = V.Mount(_root, V.Component(SlicedRootRender, key: "sliced-root"));
            var sliced = s_fiber;
            s_start.Invoke(() =>
            {
                s_add.Invoke("sent");
                s_setTick.Invoke(1);
            });
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var shownBefore = ReadLabels();
            var seenBefore = s_seenAtCommit.Count;
            sliced.FlushStateWithTinyBudgetForTest();
            var parked = sliced.HasPendingReconcileWorkForTest();

            // Act
            sliced.DrainTimeSlicedReconcileForTest();

            // Assert — the park travels with the assertion, since a flush that never parked settles in FlushState
            Assert.That((parked, shownBefore, s_seenAtCommit.Skip(seenBefore).FirstOrDefault()),
                Is.EqualTo((true, "base+sent|base+sent", "base|base")),
                "The terminal slice reverts its holders ahead of the layout effects it commits");
        }

        // GREEN_ON_BASE(refactor): the refusal is armed after the immediate drain, which renders the pending state on
        // the head and the component's entry on both, so only the time-sliced pass reaches the refusing row.
        [Test]
        public void Given_ATimeSlicedTransitionReconcile_When_ItsResumedSliceThrowsPastEveryInPassBoundary_Then_TheOuterFallbackReadsTheHolderAndItsReaderWithoutTheEntry()
        {
            // Arrange — the row appended past the first slice's budget refuses construction, and the only
            // boundary is above the fiber being resumed, so the throw leaves the slice rather than being caught in it
            using var mounted = V.Mount(_root, V.Component(AbortRootRender, key: "abort-root"), CaughtErrors.Unlogged);
            var sliced = s_fiber;
            s_start.Invoke(() =>
            {
                s_add.Invoke("sent");
                s_setTick.Invoke(1);
            });
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_refuse = true;
            var shownBefore = ReadLabels();
            sliced.FlushStateWithTinyBudgetForTest();
            var parked = sliced.HasPendingReconcileWorkForTest();

            // Act
            sliced.DrainTimeSlicedReconcileForTest();

            // Assert — the park and the fallback's reading travel with the assertion, so an arrangement that
            // never threw leaves the reading empty
            Assert.That((parked, shownBefore, s_seenInFallback.FirstOrDefault()),
                Is.EqualTo((true, "base+sent|base+sent", "base|base")),
                "A slice that aborts has settled its transition, so the boundary above reads its holders without the entry");
        }

        private static VisualElement s_observedRoot;
        private static ComponentFiber s_fiber;
        private static bool s_refuse;
        private static readonly List<string> s_seenInFallback = new();

        private sealed class ConstructionRefusingElement : VisualElement
        {
            public ConstructionRefusingElement() => throw new InvalidOperationException("constructor refused");
        }
        private static readonly List<string> s_seenAtCommit = new();
        private static Action<string> s_add;
        private static TransitionStarter s_start;
        private static StateUpdater<int> s_setTick;
        private static StateUpdater<int> s_setParentTick;

        private static string ReadLabels()
            => $"{s_observedRoot.Q<Label>("holder-label").text}|{s_observedRoot.Q<Label>("reader-label").text}";

        // Unwoven: each case needs every write to a component to render it.
        [Component(Compiler = false)]
        private static VNode HolderRender()
        {
            var (optimisticState, addOptimistic) = Hooks.UseOptimistic<string, string>(
                "base",
                (current, action) => current + "+" + action);
            s_add = addOptimistic;
            return V.Div(children: new VNode[]
            {
                V.Label(name: "holder-label", text: optimisticState),
                V.Component<string>(ReaderRender, optimisticState, key: "reader"),
            });
        }

        [Component(Compiler = false)]
        private static VNode ReaderRender(string value) => V.Label(name: "reader-label", text: value);

        [Component(Compiler = false)]
        private static VNode OutsideRootRender()
        {
            var (_, start) = Hooks.UseTransition();
            s_start = start;
            return V.Div(children: new VNode[]
            {
                V.Component(HolderRender, key: "holder"),
                V.Component(OutsideSiblingRender, key: "sibling"),
            });
        }

        [Component(Compiler = false)]
        private static VNode OutsideSiblingRender()
        {
            s_fiber = FiberAmbientStack.Current;
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            // Every render, since no dependencies are given: the tree is read when the commit's layout effects run.
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_seenAtCommit.Add(ReadLabels());
                return null;
            }));
            return V.Label(text: "sibling");
        }

        [Component(Compiler = false)]
        private static VNode SlicedRootRender()
        {
            var (_, start) = Hooks.UseTransition();
            s_start = start;
            return V.Div(children: new VNode[]
            {
                V.Component(SlicedRowsRender, key: "rows"),
                V.Component(HolderRender, key: "holder"),
            });
        }

        // A top-level Fragment of host nodes takes the reconciler's time-sliceable fast path.
        [Component(Compiler = false)]
        private static VNode SlicedRowsRender()
        {
            s_fiber = FiberAmbientStack.Current;
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_seenAtCommit.Add(ReadLabels());
                return null;
            }));
            var rows = new VNode[s_refuse ? 10 : 9];
            for (var i = 0; i < 9; i++)
            {
                rows[i] = V.Label(text: $"{i}-{tick}");
            }
            if (s_refuse)
            {
                rows[9] = V.Custom<ConstructionRefusingElement>();
            }
            return V.Fragment(children: rows);
        }

        [Component(Compiler = false)]
        private static VNode AbortRootRender()
        {
            var (_, start) = Hooks.UseTransition();
            s_start = start;
            return V.Div(children: new VNode[]
            {
                V.Component(AbortBoundaryRender, key: "boundary"),
                V.Component(HolderRender, key: "holder"),
            });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode AbortBoundaryRender()
        {
            Hooks.UseFallback(_ =>
            {
                s_seenInFallback.Add(ReadLabels());
                return V.Label(text: "fallback");
            });
            return V.Component(SlicedRowsRender, key: "rows");
        }

        [Component(Compiler = false)]
        private static VNode SubsumedParentRender()
        {
            var (_, setParentTick) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_setParentTick = setParentTick;
            s_start = start;
            return V.Div(children: new VNode[]
            {
                V.Component(HolderRender, key: "holder"),
                V.Component(SubsumedLastChildRender, key: "last-child"),
            });
        }

        [Component(Compiler = false)]
        private static VNode SubsumedLastChildRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            // Every render, since no dependencies are given: the tree is read when the commit's layout effects run.
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_seenAtCommit.Add(ReadLabels());
                return null;
            }));
            return V.Label(text: "last child");
        }
    }
}
