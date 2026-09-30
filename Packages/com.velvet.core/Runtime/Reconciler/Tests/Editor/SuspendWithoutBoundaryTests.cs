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
    /// A component whose own render suspended before it reconciled anything shows what it showed before.</item>
    /// <item>With a boundary above, that boundary renders again and shows its fallback, and the component whose
    /// update suspended renders that update inside it, a memoized one included.</item>
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
