using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that an error boundary mounts its fallback afresh, as React's boundary reconciles it against none of the
    /// children it replaces, where the boundary catches outside a walk expanding it: an error a layout effect of the
    /// boundary's update throws, and one the render of the boundary's own update throws.
    /// <list type="bullet">
    /// <item>A component of the fallback with the type and key of one the boundary showed starts from its initial
    /// state, after either error.</item>
    /// <item>A row the boundary showed lets go of its ref where the fallback has a row with the same key.</item>
    /// <item>Once the mount is disposed, no props bag of what the boundary showed is left rented out.</item>
    /// <item>Where a layout cleanup of a Suspense's hide throws inside the pass that hides, a fallback repeating the
    /// Suspense the boundary showed takes nothing that Suspense recorded: it keeps none of the children it never
    /// committed, whether the shown Suspense was hiding its children or showing them, and an AnimatePresence it
    /// repeats starts with state of its own.</item>
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
                V.Div(children: V.List(new[] { "open" }, id => id, id => V.Button(text: id, onClick: () => { }))),
                V.Component(LayoutThrower, tick, key: "thrower"),
            });
        }

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

        private static void TurnPage(MountedTree mounted)
        {
            s_setPage.Invoke(1);
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
    }
}
