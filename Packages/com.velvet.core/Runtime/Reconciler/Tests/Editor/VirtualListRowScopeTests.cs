using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a <see cref="V.VirtualList{T}"/> row's render takes from around it and leaves to the rows after
    /// it, in a range a scroll renders and in one a render of the list's host renders.
    /// <list type="bullet">
    /// <item>A boundary inside a row that swaps in its fallback as the range patches the row leaves the rows after it
    /// in the range built whole.</item>
    /// <item>A row a scroll mounts reads the value its enclosing Provider holds now, under a memoized host that has not
    /// rendered since that value changed; a row a render of the host mounts reads the value that render gave the
    /// Provider, a scroll having rendered a range before it.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListRowScopeTests
    {
        private VisualElement _root;
        private static bool s_refHeld;
        private static StateUpdater<int> s_render;
        private static StateUpdater<string> s_setTheme;

        private static readonly ComponentContext<string> s_theme = ComponentContext<string>.Create("light");

        // The cleanup a held ref returns throws once the patch takes the ref away, which the boundary owning the label
        // catches as it swaps in its fallback.
        private static readonly Func<VisualElement, Action> s_refWithThrowingCleanup =
            _ => () => throw new InvalidOperationException("ref cleanup refused");

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            (s_refHeld, s_render, s_setTheme, s_hostMemoized) = (true, default, default, true);
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode RefBoundary(bool holdRef)
        {
            Hooks.UseFallback(_ => V.Label(name: "fallback", text: "fallback"));
            return V.Label(name: "held", refCallback: holdRef ? s_refWithThrowingCleanup : null);
        }

        // Rows "a" to "d"; "b" holds the boundary, the others a label named after the row.
        [Component(Compiler = false)]
        private static VNode BoundaryRowsHost()
        {
            var (_, setRender) = Hooks.UseState(0);
            s_render = setRender;
            return V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: new[] { "a", "b", "c", "d" },
                    keySelector: item => item,
                    itemHeight: 10f,
                    renderer: item => item == "b"
                        ? V.Div(name: item, children: new VNode[] { V.Component(RefBoundary, s_refHeld, key: "boundary") })
                        : V.Div(name: item, children: new VNode[] { V.Label(name: item + "-label", text: item) }),
                    overscan: 0,
                    key: "list"),
            });
        }

        [Component(Compiler = false)]
        private static VNode ThemedRow(string item) => V.Label(name: item, text: Hooks.UseContext(s_theme));

        [Component(Memoize = true, Compiler = false)]
        private static VNode MemoizedListHost()
            => V.VirtualList(
                items: new[] { "a", "b", "c" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: item => V.Component(ThemedRow, item, key: item),
                overscan: 0);

        [Component(Compiler = false)]
        private static VNode ListHost()
            => V.VirtualList(
                items: new[] { "a", "b", "c" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: item => V.Component(ThemedRow, item, key: item),
                overscan: 0);

        private static bool s_hostMemoized;

        [Component(Compiler = false)]
        private static VNode ThemeRoot()
        {
            var (theme, setTheme) = Hooks.UseState("light");
            s_setTheme = setTheme;
            return V.Provider(s_theme, theme, children: new VNode[]
            {
                s_hostMemoized ? V.Component(MemoizedListHost, key: "host") : V.Component(ListHost, key: "host"),
            });
        }

        private FiberVirtualListController Controller(MountedTree mounted)
            => mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()];

        // GREEN_ON_BASE(characterization): the base builds a scrolled range's rows with no pass around them.
        // A boundary's catch inside one stops nothing after it there, which bracketing the range has to keep.
        [Test]
        public void Given_ARowWhoseBoundarySwapsInItsFallbackAsAScrollPatchesIt_When_TheScrollBuildsARowAfterIt_Then_ThatRowIsBuiltWhole()
        {
            // Arrange — "b"'s boundary holds a ref after a first scroll renders "a" to "c".
            using var mounted = V.Mount(_root, V.Component(BoundaryRowsHost, key: "host"), CaughtErrors.Unlogged);
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 30f);
            s_refHeld = false;

            // Act — a scroll that patches "b", taking its ref away, and builds "d" after it.
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 40f);

            // Assert
            Assert.That(_root.Q<Label>("d-label"), Is.Not.Null);
        }

        [Test]
        public void Given_ARowWhoseBoundarySwapsInItsFallbackAsTheHostsRenderPatchesIt_When_ThatRenderBuildsARowAfterIt_Then_ThatRowIsBuiltWhole()
        {
            // Arrange — the viewport height a geometry pass would have left, so a render of the host renders the range
            // inside its pass; "b"'s boundary holds a ref after a first render.
            using var mounted = V.Mount(_root, V.Component(BoundaryRowsHost, key: "host"), CaughtErrors.Unlogged);
            var viewportHeight = typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!;
            viewportHeight.SetValue(Controller(mounted), 30f);
            s_render.Invoke(previous => previous + 1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_refHeld = false;
            viewportHeight.SetValue(Controller(mounted), 40f);

            // Act — a render that patches "b", taking its ref away, and builds "d" after it.
            s_render.Invoke(previous => previous + 1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(_root.Q<Label>("d-label"), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base mounts a scrolled row at depth 0, rebuilding its Providers' spine.
        // A row mounted there reads what an enclosing Provider committed last, which bracketing the range has to keep.
        [Test]
        public void Given_AMemoizedListHostUnderAProviderWhoseValueChanged_When_AScrollMountsARow_Then_TheRowReadsTheCurrentValue()
        {
            // Arrange — "a" mounted by a first scroll; the Provider's value changes, which the memoized host does not
            // render again for.
            using var mounted = V.Mount(_root, V.Component(ThemeRoot, key: "root"));
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();

            // Act — a scroll that mounts "b".
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 20f);

            // Assert
            Assert.That(_root.Q<Label>("b")?.text, Is.EqualTo("dark"));
        }

        // GREEN_ON_BASE(characterization): the base mounts a row its host's render reaches inside that render's walk.
        // The walk pushed the Provider's new value, which a range a scroll rendered first must not take from it.
        [Test]
        public void Given_ARangeAScrollRendered_When_AProvidersNewValueRendersTheHostAndMountsARow_Then_TheRowReadsTheNewValue()
        {
            // Arrange — a host that renders again with its Provider; "a" mounted by a scroll, and the viewport height a
            // geometry pass would have left at two rows, so the host's render mounts "b" inside its pass.
            s_hostMemoized = false;
            using var mounted = V.Mount(_root, V.Component(ThemeRoot, key: "root"));
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(Controller(mounted), 20f);

            // Act
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("b")?.text, Is.EqualTo("dark"));
        }
    }
}
