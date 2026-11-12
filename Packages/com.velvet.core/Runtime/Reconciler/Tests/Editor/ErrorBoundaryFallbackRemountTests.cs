using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that an error boundary mounts its fallback afresh at each catch, as React's boundary reconciles it against
    /// none of the children it replaces.
    /// <list type="bullet">
    /// <item>A component of the fallback with the type and key of one the boundary showed starts from its initial
    /// state, after an error a layout effect of the boundary's update throws, one the render of the boundary's own
    /// update throws, and one a render in the walk expanding the boundary throws, also where the fallback mounts a
    /// boundary that catches in that walk ahead of the component.</item>
    /// <item>A row the boundary showed lets go of its ref where the fallback has a row with the same key.</item>
    /// <item>Once the mount is disposed, no props bag of what the boundary showed is left rented out.</item>
    /// <item>Where a layout cleanup of a Suspense's hide throws inside the pass that hides, a fallback repeating the
    /// Suspense the boundary showed takes nothing that Suspense recorded: it keeps none of the children it never
    /// committed, whether the shown Suspense was hiding its children or showing them, and an AnimatePresence it
    /// repeats starts with state of its own. Where a catch in the walk replaces a Suspense showing its children, the
    /// fallback's Suspense keeps none of the children it never committed either.</item>
    /// <item>A component the walk mounts in place of one the boundary showed keeps its state across the parent's
    /// next update.</item>
    /// <item>Where swapping the fallback in unmounts a child whose cleanup throws, the render the boundary takes
    /// for that error mounts the fallback afresh, whether the boundary renders on its own or in its parent's walk,
    /// and the render after that keeps it. Where that render unmounts a fallback whose cleanup throws, one more such
    /// render mounts a single fallback for that error, and where that render ends discarded, the fallback it unmounted
    /// leaves no props bag rented out. An error the fallback's own update throws after that render is the boundary's
    /// to catch.</item>
    /// <item>Where a transition gives up a walk that caught in the boundary because the fallback's content suspended,
    /// the boundary still records the Suspense it showed as showing its fallback, and once the reads resolve, the
    /// retry shows what the fallback read and cleans up the child the boundary showed once, the reader that replaces
    /// that child included.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ErrorBoundaryFallbackRemountTests
    {
        private static StateUpdater<int> s_setCount;
        private static StateUpdater<int> s_setTick;
        private static int s_rowDetaches;
        private static StateUpdater<int> s_setPage;
        private static Func<int, VNode> s_pagedContent;
        private static int s_catches;
        private static VelvetTaskCompletionSource<int> s_never;

        private static readonly Func<VisualElement, Action> s_rowRef = _ => () => s_rowDetaches++;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setCount = default;
            s_setTick = default;
            s_rowDetaches = 0;
            s_setPage = default;
            s_pagedContent = null;
            s_catches = 0;
            s_never = new VelvetTaskCompletionSource<int>();
            s_setThrowerTick = default;
            s_setProbe = default;
            s_setBoundaryRender = default;
            s_onCleanup = null;
            s_probeCleanupThrows = 0;
            s_start = default;
            s_loads = new List<VelvetTaskCompletionSource<int>>();
            s_counterCleanups = 0;
            s_loadingBoundary = null;
        }

        [Component(Compiler = false)]
        private static VNode Counter()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            return V.Label(name: "count", text: "count:" + count);
        }

        [Component(Compiler = false)]
        private static VNode LayoutThrower(int tick)
        {
            Hooks.UseLayoutEffect(
                (Func<Action>)(() => tick > 0 ? throw new InvalidOperationException("layout throw") : (Action)null),
                new object[] { tick });
            return V.Label(text: "layout-thrower");
        }

        [Component(Compiler = false)]
        private static VNode RenderThrower(int tick)
            => tick > 0 ? throw new InvalidOperationException("render throw") : V.Label(text: "render-thrower");

        // The fallback is the first child the boundary shows, at its position and with its key.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode CounterLayoutBoundary()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseFallback(_ => V.Component(Counter, key: "counter"));
            return V.Fragment(new VNode[]
            {
                V.Component(Counter, key: "counter"),
                V.Component(LayoutThrower, tick, key: "thrower"),
            });
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode CounterRenderBoundary()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseFallback(_ => V.Component(Counter, key: "counter"));
            return V.Fragment(new VNode[]
            {
                V.Component(Counter, key: "counter"),
                V.Component(RenderThrower, tick, key: "thrower"),
            });
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode RowLayoutBoundary()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseFallback(_ => V.Label(key: "row", name: "row", text: "row", refCallback: s_rowRef));
            return V.Fragment(new VNode[]
            {
                V.Label(key: "row", name: "row", text: "row", refCallback: s_rowRef),
                V.Component(LayoutThrower, tick, key: "thrower"),
            });
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode PooledLayoutBoundary()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(new VNode[]
            {
                PooledRow(),
                V.Component(LayoutThrower, tick, key: "thrower"),
            });
        }

        private static VNode PooledRow()
            => V.Div(children: V.List(new[] { "open" }, id => id, id => V.Button(text: id, onClick: () => { })));

        private MountedTree MountWithCount(Func<VNode> boundary)
        {
            var mounted = V.Mount(_root, V.Component(boundary, key: "boundary"), CaughtErrors.Unlogged);
            s_setCount.Invoke(5);
            mounted.FlushStateForTest();
            return mounted;
        }

        [Test]
        public void Given_AFallbackRepeatingAStatefulChildOfTheBoundary_When_ALayoutEffectOfTheBoundarysUpdateThrows_Then_TheFallbacksChildStartsFromItsInitialState()
        {
            // Arrange
            using var mounted = MountWithCount(CounterLayoutBoundary);

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — a boundary that did not catch shows the child it had, which reads count:5
            Assert.That(_root.Q<Label>("count")?.text, Is.EqualTo("count:0"));
        }

        [Test]
        public void Given_AFallbackRepeatingAStatefulChildOfTheBoundary_When_TheRenderOfTheBoundarysOwnUpdateThrows_Then_TheFallbacksChildStartsFromItsInitialState()
        {
            // Arrange
            using var mounted = MountWithCount(CounterRenderBoundary);

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — a boundary that did not catch shows the child it had, which reads count:5
            Assert.That(_root.Q<Label>("count")?.text, Is.EqualTo("count:0"));
        }

        [Test]
        public void Given_AFallbackRepeatingARowOfTheBoundary_When_ALayoutEffectOfTheBoundarysUpdateThrows_Then_TheRowTheBoundaryShowedLetsGoOfItsRef()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RowLayoutBoundary, key: "boundary"), CaughtErrors.Unlogged);

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — a boundary that did not catch keeps the row it had, which lets go of nothing
            Assert.That(s_rowDetaches, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base retires what the boundary showed once the fallback has mounted.
        // This pins that retiring it ahead of the fallback's own reconcile still returns its pooled parts.
        [Test]
        public void Given_AShownOutputHoldingPooledNodes_When_ALayoutEffectOfTheBoundarysUpdateThrowsAndTheMountIsDisposed_Then_ThePoolCountsNoPropsRentedOut()
        {
            // Arrange
            var before = VNodePoolTestAccess.RentedOutCountsForTest().Props;
            var mounted = V.Mount(_root, V.Component(PooledLayoutBoundary, key: "boundary"), CaughtErrors.Unlogged);
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(VNodePoolTestAccess.RentedOutCountsForTest().Props - before, Is.EqualTo(0));
        }

        #region A swap a Suspense's hide runs

        // Reads page 0 at once, and waits for good on any later page.
        [Component(Compiler = false)]
        private static VNode PagedRead(int page)
        {
            var read = Hooks.Use(() => page == 0 ? VelvetTask.FromResult(0) : s_never.Task, page);
            return V.Label(text: "read:" + read);
        }

        [Component(Compiler = false)]
        private static VNode HideCleanupThrower()
        {
            Hooks.UseLayoutEffect(
                (Func<Action>)(() => () => throw new InvalidOperationException("hide cleanup throw")),
                Array.Empty<object>());
            return V.Label(text: "hide-cleanup-thrower");
        }

        private static VNode[] WaitingChildren() => new VNode[]
        {
            V.Label(name: "waiting", text: "waiting"),
            V.Component(PagedRead, 1, key: "wait"),
        };

        // Its fallback repeats the Suspense it renders, at the same position, with children that wait for good.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode HidingSuspenseBoundary(int page)
        {
            Hooks.UseFallback(_ => V.Suspense(fallback: V.Div(name: "loading"), children: WaitingChildren()));
            return V.Suspense(fallback: V.Div(name: "loading"), children: new VNode[]
            {
                V.Component(HideCleanupThrower, key: "thrower"),
                V.Component(PagedRead, page, key: "read"),
            });
        }

        // Shows its Suspense's children; its fallback repeats that Suspense, at the same position, with children that
        // wait for good.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode ShowingSuspenseBoundary()
        {
            Hooks.UseFallback(_ => V.Suspense(fallback: V.Div(name: "inner-loading"), children: WaitingChildren()));
            return V.Suspense(
                fallback: V.Div(name: "inner-loading"),
                children: new VNode[] { V.Component(HideCleanupThrower, key: "thrower") });
        }

        private static VNode Presence() => V.AnimatePresence(children: new VNode[] { V.Label(key: "a", text: "a") });

        // Its fallback repeats the Suspense it renders, at the same position, holding the same AnimatePresence.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode PresenceSuspenseBoundary(int page)
        {
            Hooks.UseFallback(_ => V.Suspense(fallback: V.Div(name: "loading"), children: new VNode[] { Presence() }));
            return V.Suspense(fallback: V.Div(name: "loading"), children: new VNode[]
            {
                Presence(),
                V.Component(HideCleanupThrower, key: "thrower"),
                V.Component(PagedRead, page, key: "read"),
            });
        }

        [Component(Compiler = false)]
        private static VNode PagedHost()
        {
            var (page, setPage) = Hooks.UseState(0);
            s_setPage = setPage;
            return s_pagedContent(page);
        }

        private MountedTree MountPaged(Func<int, VNode> content)
        {
            s_pagedContent = content;
            var mounted = V.Mount(_root, V.Component(PagedHost, key: "host"),
                new MountOptions { OnCaughtError = (_, _) => s_catches++ });
            mounted.DrainForTest();
            return mounted;
        }

        private static void TurnPage(MountedTree mounted, int page = 1)
        {
            s_setPage.Invoke(page);
            mounted.DrainForTest();
        }

        [Test]
        public void Given_ABoundaryWhoseFallbackIsTheSuspenseItShowsWithChildrenThatWait_When_ALayoutCleanupOfThatSuspensesHideThrows_Then_NoneOfThoseChildrenIsKept()
        {
            // Arrange
            using var mounted = MountPaged(page => V.Component(HidingSuspenseBoundary, page, key: "boundary"));

            // Act
            TurnPage(mounted);

            // Assert — a boundary that did not catch once reads as a Label kept
            Assert.That(s_catches == 1 ? _root.Q<Label>("waiting") : new Label(), Is.Null);
        }

        [Test]
        public void Given_ABoundaryShowingItsSuspensesChildrenWhoseFallbackIsThatSuspenseWithChildrenThatWait_When_ALayoutCleanupOfAnEnclosingSuspensesHideThrows_Then_NoneOfThoseChildrenIsKept()
        {
            // Arrange
            using var mounted = MountPaged(page => V.Suspense(
                fallback: V.Label(text: "outer-loading"),
                children: new VNode[]
                {
                    V.Component(ShowingSuspenseBoundary, key: "boundary"),
                    V.Component(PagedRead, page, key: "read"),
                }));

            // Act
            TurnPage(mounted);

            // Assert — a boundary that did not catch once reads as a Label kept
            Assert.That(s_catches == 1 ? _root.Q<Label>("waiting") : new Label(), Is.Null);
        }

        [Test]
        public void Given_ABoundaryWhoseFallbackRepeatsItsSuspenseAndPresence_When_ALayoutCleanupOfThatSuspensesHideThrows_Then_ThePresenceStartsWithStateOfItsOwn()
        {
            // Arrange
            using var mounted = MountPaged(page => V.Component(PresenceSuspenseBoundary, page, key: "boundary"));
            var presences = mounted.Root.Reconciler.Context.PresenceStates;
            var shown = presences.Values.Single();

            // Act
            TurnPage(mounted);

            // Assert — a boundary that did not catch, or a fallback that kept no presence, reads as the state shown
            Assert.That(presences.Values.SingleOrDefault() ?? shown, Is.Not.SameAs(shown));
        }

        #endregion

        #region A catch in the walk expanding the boundary

        // The fallback is the first child the boundary shows, at its position and with its key; the update the host
        // gives the boundary throws in the walk expanding it.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode InWalkCounterBoundary(int page)
        {
            Hooks.UseFallback(_ => V.Component(Counter, key: "counter"));
            return V.Fragment(new VNode[]
            {
                V.Component(Counter, key: "counter"),
                V.Component(RenderThrower, page, key: "thrower"),
            });
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode MountThrowingBoundary()
        {
            Hooks.UseFallback(_ => V.Label(text: "inner-fallback"));
            return V.Component(RenderThrower, 1, key: "thrower");
        }

        // The fallback mounts a boundary that catches in the same walk ahead of the child it repeats, both in a
        // fragment at the position of the one the boundary shows.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode InWalkNestedBoundary(int page)
        {
            Hooks.UseFallback(_ => V.Fragment(new VNode[]
            {
                V.Component(MountThrowingBoundary, key: "inner"),
                V.Component(Counter, key: "counter"),
            }));
            return V.Fragment(new VNode[]
            {
                V.Fragment(new VNode[] { V.Component(Counter, key: "counter") }),
                V.Component(RenderThrower, page, key: "thrower"),
            });
        }

        // Shows its Suspense's children; its update throws ahead of that Suspense, and its fallback repeats the Suspense
        // by key, with children that wait for good.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode InWalkSuspenseBoundary(int page)
        {
            Hooks.UseFallback(_ => V.Suspense(V.Div(name: "inner-loading"), WaitingChildren(), key: "suspense"));
            return V.Fragment(new VNode[]
            {
                V.Component(RenderThrower, page, key: "thrower"),
                V.Suspense(V.Div(name: "inner-loading"), new VNode[] { V.Label(text: "row") }, key: "suspense"),
            });
        }

        private MountedTree MountCounted(Func<int, VNode> content)
        {
            var mounted = MountPaged(content);
            s_setCount.Invoke(5);
            mounted.DrainForTest();
            return mounted;
        }

        private string CountTexts() => string.Join(",", _root.Query<Label>("count").ToList().Select(label => label.text));

        [Test]
        public void Given_AFallbackRepeatingAStatefulChildOfTheBoundary_When_TheBoundaryCatchesInTheWalkOfItsParentsUpdate_Then_TheFallbacksChildStartsFromItsInitialState()
        {
            // Arrange
            using var mounted = MountCounted(page => V.Component(InWalkCounterBoundary, page, key: "boundary"));

            // Act
            TurnPage(mounted);

            // Assert — a boundary that did not catch shows the child it had, which reads count:5
            Assert.That(CountTexts(), Is.EqualTo("count:0"));
        }

        // GREEN_ON_BASE(characterization): the base hands the fallback the child the boundary showed, whose registration
        // goes on. This pins that the child mounted in its place stays registered once the one it replaces is disposed.
        [Test]
        public void Given_AFallbackChildMountedInTheWalkInPlaceOfOneTheBoundaryShowed_When_TheParentUpdatesAgain_Then_ThatChildKeepsItsState()
        {
            // Arrange
            using var mounted = MountCounted(page => V.Component(InWalkCounterBoundary, page, key: "boundary"));
            TurnPage(mounted);
            s_setCount.Invoke(3);
            mounted.DrainForTest();

            // Act
            TurnPage(mounted, 2);

            // Assert — a child mounted again reads count:0, and one left on screen beside it reads twice
            Assert.That(CountTexts(), Is.EqualTo("count:3"));
        }

        [Test]
        public void Given_AFallbackWhoseOwnBoundaryCatchesAheadOfAChildItRepeats_When_TheOuterBoundaryCatchesInTheWalk_Then_TheRepeatedChildStartsFromItsInitialState()
        {
            // Arrange
            using var mounted = MountCounted(page => V.Component(InWalkNestedBoundary, page, key: "boundary"));

            // Act
            TurnPage(mounted);

            // Assert — a boundary that did not catch shows the child it had, which reads count:5
            Assert.That(CountTexts(), Is.EqualTo("count:0"));
        }

        [Test]
        public void Given_ABoundaryShowingItsSuspensesChildrenWhoseFallbackRepeatsThatSuspenseWithChildrenThatWait_When_TheBoundaryCatchesInTheWalk_Then_NoneOfThoseChildrenIsKept()
        {
            // Arrange
            using var mounted = MountPaged(page => V.Component(InWalkSuspenseBoundary, page, key: "boundary"));

            // Act
            TurnPage(mounted);

            // Assert — a boundary that did not catch once reads as a Label kept
            Assert.That(s_catches == 1 ? _root.Q<Label>("waiting") : new Label(), Is.Null);
        }

        #endregion

        #region A catch of what the swap itself throws

        private static StateUpdater<int> s_setThrowerTick;
        private static StateUpdater<string> s_setProbe;
        private static StateUpdater<int> s_setBoundaryRender;
        private static Action s_onCleanup;

        [Component(Compiler = false)]
        private static VNode ErrorProbe(string error)
        {
            var (first, setFirst) = Hooks.UseState(error);
            s_setProbe = setFirst;
            if (first == "throw") throw new InvalidOperationException("probe render throw");
            return V.Label(name: "probe", text: first);
        }

        [Component(Compiler = false)]
        private static VNode CleanupThrowingChild()
        {
            Hooks.UseEffect((Func<Action>)(() => () =>
            {
                s_onCleanup?.Invoke();
                throw new InvalidOperationException("swap cleanup throw");
            }), Array.Empty<object>());
            return V.Label(text: "cleanup-child");
        }

        [Component(Compiler = false)]
        private static VNode OwnTickThrower()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setThrowerTick = setTick;
            return tick > 0 ? throw new InvalidOperationException("own render throw") : V.Label(text: "own-tick");
        }

        // Swapping its fallback in, for what the thrower's update throws, unmounts a child whose passive cleanup throws,
        // and its fallback starts from the error it is handed.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode SwapErrorBoundary()
        {
            var (_, setRender) = Hooks.UseState(0);
            s_setBoundaryRender = setRender;
            Hooks.UseFallback(exception => V.Component(ErrorProbe, exception.Message, key: "probe"));
            return V.Fragment(new VNode[]
            {
                V.Component(CleanupThrowingChild, key: "child"),
                V.Component(OwnTickThrower, key: "thrower"),
            });
        }

        private MountedTree MountSwapError()
        {
            var mounted = V.Mount(_root, V.Component(SwapErrorBoundary, key: "boundary"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            return mounted;
        }

        private static void ThrowFromTheThrower(MountedTree mounted)
        {
            s_setThrowerTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();
        }

        [Test]
        public void Given_ABoundaryWhoseSwapUnmountsAChildWhoseCleanupThrows_When_TheBoundaryRendersAgainForThatError_Then_ItsFallbackStartsFromThatError()
        {
            // Arrange
            using var mounted = MountSwapError();

            // Act
            ThrowFromTheThrower(mounted);

            // Assert — a fallback kept from the first swap reads the error it started from, the thrower's
            Assert.That(_root.Q<Label>("probe")?.text, Is.EqualTo("swap cleanup throw"));
        }

        [Test]
        public void Given_ABoundaryThatRenderedAgainForWhatItsSwapThrew_When_ItsFallbacksOwnUpdateThrows_Then_ItMountsAFallbackForThatError()
        {
            // Arrange
            using var mounted = MountSwapError();
            ThrowFromTheThrower(mounted);

            // Act
            s_setProbe.Invoke("throw");
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();

            // Assert — a boundary that passes the error on leaves no probe, and a fallback kept in place throws again
            Assert.That(_root.Q<Label>("probe")?.text, Is.EqualTo("probe render throw"));
        }

        // GREEN_ON_BASE(characterization): the base keeps the fallback across the boundary's renders.
        // This pins that the boundary's next render after the one for the error its swap threw keeps the fallback.
        [Test]
        public void Given_ABoundaryThatRenderedAgainForWhatItsSwapThrew_When_ItRendersOnceMore_Then_ItsFallbackKeepsItsState()
        {
            // Arrange
            using var mounted = MountSwapError();
            ThrowFromTheThrower(mounted);
            s_setProbe.Invoke("changed");
            mounted.FlushStateForTest();

            // Act
            s_setBoundaryRender.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("probe")?.text, Is.EqualTo("changed"));
        }

        [Test]
        public void Given_ABoundaryWhoseSwapUnmountsAChildWhoseCleanupUpdatesTheParent_When_TheParentsWalkRendersTheBoundaryForThatError_Then_ItsFallbackStartsFromThatError()
        {
            // Arrange — the parent's update and the boundary's render for the cleanup's error come in one pass
            using var mounted = MountPaged(_ => V.Component(SwapErrorBoundary, key: "boundary"));
            s_onCleanup = () => s_setPage.Invoke(1);

            // Act
            s_setThrowerTick.Invoke(1);
            mounted.DrainForTest();

            // Assert — a fallback kept from the first swap reads the error it started from, the thrower's
            Assert.That(_root.Q<Label>("probe")?.text, Is.EqualTo("swap cleanup throw"));
        }

        private static int s_probeCleanupThrows;

        [Component(Compiler = false)]
        private static VNode CleanupThrowingProbe(string error)
        {
            Hooks.UseEffect((Func<Action>)(() => () =>
            {
                if (s_probeCleanupThrows-- > 0) throw new InvalidOperationException("probe cleanup throw");
            }), Array.Empty<object>());
            return V.Label(name: "probe", text: error);
        }

        // As SwapErrorBoundary, with a fallback whose passive cleanup throws the first time it runs.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode CleanupThrowingFallbackBoundary()
        {
            Hooks.UseFallback(exception => V.Component(CleanupThrowingProbe, exception.Message, key: "probe"));
            return V.Fragment(new VNode[]
            {
                V.Component(CleanupThrowingChild, key: "child"),
                V.Component(OwnTickThrower, key: "thrower"),
            });
        }

        // As SwapErrorBoundary, with pooled nodes in the fallback for the thrower's error, and a fallback for the
        // error its swap throws whose content throws as it renders.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode PooledSwapBoundary()
        {
            Hooks.UseFallback(exception => exception.Message == "swap cleanup throw"
                ? V.Component(RenderThrower, 1, key: "probe")
                : PooledRow());
            return V.Fragment(new VNode[]
            {
                V.Component(CleanupThrowingChild, key: "child"),
                V.Component(OwnTickThrower, key: "thrower"),
            });
        }

        [Test]
        public void Given_AFallbackWhoseCleanupThrows_When_TheRenderForWhatTheSwapThrewUnmountsIt_Then_OneFallbackStartsFromThatCleanupsError()
        {
            // Arrange
            s_probeCleanupThrows = 1;
            using var mounted = V.Mount(_root, V.Component(CleanupThrowingFallbackBoundary, key: "boundary"),
                CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();

            // Act — a flush for each render the errors ask for, and one to spare
            s_setThrowerTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();

            // Assert — a fallback swapped in under that render's own unmount reads beside the one the render mounts
            var probes = _root.Query<Label>("probe").ToList().Select(label => label.text);
            Assert.That(string.Join(",", probes), Is.EqualTo("probe cleanup throw"));
        }

        // GREEN_ON_BASE(characterization): the base retires what the boundary showed when its fallback swaps in.
        // This pins that a render for what the swap threw, which ends discarded, still returns the pooled parts of the
        // fallback it unmounted.
        [Test]
        public void Given_AFallbackHoldingPooledNodes_When_TheRenderForWhatTheSwapThrewEndsDiscardedAndTheMountIsDisposed_Then_ThePoolCountsNoPropsRentedOut()
        {
            // Arrange
            var before = VNodePoolTestAccess.RentedOutCountsForTest().Props;
            var mounted = V.Mount(_root, V.Component(PooledSwapBoundary, key: "boundary"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            s_setThrowerTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(VNodePoolTestAccess.RentedOutCountsForTest().Props - before, Is.EqualTo(0));
        }

        #endregion

        #region A catch in a walk a transition gives up

        private static TransitionStarter s_start;
        private static List<VelvetTaskCompletionSource<int>> s_loads;
        private static int s_counterCleanups;
        private static ComponentFiber s_loadingBoundary;

        // Reads page 0 at once, and starts a load the case resolves for any later page.
        private static int ReadPage(int page) => Hooks.Use<int>(_ =>
        {
            if (page == 0) return VelvetTask.FromResult(0);
            var source = new VelvetTaskCompletionSource<int>();
            s_loads.Add(source);
            return source.Task;
        }, page);

        [Component(Compiler = false)]
        private static VNode CountingReader(int page)
        {
            Hooks.UseEffect((Func<Action>)(() => () => s_counterCleanups++), Array.Empty<object>());
            return V.Label(name: "counted", text: "counted:" + ReadPage(page));
        }

        [Component(Compiler = false)]
        private static VNode PageLoader(int page) => V.Label(name: "loaded", text: "loaded:" + ReadPage(page));

        [Component(Compiler = false)]
        private static VNode NeverRead() => V.Label(text: "read:" + Hooks.Use(() => s_never.Task, "never"));

        // The child the fallback repeats sits in a fragment at the fallback's own position, the boundary shows a
        // Suspense of its own showing its fallback, and the fallback, that child included, reads a page that waits until
        // the case resolves it.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode LoadingFallbackBoundary(int page)
        {
            s_loadingBoundary = FiberAmbientStack.Current;
            Hooks.UseFallback(_ => V.Fragment(new VNode[]
            {
                V.Component(CountingReader, page, key: "counter"),
                V.Component(PageLoader, page, key: "loader"),
            }));
            return V.Fragment(new VNode[]
            {
                V.Fragment(new VNode[] { V.Component(CountingReader, 0, key: "counter") }),
                V.Suspense(V.Label(text: "own-loading"), new VNode[] { V.Component(NeverRead, key: "never") }, key: "own"),
                V.Component(RenderThrower, page, key: "thrower"),
            });
        }

        // The page turns in a transition, under a Suspense showing its children, which gives the transition's walk up
        // where the boundary's fallback suspends.
        [Component(Compiler = false)]
        private static VNode TransitionHost()
        {
            var (page, setPage) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_setPage = setPage;
            s_start = start;
            return V.Suspense(V.Label(text: "loading"), new VNode[]
            {
                V.Component(LoadingFallbackBoundary, page, key: "boundary"),
            });
        }

        private MountedTree MountTransitionHost()
        {
            var mounted = V.Mount(_root, V.Component(TransitionHost, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
            return mounted;
        }

        private static void TurnPageInATransition(MountedTree mounted)
        {
            s_start.Invoke(() => s_setPage.Invoke(1));
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();
        }

        // The retry reaches each read once the one ahead of it has resolved. Bounded, since a retry that mounts a reader
        // again starts a load every time.
        private static void ResolveTheLoads(MountedTree mounted)
        {
            for (var resolved = 0; resolved < s_loads.Count && resolved < 4; resolved++)
            {
                s_loads[resolved].TrySetResult(7);
                mounted.FlushStateForTest();
                mounted.FlushStateForTest();
            }
            mounted.FlushEffectsForTest();
        }

        [Test]
        public void Given_ACatchInAWalkATransitionGaveUpWhereTheFallbackSuspended_When_ItsReadsResolve_Then_TheChildTheBoundaryShowedIsCleanedUpOnce()
        {
            // Arrange
            using var mounted = MountTransitionHost();
            TurnPageInATransition(mounted);

            // Act
            ResolveTheLoads(mounted);

            // Assert — a child the fallback took over is never cleaned up, one no key reaches any more stays too, and a
            // retry that mounts the fallback's own reader again starts its load over and never settles
            Assert.That(s_counterCleanups, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base hands the retry the reader the walk it gave up mounted, by its key.
        // This pins that mounting the fallback afresh leaves the reader no shown child held the key of to the retry.
        [Test]
        public void Given_ACatchInAWalkATransitionGaveUpWhereTheFallbackSuspended_When_ItsReadsResolve_Then_TheFallbackShowsWhatItRead()
        {
            // Arrange
            using var mounted = MountTransitionHost();
            TurnPageInATransition(mounted);

            // Act
            ResolveTheLoads(mounted);

            // Assert — a reader mounted again starts its load over and shows nothing yet
            Assert.That(_root.Q<Label>("loaded")?.text, Is.EqualTo("loaded:7"));
        }

        [Test]
        public void Given_ABoundaryShowingItsOwnSuspensesFallback_When_ATransitionGivesUpTheWalkThatCaughtInIt_Then_TheBoundaryStillRecordsThatFallbackAsShown()
        {
            // Arrange
            using var mounted = MountTransitionHost();

            // Act
            TurnPageInATransition(mounted);

            // Assert — a catch the walk never reached reads the same, so the one load the fallback's first read started is
            // read beside it
            Assert.That(
                s_loads.Count == 1 && mounted.Root.Reconciler.Context.IsBoundaryShowingFallback(s_loadingBoundary),
                Is.True);
        }

        #endregion
    }
}
