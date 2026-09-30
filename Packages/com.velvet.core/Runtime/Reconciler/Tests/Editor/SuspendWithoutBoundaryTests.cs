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
    /// <item>With no boundary above, the flush does not throw, the component keeps its previous output, and the
    /// resource resolving renders it again with the value. Where the pass that suspended was an ancestor's, the
    /// resolve retries that ancestor's pass.</item>
    /// <item>With a boundary above, that boundary renders again and shows its fallback.</item>
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
            // Arrange — the child comes before the parent's own label, so a pass that stopped at the child left
            // that label unpatched
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(KeyedParentRender, key: "parent"));
            s_parentSetKey.Invoke(1);
            mounted.FlushStateForTest();
            var whileSuspended = Texts();

            // Act
            s_source.TrySetResult(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((whileSuspended, Texts()), Is.EqualTo(("child:0|parent:0", "child:5|parent:1")),
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

        private static VelvetTaskCompletionSource<int> s_source;
        private static StateUpdater<int> s_setOwn;
        private static StateUpdater<int> s_parentSetKey;
        private static ComponentFiber s_parentFiber;

        // Read by name so this file still builds on a tree without the property, where the case fails instead.
        private static bool MarkedSuspended(ComponentFiber fiber)
            => (bool)typeof(ComponentFiber).GetProperty("SuspendedWithoutBoundary",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(fiber)!;

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
