using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a <see cref="V.VirtualList{T}"/> row's render takes from around it and leaves to the rows after
    /// it.
    /// <list type="bullet">
    /// <item>In a range a scroll renders, a boundary inside a row that swaps in its fallback as the range patches the
    /// row leaves the rest of that row patched, a Component the patch drops from the row gone, and the rows after it
    /// built whole. The rows after a row are built whole too where a boundary's fallback in it fails as it is swapped
    /// in, caught by a boundary outside it, and where a boundary catches in the resume of a component's parked render
    /// that the patch drains. A render after such a range still keeps the tree it had before where a boundary in its
    /// walk aborts it.</item>
    /// <item>A row a scroll mounts reads the value its enclosing Provider holds now, under a memoized host that has not
    /// rendered since that value changed; a row a render of the host mounts reads the value that render gave the
    /// Provider, a scroll having rendered a range before it; one inside a Provider of the row's own reads that
    /// Provider's value over one of the same context above the list, and so does one inside a Provider the host puts
    /// around the list, as does a Portal's child a row holds — with the list behind a memo under a Provider in a Portal,
    /// or in a Suspense's shown fallback, too — and after a render of the host that a boundary aborted, the row reads
    /// the value the host kept. A component outside the enclosing Provider
    /// reads the context's default after a scroll has mounted rows under that Provider.</item>
    /// <item>A row that renders again on its own reads the value its enclosing Provider holds then, whether a render of
    /// the host gave the Provider that value or a memoized host has not rendered since, and so does a Portal's child a
    /// row holds under such a memoized host. A row reads its own row's Provider where every row's Provider holds one
    /// shared body node.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListRowScopeTests
    {
        private VisualElement _root;
        private static bool s_refHeld;
        private static string s_title;
        private static bool s_badge;
        private static Func<VNode> s_rowB;
        private static bool s_childShown;
        private static ComponentFiber s_parkingFiber;
        private static bool s_fallbackShown;
        private static bool s_rowProvided;
        private static bool s_hostMemoized;
        private static ComponentFiber s_hostFiber;
        private static StateUpdater<bool> s_setFlipped;
        private static StateUpdater<string> s_setTheme;
        private static StateUpdater<int> s_renderOutside;

        private static readonly ComponentContext<string> s_theme = ComponentContext<string>.Create("none");

        // The cleanup a held ref returns throws once the patch takes the ref away, which the boundary owning the label
        // catches as it swaps in its fallback.
        private static readonly Func<VisualElement, Action> s_refWithThrowingCleanup =
            _ => () => throw new InvalidOperationException("ref cleanup refused");

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            (s_refHeld, s_title, s_badge, s_childShown, s_parkingFiber) = (true, "x", false, true, null);
            s_rowB = BoundaryRow;
            (s_fallbackShown, s_rowProvided, s_hostMemoized) = (false, false, true);
            (s_hostFiber, s_setFlipped, s_setTheme, s_renderOutside) = (null, default, default, default);
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode RefBoundary(bool holdRef)
        {
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Label(name: "fallback", text: "fallback");
            });
            return V.Label(name: "held", refCallback: holdRef ? s_refWithThrowingCleanup : null);
        }

        [Component(Compiler = false)]
        private static VNode Badge() => V.Label(name: "badge", text: "badge");

        [Component(Compiler = false)]
        private static VNode BoundaryRowsHost()
            => V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: new[] { "a", "b", "c", "d" },
                    keySelector: item => item,
                    itemHeight: 10f,
                    renderer: item => item != "b"
                        ? V.Div(name: item, children: new VNode[] { V.Label(name: item + "-label", text: item) })
                        : s_rowB(),
                    overscan: 0,
                    key: "list"),
            });

        private static VNode BoundaryRow()
            => V.Div(name: "b", children: new VNode[]
            {
                s_badge ? V.Component(Badge, key: "badge") : null,
                V.Component(RefBoundary, s_refHeld, key: "boundary"),
                V.Label(name: "after", text: s_title),
            });

        private static VNode NestedBoundariesRow()
            => V.Div(name: "b", children: new VNode[] { V.Component(OuterBoundary, key: "outer") });

        // The child's layout cleanup throws as a patch drops it, which the inner boundary takes up by swapping in its
        // fallback; that fallback throws as it renders, which the outer boundary catches. The child sits at the inner boundary's own
        // level, so the patch drops it in the walk that expands both boundaries, and its cleanup runs once that walk is
        // done with them.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OuterBoundary()
        {
            Hooks.UseFallback(_ => V.Label(name: "outer-fallback", text: "caught"));
            return V.Component(InnerBoundary, key: "inner");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode InnerBoundary()
        {
            Hooks.UseFallback(_ => V.Component(FailingFallback, key: "failing"));
            return V.Fragment(s_childShown ? V.Component(CleanupFailingChild, key: "child") : null);
        }

        [Component(Compiler = false)]
        private static VNode CleanupFailingChild()
        {
            Hooks.UseLayoutEffect(() => () => throw new InvalidOperationException("cleanup refused"), new object[0]);
            return V.Label(name: "child", text: "child");
        }

        [Component(Compiler = false)]
        private static VNode FailingFallback() => throw new InvalidOperationException("fallback refused");

        private static VNode ParkingRow()
            => V.Div(name: "b", children: new VNode[] { V.Component(ParkingRows, key: "parking") });

        // Keyed host leaves, so a render on the Transition lane under the tiny budget parks ahead of the last, whose
        // boundary's label that render takes the ref away from.
        [Component(Compiler = false)]
        private static VNode ParkingRows()
        {
            s_parkingFiber = FiberAmbientStack.Current;
            return V.Fragment(children: new VNode[]
            {
                V.Label(name: "p0", key: "p0"),
                V.Label(name: "p1", key: "p1"),
                V.Div(name: "box", key: "box", children: new VNode[]
                {
                    V.Component(RefBoundary, s_refHeld, key: "boundary"),
                }),
            });
        }

        // The host's own boundary, beside the list, takes the ref away from its label once flipped.
        [Component(Compiler = false)]
        private static VNode ListBesideABoundaryHost()
        {
            s_hostFiber = FiberAmbientStack.Current;
            var (flipped, setFlipped) = Hooks.UseState(false);
            s_setFlipped = setFlipped;
            return V.Div(name: flipped ? "host-flipped" : "host-initial", children: new VNode[]
            {
                V.VirtualList(
                    items: new[] { "a", "b" },
                    keySelector: item => item,
                    itemHeight: 10f,
                    renderer: item => V.Label(name: item, text: item),
                    overscan: 0,
                    key: "list"),
                V.Component(RefBoundary, !flipped, key: "boundary"),
            });
        }

        private static VNode Row(string item) => s_rowProvided
            ? V.Provider(s_theme, "row", children: new VNode[] { V.Component(ThemedRow, item, key: item) })
            : V.Component(ThemedRow, item, key: item);

        [Component(Compiler = false)]
        private static VNode ThemedRow(string item) => V.Label(name: item, text: Hooks.UseContext(s_theme));

        [Component(Memoize = true, Compiler = false)]
        private static VNode MemoizedListHost()
            => V.VirtualList(
                items: new[] { "a", "b", "c" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: Row,
                overscan: 0);

        [Component(Compiler = false)]
        private static VNode ListHost()
            => V.VirtualList(
                items: new[] { "a", "b", "c" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: Row,
                overscan: 0);

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

        [Component(Compiler = false)]
        private static VNode OutsideReader()
        {
            var (_, setRender) = Hooks.UseState(0);
            s_renderOutside = setRender;
            return V.Label(name: "outside", text: Hooks.UseContext(s_theme));
        }

        [Component(Compiler = false)]
        private static VNode ThemeRootBesideAReader()
            => V.Div(children: new VNode[]
            {
                V.Component(ThemeRoot, key: "theme"),
                V.Component(OutsideReader, key: "outside"),
            });

        private static Func<string, VNode> s_providingHostRow;
        private static StateUpdater<string> s_setListValue;
        private static VisualElement s_portalTarget;

        // A row whose component a Portal holds, with no component of the row's own around the Portal.
        private static VNode PortalRow(string item)
            => V.Portal(s_portalTarget, children: new VNode[] { V.Component(ThemedRow, item, key: item) });

        // Provides the context again around its list, with a value of its own state.
        [Component(Compiler = false)]
        private static VNode ProvidingListHost()
        {
            var (value, setValue) = Hooks.UseState("list");
            s_setListValue = setValue;
            return V.Provider(s_theme, value, children: new VNode[]
            {
                V.VirtualList(
                    items: new[] { "a", "b", "c" },
                    keySelector: item => item,
                    itemHeight: 10f,
                    renderer: item => s_providingHostRow(item),
                    overscan: 0),
            });
        }

        [Component(Compiler = false)]
        private static VNode ThemeRootAroundAProvidingHost()
        {
            var (theme, setTheme) = Hooks.UseState("light");
            s_setTheme = setTheme;
            return V.Provider(s_theme, theme, children: new VNode[] { V.Component(ProvidingListHost, key: "host") });
        }

        [Component(Memoize = true, Compiler = false)]
        private static VNode MemoizedPortalRowsHost()
            => V.VirtualList(
                items: new[] { "a", "b", "c" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: PortalRow,
                overscan: 0);

        [Component(Compiler = false)]
        private static VNode ThemeRootAroundAPortalRowsHost()
        {
            var (theme, setTheme) = Hooks.UseState("light");
            s_setTheme = setTheme;
            return V.Provider(s_theme, theme, children: new VNode[] { V.Component(MemoizedPortalRowsHost, key: "host") });
        }

        // The host's boundary, beside its list, takes the ref away from its label once flipped, in the render that also
        // changes the value around the list.
        [Component(Compiler = false)]
        private static VNode ProvidingListBesideABoundaryHost()
        {
            s_hostFiber = FiberAmbientStack.Current;
            var (flipped, setFlipped) = Hooks.UseState(false);
            s_setFlipped = setFlipped;
            return V.Provider(s_theme, flipped ? "flipped" : "kept", children: new VNode[]
            {
                V.Div(name: flipped ? "host-flipped" : "host-initial", children: new VNode[]
                {
                    V.VirtualList(
                        items: new[] { "a", "b" },
                        keySelector: item => item,
                        itemHeight: 10f,
                        renderer: Row,
                        overscan: 0,
                        key: "list"),
                    V.Component(RefBoundary, !flipped, key: "boundary"),
                }),
            });
        }

        private static readonly System.Collections.Generic.Dictionary<string, StateUpdater<int>> s_sharedBodyBumps = new();
        private static VNode[] s_sharedRowBody;

        // Every row's Provider holds the same body node.
        private static VNode SharedBodyRow(string item) => V.Provider(s_theme, "item-" + item, children: s_sharedRowBody);

        [Component(Compiler = false)]
        private static VNode SharedBody()
        {
            var (count, bump) = Hooks.UseState(0);
            var theme = Hooks.UseContext(s_theme);
            s_sharedBodyBumps[theme] = bump;
            return V.Label(name: "body-" + theme, text: theme + "#" + count);
        }

        [Component(Compiler = false)]
        private static VNode SharedBodyListHost()
            => V.VirtualList(
                items: new[] { "a", "b" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: SharedBodyRow,
                overscan: 0);

        // The row's Provider holds a Portal, which holds the body.
        [Component(Compiler = false)]
        private static VNode ProvidedPortalRowsHost()
            => V.VirtualList(
                items: new[] { "a" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: item => V.Provider(s_theme, "portal-row-" + item, children: new VNode[]
                {
                    V.Portal(s_portalTarget, children: new VNode[] { V.Component(SharedBody) }),
                }),
                overscan: 0);

        // The row's Provider holds an element, which holds the body.
        [Component(Compiler = false)]
        private static VNode FramedBodyListHost()
            => V.VirtualList(
                items: new[] { "a" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: item => V.Provider(s_theme, "framed-" + item, children: new VNode[]
                {
                    V.Div(name: "frame", children: new VNode[] { V.Component(SharedBody) }),
                }),
                overscan: 0);

        private static VNode ThemedRowsList()
            => V.VirtualList(
                items: new[] { "a", "b", "c" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: Row,
                overscan: 0);

        private static VisualElement s_otherPortalTarget;

        // The list sits in the second of two Portals, behind a memo whose dependencies never change, under a Provider the
        // Portal holds.
        [Component(Compiler = false)]
        private static VNode PortalMemoListHost()
            => V.Div(children: new VNode[]
            {
                V.Portal(s_otherPortalTarget, children: new VNode[] { V.Label(text: "other") }),
                V.Portal(s_portalTarget, children: new VNode[]
                {
                    V.Provider(s_theme, "list", children: new VNode[] { V.Memoized(ThemedRowsList, Array.Empty<object>()) }),
                }),
            });

        private static VelvetTaskCompletionSource<int> s_neverRead;

        [Component(Compiler = false)]
        private static VNode NeverReadyReader()
        {
            var value = Hooks.Use<int>(_ => s_neverRead.Task, 0);
            return V.Label(text: "read " + value);
        }

        // The list is the fallback of a Suspense whose reader never resolves, with the context around the Suspense.
        [Component(Compiler = false)]
        private static VNode SuspenseFallbackListHost()
            => V.Provider(s_theme, "list", children: new VNode[]
            {
                V.Suspense(fallback: ThemedRowsList(), children: new VNode[] { V.Component(NeverReadyReader, key: "reader") }),
            });

        [Component(Compiler = false)]
        private static VNode ThemeRootAroundAPortalMemoListHost()
            => V.Provider(s_theme, "light", children: new VNode[] { V.Component(PortalMemoListHost, key: "host") });

        [Component(Compiler = false)]
        private static VNode ThemeRootAroundASuspenseFallbackListHost()
            => V.Provider(s_theme, "light", children: new VNode[] { V.Component(SuspenseFallbackListHost, key: "host") });

        // A list the mount's root does not render, over the root as its host, with the context the test hands it.
        private static FiberVirtualListController ListOutsideTheHostsOutput(
            MountedTree mounted, ScrollView scrollView, Func<string, VNode> renderer)
        {
            var context = new ComponentContextStack();
            context.Push(s_theme, "captured");
            var node = V.VirtualList(
                items: new[] { "a" }, keySelector: item => item, itemHeight: 10f, renderer: renderer, overscan: 0);
            return new FiberVirtualListController(scrollView, node, mounted.Root.Reconciler, mounted.Root, context);
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

        // GREEN_ON_BASE(characterization): the base patches a scrolled range's rows with no pass around them.
        // A boundary's catch in a row stops nothing after it there, so the row's later children are patched too.
        [Test]
        public void Given_ARowWhoseBoundarySwapsInItsFallbackAsAScrollPatchesIt_When_TheRowHoldsALabelAfterTheBoundary_Then_TheLabelIsPatched()
        {
            // Arrange — "b"'s boundary holds a ref, and its label reads "x", after a first scroll renders "a" to "c".
            using var mounted = V.Mount(_root, V.Component(BoundaryRowsHost, key: "host"), CaughtErrors.Unlogged);
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 30f);
            (s_refHeld, s_title) = (false, "y");

            // Act — a scroll that patches "b", taking its ref away and retitling its label.
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 40f);

            // Assert
            Assert.That(_root.Q<Label>("after")?.text, Is.EqualTo("y"));
        }

        // GREEN_ON_BASE(characterization): the base patches a scrolled range's rows with no pass around them.
        // A boundary's catch in a row aborts nothing there, so a Component the patch drops leaves with its elements.
        [Test]
        public void Given_ARowWhoseBoundarySwapsInItsFallbackAsAScrollPatchesIt_When_ThatPatchDropsAComponentAheadOfIt_Then_TheComponentsElementsLeave()
        {
            // Arrange — "b" holds the badge ahead of its boundary, which holds a ref, after a first scroll.
            s_badge = true;
            using var mounted = V.Mount(_root, V.Component(BoundaryRowsHost, key: "host"), CaughtErrors.Unlogged);
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 30f);
            (s_badge, s_refHeld) = (false, false);

            // Act — a scroll that patches "b", dropping the badge and taking the boundary's ref away.
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 40f);

            // Assert
            Assert.That(_root.Q<Label>("badge"), Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base swaps a scrolled row's boundary's fallback in as a pass of its own.
        // The abort a boundary outside it raises as that fallback fails ends with that pass, which bracketing the range
        // has to keep.
        [Test]
        public void Given_ARowWhoseBoundarysFallbackFailsAsAScrollPatchesIt_When_TheScrollBuildsARowAfterIt_Then_ThatRowIsBuiltWhole()
        {
            // Arrange — "b" holds the two boundaries around the child, after a first scroll renders "a" to "c".
            s_rowB = NestedBoundariesRow;
            using var mounted = V.Mount(_root, V.Component(BoundaryRowsHost, key: "host"), CaughtErrors.Unlogged);
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 30f);
            s_childShown = false;

            // Act — a scroll that patches "b", dropping the child, and builds "d" after it.
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 40f);

            // Assert
            Assert.That(_root.Q<Label>("d-label"), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base drains a scrolled row's parked render as a resume of its own.
        // The abort a boundary catching in that resume raises ends with it, which bracketing the range has to keep.
        [Test]
        public void Given_ARowWhoseComponentsRenderParked_When_AScrollPatchesItAndABoundaryCatchesInTheResume_Then_ARowAfterItIsBuiltWhole()
        {
            // Arrange — "b"'s component, after a first scroll renders "a" to "c", parks a render on the Transition lane
            // under the tiny budget ahead of the row whose ref that render takes away.
            s_rowB = ParkingRow;
            using var mounted = V.Mount(_root, V.Component(BoundaryRowsHost, key: "host"), CaughtErrors.Unlogged);
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 30f);
            s_refHeld = false;
            s_parkingFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_parkingFiber.FlushStateWithTinyBudgetForTest();
            var parked = s_parkingFiber.HasPendingReconcileWorkForTest() && !s_fallbackShown;

            // Act — a scroll that patches "b", draining the parked render, and builds "d" after it.
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 40f);

            // Assert — null unless the render parked before the scroll and the scroll's resume of it caught, which
            // the name of the row's label never equals.
            var resumedAndCaught = parked && !s_parkingFiber.HasPendingReconcileWorkForTest() && s_fallbackShown;
            Assert.That(resumedAndCaught ? _root.Q<Label>("d-label")?.name : null, Is.EqualTo("d-label"));
        }

        // GREEN_ON_BASE(characterization): the base aborts a pass in whose walk a boundary catches a failing ref cleanup.
        // It does so whatever ran before the pass, which a scroll's range that ended ahead of it must not change.
        [Test]
        public void Given_ARangeAScrollRendered_When_ABoundaryInTheHostsNextRenderAbortsIt_Then_TheHostKeepsTheTreeItHadBefore()
        {
            // Arrange — a scroll renders the list's range ahead of the host's render.
            using var mounted = V.Mount(_root, V.Component(ListBesideABoundaryHost, key: "host"), CaughtErrors.Unlogged);
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 20f);

            // Act — the host's render takes the ref of the boundary's label away, whose cleanup the boundary catches.
            s_setFlipped.Invoke(true);
            mounted.FlushStateForTest();

            // Assert — null where the boundary caught nothing, which the name the host had before never equals.
            Assert.That(
                s_fallbackShown ? (s_hostFiber?.PreviousTree?[0] as BaseElementNode)?.Name : null,
                Is.EqualTo("host-initial"));
        }

        [Test]
        public void Given_ARowInAProviderOfItsOwnUnderAProviderOfTheSameContextAboveTheList_When_AScrollMountsIt_Then_ItReadsItsOwnProvidersValue()
        {
            // Arrange — the list renders no range until a scroll asks for one.
            s_rowProvided = true;
            using var mounted = V.Mount(_root, V.Component(ThemeRoot, key: "root"));

            // Act
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Assert
            Assert.That(_root.Q<Label>("a")?.text, Is.EqualTo("row"));
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

        // GREEN_ON_BASE(characterization): the base's item scope pushes onto the context cursor only what it pops.
        // That is the snapshot; what the scope pushes over it has to come off with it.
        [Test]
        public void Given_AScrollThatMountedARowUnderAProvider_When_AComponentOutsideThatProviderRendersAgain_Then_ItReadsTheContextsDefault()
        {
            // Arrange — "a" mounted by a scroll under the Provider, whose value differs from the context's default.
            using var mounted = V.Mount(_root, V.Component(ThemeRootBesideAReader, key: "root"));
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Act — a render of the reader alone.
            s_renderOutside.Invoke(previous => previous + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("outside")?.text, Is.EqualTo("none"));
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

        [Test]
        public void Given_AHostProvidingAroundItsListUnderAProviderOfTheSameContext_When_AScrollMountsARow_Then_TheRowReadsTheHostsProvider()
        {
            // Arrange — the list renders no range until a scroll asks for one.
            s_providingHostRow = Row;
            using var mounted = V.Mount(_root, V.Component(ThemeRootAroundAProvidingHost, key: "root"));

            // Act
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Assert
            Assert.That(_root.Q<Label>("a")?.text, Is.EqualTo("list"));
        }

        [Test]
        public void Given_AHostProvidingAroundItsListUnderAProviderOfTheSameContext_When_AScrollMountsARowsPortal_Then_ThePortalsChildReadsTheHostsProvider()
        {
            // Arrange
            s_providingHostRow = PortalRow;
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(ThemeRootAroundAProvidingHost, key: "root"));

            // Act
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Assert
            Assert.That(s_portalTarget.Q<Label>("a")?.text, Is.EqualTo("list"));
        }

        [Test]
        public void Given_AMemoizedListHostUnderAProviderWhoseValueChanged_When_ARowAScrollMountedRendersAgain_Then_ItReadsTheCurrentValue()
        {
            // Arrange — "a" mounted by a scroll.
            using var mounted = V.Mount(_root, V.Component(ThemeRoot, key: "root"));
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Act — the Provider's value changes, which the memoized host does not render again for and the row,
            // reading it, does.
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("a")?.text, Is.EqualTo("dark"));
        }

        [Test]
        public void Given_AMemoizedListHostUnderAProviderWhoseValueChanged_When_APortalsChildARowHoldsRendersAgain_Then_ItReadsTheCurrentValue()
        {
            // Arrange — "a" mounted into the Portal's target by a scroll.
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(ThemeRootAroundAPortalRowsHost, key: "root"));
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Act
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_portalTarget.Q<Label>("a")?.text, Is.EqualTo("dark"));
        }

        [Test]
        public void Given_AHostProvidingAroundItsList_When_ItsRenderChangesTheValueAndARowRendersAgainAlone_Then_TheRowReadsTheNewValue()
        {
            // Arrange — "a" mounted by a scroll.
            s_providingHostRow = Row;
            using var mounted = V.Mount(_root, V.Component(ThemeRootAroundAProvidingHost, key: "root"));
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Act — the host's render changes the value and patches the list, and the row, reading it, renders on its own.
            s_setListValue.Invoke("list-2");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("a")?.text, Is.EqualTo("list-2"));
        }

        [Test]
        public void Given_AHostWhoseAbortedRenderChangedTheValueAroundItsList_When_AScrollMountsARow_Then_TheRowReadsTheValueTheHostKept()
        {
            // Arrange — the host's render changes the value and patches the list, and its boundary then aborts that
            // render, so the host keeps the output it had, which holds the list's earlier node.
            using var mounted = V.Mount(
                _root, V.Component(ProvidingListBesideABoundaryHost, key: "host"), CaughtErrors.Unlogged);
            s_setFlipped.Invoke(true);
            mounted.FlushStateForTest();
            var keptProvider = s_hostFiber?.PreviousTree?[0] as ContextProviderNode;
            var keptList = (keptProvider?.Children?[0] as BaseElementNode)?.Children?[0];
            var listNode = typeof(FiberVirtualListController)
                .GetField("_node", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(Controller(mounted));
            var arranged = s_fallbackShown && keptList != null && !ReferenceEquals(keptList, listNode);

            // Act
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Assert — null where the boundary caught nothing or the list's node was still the one the host kept, which
            // the label's text never equals.
            Assert.That(arranged ? _root.Q<Label>("a")?.text : null, Is.EqualTo("kept"));
        }

        // GREEN_ON_BASE(characterization): the base walks the row's own captured node for the row's own body.
        // What this pins is that a row whose body node the other rows share reads its own row's Provider.
        [Test]
        public void Given_RowsWhoseProvidersShareOneBodyNode_When_TheSecondRowsBodyRendersAgainAlone_Then_ItReadsItsOwnRowsProvider()
        {
            // Arrange — a scroll mounts both rows.
            s_sharedBodyBumps.Clear();
            s_sharedRowBody = new VNode[] { V.Component(SharedBody) };
            using var mounted = V.Mount(_root, V.Component(SharedBodyListHost, key: "host"));
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 20f);

            // Act
            s_sharedBodyBumps["item-b"].Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("body-item-b")?.text, Is.EqualTo("item-b#1"));
        }

        // GREEN_ON_BASE(characterization): the base walks the row's own captured node down to the body inside its element.
        // What this pins is that a row is found from a component inside one of the row's elements.
        [Test]
        public void Given_ARowWhoseProviderHoldsAnElementAroundItsBody_When_TheBodyRendersAgainAlone_Then_ItReadsTheRowsProvider()
        {
            // Arrange — a scroll mounts the row.
            s_sharedBodyBumps.Clear();
            using var mounted = V.Mount(_root, V.Component(FramedBodyListHost, key: "host"));
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Act
            s_sharedBodyBumps["framed-a"].Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("body-framed-a")?.text, Is.EqualTo("framed-a#1"));
        }

        // GREEN_ON_BASE(characterization): the base reads the row's Provider off the Portal's snapshot, taken inside the row.
        // What this pins is that a walk through the list to the Portal goes through the row's Provider on its way.
        [Test]
        public void Given_APortalInsideARowsProvider_When_ThePortalsChildRendersAgainAlone_Then_ItReadsTheRowsProvider()
        {
            // Arrange — a scroll mounts the row, and its Portal's child after it.
            s_sharedBodyBumps.Clear();
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(ProvidedPortalRowsHost, key: "host"));
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Act
            s_sharedBodyBumps["portal-row-a"].Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_portalTarget.Q<Label>("body-portal-row-a")?.text, Is.EqualTo("portal-row-a#1"));
        }

        // GREEN_ON_BASE(characterization): the base reads a row's captured context on every render of the row alone.
        // What this pins is that a row whose list no output of its host holds still reads it.
        [Test]
        public void Given_ARowOfAListItsHostDoesNotRender_When_TheRowRendersAgainAlone_Then_ItReadsTheContextTheListCaptured()
        {
            // Arrange
            s_sharedBodyBumps.Clear();
            using var mounted = V.Mount(_root, V.Label(text: "host"));
            var scrollView = new ScrollView();
            using var list = ListOutsideTheHostsOutput(mounted, scrollView, _ => V.Component(SharedBody));
            list.UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Act
            s_sharedBodyBumps["captured"].Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(scrollView.Q<Label>("body-captured")?.text, Is.EqualTo("captured#1"));
        }

        // GREEN_ON_BASE(characterization): the base reads a Portal child's captured context on every render of it alone.
        // What this pins is that a Portal's child whose Portal no output of its declaring component holds still reads it.
        [Test]
        public void Given_APortalInARowOfAListItsHostDoesNotRender_When_ThePortalsChildRendersAgainAlone_Then_ItReadsTheContextThePortalCaptured()
        {
            // Arrange
            s_sharedBodyBumps.Clear();
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Label(text: "host"));
            using var list = ListOutsideTheHostsOutput(
                mounted, new ScrollView(),
                _ => V.Portal(s_portalTarget, children: new VNode[] { V.Component(SharedBody) }));
            list.UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Act
            s_sharedBodyBumps["captured"].Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_portalTarget.Q<Label>("body-captured")?.text, Is.EqualTo("captured#1"));
        }

        [Test]
        public void Given_AListBehindAMemoUnderAProviderInAPortal_When_AScrollMountsARow_Then_TheRowReadsThatProvider()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            s_otherPortalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(ThemeRootAroundAPortalMemoListHost, key: "root"));

            // Act
            mounted.Root.Reconciler.Context.VirtualListControllers[s_portalTarget.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Assert
            Assert.That(s_portalTarget.Q<Label>("a")?.text, Is.EqualTo("list"));
        }

        [Test]
        public void Given_AListInTheShownFallbackOfASuspenseUnderAProviderOfTheSameContextAsOneAboveTheHost_When_AScrollMountsARow_Then_TheRowReadsTheNearerProvider()
        {
            // Arrange
            s_neverRead = new VelvetTaskCompletionSource<int>();
            using var mounted = V.Mount(_root, V.Component(ThemeRootAroundASuspenseFallbackListHost, key: "root"));

            // Act
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 10f);

            // Assert
            Assert.That(_root.Q<Label>("a")?.text, Is.EqualTo("list"));
        }
    }
}
