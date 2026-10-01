using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a suspend does where no Suspense expansion is on the stack to catch it: in the render a
    /// component's own flush starts.
    /// <list type="bullet">
    /// <item>With no boundary above, the flush does not throw. A resource of the component whose read suspended
    /// the pass resolving retries that pass, an ancestor's included, and every pass on the way that suspended
    /// on that read, a memoized one included; a pass that suspended on another component's read is left alone.
    /// The resolve commits nothing ahead of that retry, and a resolve no suspended pass waits on renders
    /// nothing: the pass retried later reads the value without starting the read again. A pass that suspended
    /// commits none of the props it passed, so a later pass renders the component that suspended again, one the
    /// suspended pass first mounted included. A component whose own render suspended before it
    /// reconciled anything shows what it showed before, and a transition whose render suspended stays
    /// pending.</item>
    /// <item>With a boundary above, that boundary renders again and shows its fallback, a boundary the compiler
    /// memoized included, and the component whose update suspended renders that update inside it, a memoized
    /// one included. The boundary is the nearest above the component whose read suspended, so one that renders
    /// its own Suspense reveals through the boundary above it.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class SuspendWithoutBoundaryTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_source = null;
            s_setOwn = default;
            s_parentSetKey = default;
            s_innerFiber = null;
            s_outerFiber = null;
            s_innerSetOwn = default;
            s_outerSetTick = default;
            s_innerMemoized = false;
            s_parentFiber = null;
            s_pageSetId = default;
            s_otherSource = null;
            s_readerSetOwn = default;
            s_readerFiber = null;
            s_twoReadersRenders = 0;
            s_indicatorFiber = null;
            s_indicatorStart = default;
            s_selfSetOwn = default;
            s_tickedSetKey = default;
            s_tickedSetTick = default;
            s_showSetShow = default;
            s_pairSetA = default;
            s_pairSetB = default;
            s_sourceA = null;
            s_sourceB = null;
        }

        [Test]
        public void Given_AComponentRenderingItsOwnSuspenseWhoseUpdateSuspendsUnderAnOuterBoundary_When_TheResourceResolves_Then_TheOuterBoundaryRevealsIt()
        {
            // Arrange — the component's own update suspends, and the boundary above it shows its fallback
            using var mounted = V.Mount(_root, V.Component(OuterSuspenseHostRender, key: "outer-host"));
            s_selfSetOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            s_source.TrySetResult(5);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("self:5"),
                "React takes the nearest Suspense above the component that suspended, not the one it renders");
        }

        [Test]
        public void Given_AParentUpdateThatSuspendedItsMemoizedChildWithNoBoundary_When_AnotherParentUpdateRendersFirst_Then_NothingCommits()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(TickedHostRender, key: "ticked-host"));
            s_tickedSetKey.Invoke(1);
            mounted.FlushStateForTest();
            var whileSuspended = Texts();

            // Act
            s_tickedSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo(whileSuspended),
                "The later render passes the child its new props again and suspends again, as React keeps the old UI");
        }

        [Test]
        public void Given_AMemoizedComponentWhoseFirstMountSuspendsWithNoBoundary_When_TheResourceResolves_Then_ItRendersTheValue()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(ShowParentRender, key: "show-parent"));
            s_showSetShow.Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            s_source.TrySetResult(5);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("reader:5|shown:True"),
                "The retried pass renders the component its suspended pass mounted, whose props it never committed");
        }

        [Test]
        public void Given_AReadNoSuspendedPassWaitsOn_When_ItsResourceResolves_Then_ThePassRetriedLaterReadsItsValueWithoutReadingAgain()
        {
            // Arrange — the first update suspends on A's read; the second suspends on B's, ahead of A's
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(PairParentRender, key: "pair-parent"));
            s_pairSetA.Invoke(1);
            mounted.FlushStateForTest();
            s_pairSetB.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_sourceA.TrySetResult(5);
            s_sourceB.TrySetResult(7);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("b:7|a:5"),
                "A's resolve renders nothing, and the retry of the pass that suspended on B reads A's value");
        }

        [Test]
        public void Given_AComponentWhoseOwnUpdateSuspendsWithNoBoundary_When_TheResourceResolves_Then_ItRendersTheResolvedValue()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(PlainHostRender, key: "host"));
            s_setOwn.Invoke(1);
            mounted.FlushStateForTest();
            var whileSuspended = Texts();

            // Act
            s_source.TrySetResult(5);
            mounted.FlushStateForTest();

            // Assert — the suspended reading is folded in, since output that came back only after the resolve
            // would read the same at the end
            Assert.That((whileSuspended, Texts()), Is.EqualTo(("host|child:0", "host|child:5")),
                "The component keeps its previous output while suspended and renders the value once the resource resolves");
        }

        [Test]
        public void Given_AParentUpdateThatSuspendsItsChildWithNoBoundary_When_TheResourceResolves_Then_TheParentsPassIsRetried()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(KeyedParentRender, key: "parent"));
            s_parentSetKey.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_source.TrySetResult(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("child:5|parent:1"),
                "The resolve retries the parent's pass, which commits the parent's own output with the child's value");
        }

        [Test]
        public void Given_AParentUpdateThatSuspendsItsChildWithNoBoundary_When_TheResourceResolves_Then_NothingCommitsAheadOfTheParentsRetry()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(KeyedParentRender, key: "parent"));
            s_parentSetKey.Invoke(1);
            mounted.FlushStateForTest();
            var whileSuspended = Texts();

            // Act
            s_source.TrySetResult(5);

            // Assert
            Assert.That(Texts(), Is.EqualTo(whileSuspended),
                "The resolve asks for the parent's pass instead of committing the child's value beside the parent's old output");
        }

        [Test]
        public void Given_AParentPassRetriedAfterItsChildSuspended_When_TheRetryHasRendered_Then_TheParentIsNoLongerMarkedSuspended()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(KeyedParentRender, key: "parent"));
            s_parentSetKey.Invoke(1);
            mounted.FlushStateForTest();
            var markedWhileSuspended = MarkedSuspended(s_parentFiber);

            // Act
            s_source.TrySetResult(5);
            mounted.FlushStateForTest();

            // Assert — the retried output is folded in, since a mark that never went up reads false as well
            Assert.That((markedWhileSuspended, Texts(), MarkedSuspended(s_parentFiber)),
                Is.EqualTo((true, "child:5|parent:1", false)),
                "The retry takes down the mark that routed the resolve to the parent's pass");
        }

        [Test]
        public void Given_AChildPassAndThenItsParentsPassSuspendedOnOneResource_When_TheResourceResolves_Then_TheParentsPassIsRetried()
        {
            // Arrange — the child's own update suspends first, then the parent's pass reaches the same child and
            // suspends too, so both passes are marked; the parent's label sits after the child
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(OuterPassRender, key: "outer"));
            s_innerSetOwn.Invoke(1);
            FiberWorkLoop.FlushState(s_innerFiber);
            s_outerSetTick.Invoke(1);
            FiberWorkLoop.FlushState(s_outerFiber);

            // Act
            s_source.TrySetResult(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("child:5|outer:1"),
                "The resolve retries the outermost pass that suspended, which renders the inner one's too");
        }

        [Test]
        public void Given_AMemoizedChildPassAndThenItsParentsPassSuspendedOnOneResource_When_TheResourceResolves_Then_BothPassesAreRetried()
        {
            // Arrange — as above, with the inner component memoized on the outer tick, so the outer retry alone
            // would bail on it
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            s_innerMemoized = true;
            using var mounted = V.Mount(_root, V.Component(OuterPassRender, key: "outer"));
            s_innerSetOwn.Invoke(1);
            FiberWorkLoop.FlushState(s_innerFiber);
            s_outerSetTick.Invoke(1);
            FiberWorkLoop.FlushState(s_outerFiber);

            // Act
            s_source.TrySetResult(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("child:5|outer:1"),
                "Every pass that suspended on the resource is retried, a memoized inner one included");
        }

        [Test]
        public void Given_AResolvedBoundaryWhoseChildsOwnUpdateSuspends_When_TheImmediateTierDrains_Then_TheBoundaryShowsItsFallback()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(BoundaryHostRender, key: "boundary-host"));
            var beforeTheUpdate = Texts();
            s_setOwn.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That((beforeTheUpdate, Texts()), Is.EqualTo(("host|child:0", "host|loading")),
                "The boundary above a component whose own update suspends renders again and shows its fallback");
        }

        [Test]
        public void Given_ACompilerMemoizedBoundaryWhoseChildsOwnUpdateSuspends_When_TheImmediateTierDrains_Then_TheBoundaryShowsItsFallback()
        {
            // Arrange — the boundary's prop is unchanged, so its memo cache still holds the tree it showed
            using var mounted = V.Mount(_root, V.Component(PropBoundaryHostRender, 0, key: "prop-boundary-host"));
            s_setOwn.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("host|loading"),
                "A boundary whose inputs are unchanged still renders again and shows its fallback");
        }

        [Test]
        public void Given_ATransitionWhoseUpdateSuspendsWithNoBoundary_When_ItsLaneHasDrained_Then_ItIsStillPending()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(IndicatorHostRender, key: "indicator-host"));
            s_indicatorStart.Invoke(() => s_setOwn.Invoke(1));

            // Act — the flush renders the transition's update, on the lane it was queued on
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_indicatorFiber.IsTransitionPending, Is.True,
                "A transition whose render suspended has not committed, so React keeps isPending lit");
        }

        [Test]
        public void Given_AMemoizedComponentUnderABoundaryWhoseOwnUpdateSuspends_When_TheResourceResolves_Then_ItShowsTheUpdateWithTheValue()
        {
            // Arrange — the boundary's walk reaches the memoized page with equal props, so only a page left dirty
            // renders its update there
            using var mounted = V.Mount(_root, V.Component(PageBoundaryHostRender, key: "page-host"));
            s_pageSetId.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            s_source.TrySetResult(5);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("page:1|child:5"),
                "The boundary's render renders the page's update, whose read then resolves");
        }

        [Test]
        public void Given_APassSuspendedOnOneReadAndAnotherComponentsOwnPassSuspended_When_TheOtherResourceResolves_Then_TheFirstPassIsNotRetried()
        {
            // Arrange — the reader's own pass suspends first, then the outer pass suspends on the other read ahead
            // of it
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(TwoReadersRender, key: "two-readers"));
            s_readerSetOwn.Invoke(1);
            FiberWorkLoop.FlushState(s_readerFiber);
            s_outerSetTick.Invoke(1);
            FiberWorkLoop.FlushState(s_outerFiber);
            var rendersBefore = s_twoReadersRenders;

            // Act
            s_otherSource.TrySetResult(7);
            mounted.FlushStateForTest();

            // Assert — the reader's retry is folded in, since a resolve that retried nothing leaves the count too
            Assert.That((s_twoReadersRenders - rendersBefore, Texts()), Is.EqualTo((0, "child:0|reader:7|outer:0")),
                "A resolve retries the passes that suspended on the resolving component's read, not every one above it");
        }

        private static VelvetTaskCompletionSource<int> s_source;
        private static StateUpdater<int> s_setOwn;
        private static StateUpdater<int> s_parentSetKey;
        private static ComponentFiber s_parentFiber;

        // Read by name so this file still builds on a tree without the property, where the case fails instead.
        private static bool MarkedSuspended(ComponentFiber fiber)
            => typeof(ComponentFiber).GetProperty("SuspendedOn",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(fiber) != null;

        private string Texts() => string.Join("|", _root.Query<Label>().ToList().Select(label => label.text));

        // Memoized, so a pass above re-renders it only for being marked dirty.
        [Component(Memoize = true)]
        private static VNode SuspendingChildRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : (s_source = new VelvetTaskCompletionSource<int>()).Task, own);
            return V.Label(text: "child:" + value);
        }

        // Memoized for the reason SuspendingChildRender is.
        [Component(Memoize = true)]
        private static VNode KeyedChildRender(int key)
        {
            var value = Hooks.Use<int>(_ => key == 0
                ? VelvetTask.FromResult(0)
                : (s_source = new VelvetTaskCompletionSource<int>()).Task, key);
            return V.Label(text: "child:" + value);
        }

        [Component]
        private static VNode KeyedParentRender()
        {
            s_parentFiber = FiberAmbientStack.Current;
            var (key, setKey) = Hooks.UseState(0);
            s_parentSetKey = setKey;
            return V.Div(children: new VNode[]
            {
                V.Component(KeyedChildRender, key, key: "child"),
                V.Label(text: "parent:" + key),
            });
        }

        private static ComponentFiber s_innerFiber;
        private static ComponentFiber s_outerFiber;
        private static StateUpdater<int> s_innerSetOwn;
        private static StateUpdater<int> s_outerSetTick;
        private static bool s_innerMemoized;

        [Component]
        private static VNode OuterPassRender()
        {
            s_outerFiber = FiberAmbientStack.Current;
            var (tick, setTick) = Hooks.UseState(0);
            s_outerSetTick = setTick;
            return V.Div(children: new VNode[]
            {
                s_innerMemoized
                    ? V.Component(MemoizedInnerPassRender, tick, key: "inner")
                    : V.Component(InnerPassRender, key: "inner"),
                V.Label(text: "outer:" + tick),
            });
        }

        // Memoized, with the outer tick as a prop so the outer pass renders it again and suspends too.
        [Component(Memoize = true)]
        private static VNode MemoizedInnerPassRender(int tick)
        {
            s_innerFiber = FiberAmbientStack.Current;
            var (own, setOwn) = Hooks.UseState(0);
            s_innerSetOwn = setOwn;
            return V.Div(name: "inner-" + tick, children: new VNode[] { V.Component(UnmemoizedKeyedChildRender, own, key: "child") });
        }

        [Component]
        private static VNode InnerPassRender()
        {
            s_innerFiber = FiberAmbientStack.Current;
            var (own, setOwn) = Hooks.UseState(0);
            s_innerSetOwn = setOwn;
            return V.Div(children: new VNode[] { V.Component(UnmemoizedKeyedChildRender, own, key: "child") });
        }

        // Not memoized, so the outer pass renders it again and suspends on the resource the inner one did.
        [Component]
        private static VNode UnmemoizedKeyedChildRender(int key)
        {
            var value = Hooks.Use<int>(_ => key == 0
                ? VelvetTask.FromResult(0)
                : (s_source = new VelvetTaskCompletionSource<int>()).Task, key);
            return V.Label(text: "child:" + value);
        }

        private static StateUpdater<int> s_pageSetId;

        [Component(Memoize = true)]
        private static VNode MemoizedPageRender()
        {
            var (id, setId) = Hooks.UseState(0);
            s_pageSetId = setId;
            return V.Fragment(new VNode[]
            {
                V.Label(text: "page:" + id),
                V.Component(KeyedChildRender, id, key: "details"),
            });
        }

        [Component]
        private static VNode PageBoundaryHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(MemoizedPageRender, key: "page") }),
            });

        private static VelvetTaskCompletionSource<int> s_otherSource;
        private static StateUpdater<int> s_readerSetOwn;
        private static ComponentFiber s_readerFiber;
        private static int s_twoReadersRenders;

        [Component]
        private static VNode TwoReadersRender()
        {
            s_outerFiber = FiberAmbientStack.Current;
            s_twoReadersRenders++;
            var (tick, setTick) = Hooks.UseState(0);
            s_outerSetTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Component(UnmemoizedKeyedChildRender, tick, key: "child"),
                V.Component(OwnReaderRender, key: "reader"),
                V.Label(text: "outer:" + tick),
            });
        }

        [Component]
        private static VNode OwnReaderRender()
        {
            s_readerFiber = FiberAmbientStack.Current;
            var (own, setOwn) = Hooks.UseState(0);
            s_readerSetOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : (s_otherSource = new VelvetTaskCompletionSource<int>()).Task, own);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode PlainHostRender()
            => V.Div(children: new VNode[]
            {
                V.Label(text: "host"),
                V.Component(SuspendingChildRender, key: "child"),
            });

        // Takes a prop, without which a body calling no hook is left unmemoized by the compiler, as
        // BoundaryHostRender's is.
        [Component]
        private static VNode PropBoundaryHostRender(int tag)
            => V.Div(name: "host-" + tag, children: new VNode[]
            {
                V.Label(text: "host"),
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(SuspendingChildRender, key: "child") }),
            });

        private static ComponentFiber s_indicatorFiber;
        private static TransitionStarter s_indicatorStart;

        // A sibling of the component its transition writes, so nothing renders that component but its own flush.
        [Component]
        private static VNode IndicatorHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(IndicatorRender, key: "indicator"),
                V.Component(SuspendingChildRender, key: "child"),
            });

        [Component]
        private static VNode IndicatorRender()
        {
            s_indicatorFiber = FiberAmbientStack.Current;
            var (isPending, start) = Hooks.UseTransition();
            s_indicatorStart = start;
            return V.Label(text: "pending:" + isPending);
        }

        private static StateUpdater<int> s_selfSetOwn;

        // Renders a Suspense of its own around its output, so its read is caught by the one above it.
        [Component]
        private static VNode SelfSuspenseRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_selfSetOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : (s_source = new VelvetTaskCompletionSource<int>()).Task, own);
            return V.Suspense(
                fallback: V.Label(text: "inner-loading"),
                children: new VNode[] { V.Label(text: "self:" + value) });
        }

        [Component]
        private static VNode OuterSuspenseHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "outer-loading"),
                    children: new VNode[] { V.Component(SelfSuspenseRender, key: "self") }),
            });

        private static StateUpdater<int> s_tickedSetKey;
        private static StateUpdater<int> s_tickedSetTick;

        [Component]
        private static VNode TickedParentRender()
        {
            var (key, setKey) = Hooks.UseState(0);
            var (tick, setTick) = Hooks.UseState(0);
            s_tickedSetKey = setKey;
            s_tickedSetTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Component(KeyedChildRender, key, key: "child"),
                V.Label(text: "parent:" + key + ":" + tick),
            });
        }

        // The Suspense beside the parent is expanded in this mount ahead of the parent's update, so that update
        // suspends after a Suspense expansion has closed; it catches nothing of it, since another component renders it.
        [Component]
        private static VNode TickedHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(TickedParentRender, key: "ticked-parent"),
                V.Component(StaticSuspenseRender, key: "static"),
            });

        [Component]
        private static VNode StaticSuspenseRender()
            => V.Suspense(fallback: V.Label(text: "static-loading"), children: new VNode[] { V.Label(text: "static") });

        private static StateUpdater<bool> s_showSetShow;

        [Component(Memoize = true)]
        private static VNode MemoizedReaderRender()
        {
            var value = Hooks.Use<int>(_ => (s_source = new VelvetTaskCompletionSource<int>()).Task, "reader");
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode ShowParentRender()
        {
            var (shown, setShown) = Hooks.UseState(false);
            s_showSetShow = setShown;
            return V.Div(children: new VNode[]
            {
                shown ? V.Component(MemoizedReaderRender, key: "reader") : null,
                V.Label(text: "shown:" + shown),
            });
        }

        private static StateUpdater<int> s_pairSetA;
        private static StateUpdater<int> s_pairSetB;
        private static VelvetTaskCompletionSource<int> s_sourceA;
        private static VelvetTaskCompletionSource<int> s_sourceB;

        [Component(Memoize = true)]
        private static VNode PairReaderARender(int key)
        {
            var value = Hooks.Use<int>(_ => key == 0
                ? VelvetTask.FromResult(0)
                : (s_sourceA = new VelvetTaskCompletionSource<int>()).Task, key);
            return V.Label(text: "a:" + value);
        }

        [Component(Memoize = true)]
        private static VNode PairReaderBRender(int key)
        {
            var value = Hooks.Use<int>(_ => key == 0
                ? VelvetTask.FromResult(0)
                : (s_sourceB = new VelvetTaskCompletionSource<int>()).Task, key);
            return V.Label(text: "b:" + value);
        }

        // B sits ahead of A, so the second update suspends on B before it reaches A.
        [Component]
        private static VNode PairParentRender()
        {
            var (a, setA) = Hooks.UseState(0);
            var (b, setB) = Hooks.UseState(0);
            s_pairSetA = setA;
            s_pairSetB = setB;
            return V.Div(children: new VNode[]
            {
                V.Component(PairReaderBRender, b, key: "b"),
                V.Component(PairReaderARender, a, key: "a"),
            });
        }

        [Component]
        private static VNode BoundaryHostRender()
            => V.Div(children: new VNode[]
            {
                V.Label(text: "host"),
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(SuspendingChildRender, key: "child") }),
            });
    }
}
