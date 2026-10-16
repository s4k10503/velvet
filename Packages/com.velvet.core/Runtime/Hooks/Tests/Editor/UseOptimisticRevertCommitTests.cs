using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// <item>A settle no flush lands, an async action completing, leaves no render owed to a later flush.</item>
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
        public void Given_AnAsyncActionCompletingOutsideEveryFlush_When_ItsEntryIsRetired_Then_NoRenderIsLeftOwedToALaterFlush()
        {
            // Arrange — a flush has run before the completion, so a window left open by it would show
            using var mounted = V.Mount(_root, V.Component(OutsideRootRender, key: "outside-root"));
            var scheduler = mounted.GetSchedulerForTest();
            var gate = new VelvetTaskCompletionSource();
            s_start.Invoke(async () =>
            {
                s_add.Invoke("sent");
                await gate.Task;
            });
            scheduler.DrainImmediateForTest();
            scheduler.DrainDelayedForTest();
            scheduler.DrainImmediateForTest();

            // Act
            gate.TrySetResult();
            scheduler.DrainImmediateForTest();
            scheduler.DrainDelayedForTest();
            scheduler.DrainImmediateForTest();

            // Assert — the shown reading is the proof the entry was retired by the completion
            Assert.That($"{s_observedRoot.Q<Label>("holder-label").text}|{OwedRenderCount()}", Is.EqualTo("base|0"),
                "A completion that no flush lands asks its holder for a render and records none as owed");
        }

        private static int OwedRenderCount()
        {
            var owed = typeof(FiberWorkLoop).GetField("OwedSettleRenders", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingFieldException(typeof(FiberWorkLoop).FullName, "OwedSettleRenders");
            return ((ICollection)owed.GetValue(null)).Count;
        }

        private static VisualElement s_observedRoot;
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
