using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a state update renders that a commit's mutation phase makes on a component of another root:
    /// a ref cleanup as its element is removed or as its ref is dropped from an element that stays, a layout
    /// cleanup or a ref cleanup as a Suspense hides the children it showed, and a layout cleanup as a
    /// <c>V.VirtualList</c> row is taken out of the range. That root renders it before control returns from the
    /// work that ran the commit, as React runs its mutation phase at the discrete event priority, which gives an
    /// update made there outside a transition the sync lane whichever root it targets; so does the list's own root,
    /// for an update the row's layout cleanup makes on it. So does an update a layout effect makes as a transition
    /// callback mounts its root, as React clears the transition for a commit, while an update that callback makes
    /// after the mount is still the transition's. An update a removed row's passive cleanup makes takes no sync
    /// lane, and the row's insertion cleanup still runs as it leaves.
    /// </summary>
    [TestFixture]
    internal sealed class MutationPhaseUpdateOnAnotherRootTests
    {
        private static readonly List<MountedTree> s_mounts = new();
        private static StateUpdater<int> s_setShown;
        private static int s_shown;
        private static StateUpdater<int> s_setWriter;
        private static TransitionStarter s_start;
        private static bool s_passiveCleanupRan;
        private static int s_insertionCleanups;
        private static StateUpdater<int> s_setListHost;
        private static int s_listHostShown;

        [SetUp]
        public void SetUp()
        {
            FiberStrictMode.Enabled = false;
            s_setShown = default;
            s_shown = -1;
            s_setWriter = default;
            s_start = default;
            s_passiveCleanupRan = false;
            s_insertionCleanups = 0;
            s_setListHost = default;
            s_listHostShown = -1;
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = s_mounts.Count - 1; i >= 0; i--) s_mounts[i].Dispose();
            s_mounts.Clear();
        }

        private static MountedTree Mount(VNode tree)
        {
            var mounted = V.Mount(new VisualElement(), tree);
            s_mounts.Add(mounted);
            return mounted;
        }

        // The other root: records the value it last rendered and hands out its setter.
        [Component]
        private static VNode ShowsItsValue()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setShown = setValue;
            s_shown = value;
            return V.Div();
        }

        private static MountedTree MountTheOtherRootThen(VNode writer)
        {
            Mount(V.Component(ShowsItsValue));
            return Mount(writer);
        }

        private static readonly Func<VisualElement, Action> s_refSettingTheOtherRootAsItDetaches =
            _ => () => s_setShown.Invoke(1);

        // --- A ref detached in the mutation phase ---

        [Component]
        private static VNode RemovesAnElementWithARefOnItsUpdate()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setWriter = setTick;
            return V.Div(children: new VNode[]
            {
                tick == 0 ? V.Div(refCallback: s_refSettingTheOtherRootAsItDetaches) : null,
            });
        }

        [Test]
        public void Given_ARefWhoseCleanupSetsAnotherRootsState_When_ADrainRemovesItsElement_Then_TheOtherRootHasRenderedTheUpdateWhenTheDrainReturns()
        {
            // Arrange
            var writer = MountTheOtherRootThen(V.Component(RemovesAnElementWithARefOnItsUpdate));
            s_setWriter.Invoke(1);

            // Act
            writer.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_shown, Is.EqualTo(1));
        }

        [Component]
        private static VNode DropsItsRefOnItsUpdate()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setWriter = setTick;
            return V.Div(refCallback: tick == 0 ? s_refSettingTheOtherRootAsItDetaches : null);
        }

        [Test]
        public void Given_ARefWhoseCleanupSetsAnotherRootsState_When_ADrainDropsTheRefFromItsElement_Then_TheOtherRootHasRenderedTheUpdateWhenTheDrainReturns()
        {
            // Arrange
            var writer = MountTheOtherRootThen(V.Component(DropsItsRefOnItsUpdate));
            s_setWriter.Invoke(1);

            // Act
            writer.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_shown, Is.EqualTo(1));
        }

        // --- A Suspense hiding the children it showed ---

        // Waits on a read that never completes once its state has changed.
        [Component]
        private static VNode SuspendsOnItsUpdate()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setWriter = setTick;
            var value = Hooks.Use<int>(_ => tick == 0
                ? VelvetTask.FromResult(0)
                : new VelvetTaskCompletionSource<int>().Task, tick);
            return V.Label(text: "read:" + value);
        }

        [Component]
        private static VNode SetsTheOtherRootFromItsLayoutCleanup()
        {
            Hooks.UseLayoutEffect(() => () => s_setShown.Invoke(1), Array.Empty<object>());
            return V.Div();
        }

        [Component]
        private static VNode HidesALayoutCleanupOnASuspend()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Div(children: new VNode[] { V.Component(SetsTheOtherRootFromItsLayoutCleanup, key: "cleanup") }),
                    V.Component(SuspendsOnItsUpdate, key: "reader"),
                });

        [Component]
        private static VNode HidesARefOnASuspend()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Div(refCallback: s_refSettingTheOtherRootAsItDetaches),
                    V.Component(SuspendsOnItsUpdate, key: "reader"),
                });

        private static void SuspendTheReader(MountedTree writer)
        {
            s_setWriter.Invoke(1);
            writer.FlushStateForTest();
            writer.GetSchedulerForTest().DrainImmediateForTest();
        }

        [Test]
        public void Given_ALayoutCleanupInASuspensesChildrenThatSetsAnotherRootsState_When_TheBoundaryHidesThem_Then_TheOtherRootHasRenderedTheUpdate()
        {
            // Arrange
            var writer = MountTheOtherRootThen(V.Component(HidesALayoutCleanupOnASuspend));

            // Act
            SuspendTheReader(writer);

            // Assert
            Assert.That(s_shown, Is.EqualTo(1));
        }

        [Test]
        public void Given_ARefInASuspensesChildrenWhoseCleanupSetsAnotherRootsState_When_TheBoundaryHidesThem_Then_TheOtherRootHasRenderedTheUpdate()
        {
            // Arrange
            var writer = MountTheOtherRootThen(V.Component(HidesARefOnASuspend));

            // Act
            SuspendTheReader(writer);

            // Assert
            Assert.That(s_shown, Is.EqualTo(1));
        }

        // --- A list row taken out of the range ---

        [Component]
        private static VNode SetsTheOtherRootFromItsPassiveCleanup()
        {
            Hooks.UseEffect(() => () =>
            {
                s_passiveCleanupRan = true;
                s_setShown.Invoke(1);
            }, Array.Empty<object>());
            return V.Div();
        }

        // Two items of the viewport's height, the first in range: a scroll of one item takes its row out.
        private static FiberVirtualListController MountAListShowingItsFirstRow(Func<VNode> row)
        {
            Mount(V.Component(ShowsItsValue));
            var host = new VisualElement();
            s_mounts.Add(V.Mount(host, V.VirtualList(
                items: new[] { "first", "second" },
                keySelector: item => item,
                itemHeight: 50f,
                renderer: item => V.Div(key: item, children: new VNode[] { row() }),
                overscan: 0)));
            var list = s_mounts[s_mounts.Count - 1].Root.Reconciler.Context.VirtualListControllers[host.Q<ScrollView>()];
            list.UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);
            return list;
        }

        [Test]
        public void Given_AListRowWhoseLayoutCleanupSetsAnotherRootsState_When_ARangeUpdateTakesTheRowOut_Then_TheOtherRootHasRenderedTheUpdateWhenTheUpdateReturns()
        {
            // Arrange
            var list = MountAListShowingItsFirstRow(() => V.Component(SetsTheOtherRootFromItsLayoutCleanup, key: "row"));

            // Act
            list.UpdateVisibleRange(scrollY: 50f, viewportHeight: 50f);

            // Assert
            Assert.That(s_shown, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base leaves the update a removed row's passive cleanup makes for a drain.
        // React runs a deleted component's passive cleanups in its passive phase, at no higher than the default
        // priority, so the mutation work the row's other cleanups now run in must end before them.
        [Test]
        public void Given_AListRowWhosePassiveCleanupSetsAnotherRootsState_When_ARangeUpdateTakesTheRowOut_Then_TheOtherRootHasNotRenderedWhenTheUpdateReturns()
        {
            // Arrange
            var list = MountAListShowingItsFirstRow(() => V.Component(SetsTheOtherRootFromItsPassiveCleanup, key: "row"));

            // Act
            list.UpdateVisibleRange(scrollY: 50f, viewportHeight: 50f);

            // Assert — -2 where the row's passive cleanup did not run
            Assert.That(s_passiveCleanupRan ? s_shown : -2, Is.Zero);
        }

        [Component]
        private static VNode CountsItsInsertionCleanups()
        {
            Hooks.UseInsertionEffect(() => () => s_insertionCleanups++, Array.Empty<object>());
            return V.Div();
        }

        // GREEN_ON_BASE(characterization): the base runs a row's insertion cleanup as a range update takes the row out.
        // This change moves that cleanup into the mutation work beside the layout cleanups, and pins it still runs.
        [Test]
        public void Given_AListRowWithAnInsertionEffect_When_ARangeUpdateTakesTheRowOut_Then_ItsInsertionCleanupHasRunWhenTheUpdateReturns()
        {
            // Arrange
            var list = MountAListShowingItsFirstRow(() => V.Component(CountsItsInsertionCleanups, key: "row"));

            // Act
            list.UpdateVisibleRange(scrollY: 50f, viewportHeight: 50f);

            // Assert
            Assert.That(s_insertionCleanups, Is.EqualTo(1));
        }

        [Component]
        private static VNode SetsItsListsHostFromItsLayoutCleanup()
        {
            Hooks.UseLayoutEffect(() => () => s_setListHost.Invoke(1), Array.Empty<object>());
            return V.Div();
        }

        // The root that holds the list: records the value it last rendered and hands out its setter.
        [Component]
        private static VNode HostsAList()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setListHost = setValue;
            s_listHostShown = value;
            return V.VirtualList(
                items: new[] { "first", "second" },
                keySelector: item => item,
                itemHeight: 50f,
                renderer: item => V.Div(key: item, children: new VNode[]
                {
                    V.Component(SetsItsListsHostFromItsLayoutCleanup, key: "row"),
                }),
                overscan: 0);
        }

        [Test]
        public void Given_AListRowWhoseLayoutCleanupSetsItsOwnRootsState_When_ARangeUpdateTakesTheRowOut_Then_ThatRootHasRenderedTheUpdateWhenTheUpdateReturns()
        {
            // Arrange
            var host = new VisualElement();
            s_mounts.Add(V.Mount(host, V.Component(HostsAList)));
            var list = s_mounts[0].Root.Reconciler.Context.VirtualListControllers[host.Q<ScrollView>()];
            list.UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);

            // Act
            list.UpdateVisibleRange(scrollY: 50f, viewportHeight: 50f);

            // Assert
            Assert.That(s_listHostShown, Is.EqualTo(1));
        }

        // --- A transition open around the work that runs a commit ---

        [Component]
        private static VNode StartsTransitions()
        {
            var (_, start) = Hooks.UseTransition();
            s_start = start;
            return V.Div();
        }

        [Component]
        private static VNode SetsTheOtherRootFromItsMountLayoutEffect()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_setShown.Invoke(1);
                return null;
            }), Array.Empty<object>());
            return V.Div();
        }

        [Test]
        public void Given_ATransitionCallbackThatMountsARoot_When_ThatMountsLayoutEffectSetsAnotherRootsState_Then_TheOtherRootHasRenderedTheUpdateWhenTheMountReturns()
        {
            // Arrange
            Mount(V.Component(StartsTransitions));
            Mount(V.Component(ShowsItsValue));

            // Act
            s_start.Invoke(() => Mount(V.Component(SetsTheOtherRootFromItsMountLayoutEffect)));

            // Assert
            Assert.That(s_shown, Is.EqualTo(1));
        }

        [Component]
        private static VNode HasALayoutEffect()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() => null), Array.Empty<object>());
            return V.Div();
        }

        // GREEN_ON_BASE(characterization): the base gives the transition lane to a callback's update after its mount.
        // The commit inside that mount sets the open transition aside, and this pins that it hands the transition back.
        [Test]
        public void Given_ATransitionCallbackThatMountsARoot_When_ItSetsAnotherRootsStateAfterTheMount_Then_ThatUpdateWaitsOnTheTransitionTier()
        {
            // Arrange
            Mount(V.Component(StartsTransitions));
            var other = Mount(V.Component(ShowsItsValue));

            // Act
            s_start.Invoke(() =>
            {
                Mount(V.Component(HasALayoutEffect));
                s_setShown.Invoke(1);
            });

            // Assert
            Assert.That(other.GetSchedulerForTest().DelayedPendingCount, Is.EqualTo(1));
        }
    }
}
