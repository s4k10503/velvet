using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a <see cref="V.VirtualList{T}"/> range rendered from a scroll, outside any reconcile pass,
    /// places before the scroll returns: the children of each row that is a <c>V.Portal</c> mount in its target, in
    /// the rows' order, a Component row between them included, and in the order a pass mounts them where a Component
    /// row renders a Portal of its own. A Portal a range that failed had built mounts nothing when the range renders
    /// again. A child of such a Portal that suspends with no Suspense above the list — a Portal row's own, or one a row
    /// holds — leaves the list's host waiting on the read, outside a transition, rather than leaving the scroll, alone
    /// in the range or ahead of a Component row, and the rows that scroll built stay in the list. A range a scroll asks
    /// for while a root renders or commits, its own root's included, renders once that has ended, before control
    /// returns, and asked for more often than the nested-update limit throws nothing; a ScrollToItem made there still
    /// reaches its item once the content is laid out. What a range's rows mount, the updates their layout effects make
    /// and the fallback a row's boundary shows for an error one throws render before the scroll that rendered them
    /// returns. A list its host renders again places its new rows inside that render, where the host's layout effect
    /// reads them.
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListScrollPlacementTests
    {
        private VisualElement _root;
        private static VisualElement s_target;
        private static int s_refusals;
        private static VelvetTaskCompletionSource<string> s_resource;
        private static ComponentFiber s_listHost;
        // The suspending list's items, "c" a Component row and any other a Portal round PendingRead, held by a row of its
        // own where s_portalHeld.
        private static string[] s_suspendingRows;
        private static bool s_portalHeld;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            (s_target, s_refusals, s_resource, s_listHost, s_suspendingRows, s_portalHeld) =
                (new VisualElement(), 0, new VelvetTaskCompletionSource<string>(), null, new[] { "a" }, false);
            (s_otherList, s_written, s_rowRead, s_renderedInside, s_rowRenders, s_scrollTargets) =
                (null, null, null, null, 0, new[] { 49, 50 });
        }

        [Component]
        private static VNode ComponentRow(string item) => V.Div(name: item);

        [Component(Compiler = false)]
        private static VNode PortalComponentRow(string item) => V.Portal(s_target, new VNode[] { V.Label(text: item, name: item) });

        [Component(Compiler = false)]
        private static VNode PendingRead() => V.Label(name: "pending", text: Hooks.Use(_ => s_resource.Task));

        [Component(Compiler = false)]
        private static VNode SuspendingPortalListHost()
        {
            s_listHost = FiberAmbientStack.Current;
            return V.VirtualList(
                items: s_suspendingRows,
                keySelector: item => item,
                itemHeight: 10f,
                renderer: item => item == "c" ? V.Component(ComponentRow, item, key: item)
                    : s_portalHeld ? V.Div(name: item, children: new VNode[] { SuspendingPortal() })
                    : SuspendingPortal(),
                overscan: 0);
        }

        private static VNode SuspendingPortal() => V.Portal(s_target, new VNode[] { V.Component(PendingRead) });

        private ScrollView List => _root.Q<ScrollView>();

        [Test]
        public void Given_APortalRowAheadOfAComponentRowThatRendersAPortalToTheSameTarget_When_AScrollBringsThemIntoTheRange_Then_TheirChildrenMountInTheRowsOrder()
        {
            // Arrange — the list renders no range until a scroll asks for one.
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: new[] { "b", "c" },
                    keySelector: item => item,
                    itemHeight: 10f,
                    renderer: item => item == "c"
                        ? V.Component(PortalComponentRow, item, key: item)
                        : V.Portal(s_target, new VNode[] { V.Label(text: item, name: item) }),
                    overscan: 0),
            }));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[List];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(string.Join(",", s_target.Children().Select(child => child.name)), Is.EqualTo("b,c"));
        }

        [Test]
        public void Given_ARangeThatFailedAfterBuildingARowHoldingAPortal_When_TheRangeIsRenderedAgain_Then_ThePortalsChildrenMountOnce()
        {
            // Arrange — the renderer refuses "b" once, after "a" has been built around a Portal.
            s_refusals = 1;
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: new[] { "a", "b" },
                    keySelector: item => item,
                    itemHeight: 10f,
                    renderer: item => item == "b" && s_refusals-- > 0
                        ? throw new InvalidOperationException("row refused")
                        : item == "a"
                            ? V.Div(name: item, children: new VNode[] { V.Portal(s_target, new VNode[] { V.Label(text: item) }) })
                            : V.Div(name: item),
                    overscan: 0),
            }));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[List];
            try
            {
                controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            }
            catch (InvalidOperationException)
            {
            }

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(s_target.childCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_AVirtualListRowThatIsAPortalWhoseChildSuspendsWithNoSuspenseAbove_When_AScrollBringsItIntoTheRange_Then_TheListsHostWaitsOnTheRead()
        {
            // Arrange — the list renders no range until a scroll asks for one.
            using var mounted = V.Mount(_root, V.Component(SuspendingPortalListHost, key: "host"));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[List];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(s_listHost.SuspendedOn, Is.Not.Null);
        }

        [Test]
        public void Given_AVirtualListRowThatIsAPortalWhoseChildSuspendsWithNoSuspenseAbove_When_AScrollBringsItIntoTheRange_Then_TheHostWaitsOutsideATransition()
        {
            // Arrange — the list renders no range until a scroll asks for one.
            using var mounted = V.Mount(_root, V.Component(SuspendingPortalListHost, key: "host"));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[List];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert — null where the host waits on nothing, which false never equals.
            bool? inTransition = s_listHost.SuspendedOn != null ? s_listHost.SuspendedInTransition : null;
            Assert.That(inTransition, Is.EqualTo(false));
        }

        [Test]
        public void Given_APortalRowWhoseChildSuspendsAheadOfAComponentRow_When_AScrollBringsBothIntoTheRange_Then_TheListsHostWaitsOnTheRead()
        {
            // Arrange — the list renders no range until a scroll asks for one; "c" is a Component row, which mounts
            // through a render of its own.
            s_suspendingRows = new[] { "b", "c" };
            using var mounted = V.Mount(_root, V.Component(SuspendingPortalListHost, key: "host"));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[List];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(s_listHost.SuspendedOn, Is.Not.Null);
        }

        [Test]
        public void Given_ARowHoldingAPortalWhoseChildSuspendsAheadOfAComponentRow_When_AScrollBringsBothIntoTheRange_Then_TheListsHostWaitsOnTheRead()
        {
            // Arrange — the list renders no range until a scroll asks for one; "c" is a Component row, which mounts
            // through a render of its own.
            (s_suspendingRows, s_portalHeld) = (new[] { "b", "c" }, true);
            using var mounted = V.Mount(_root, V.Component(SuspendingPortalListHost, key: "host"));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[List];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(s_listHost.SuspendedOn, Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base places both rows, never mounting the Portal outside a pass.
        // Mounting it there, and so its child's suspend, must not cost the range the rows it built.
        [Test]
        public void Given_APortalRowWhoseChildSuspendsAheadOfAComponentRow_When_AScrollBringsBothIntoTheRange_Then_BothRowsAreInTheList()
        {
            // Arrange — the list renders no range until a scroll asks for one.
            s_suspendingRows = new[] { "b", "c" };
            using var mounted = V.Mount(_root, V.Component(SuspendingPortalListHost, key: "host"));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[List];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(LogicalChildSlots.Count(List.contentContainer.ElementAt(1)), Is.EqualTo(2));
        }

        [Test]
        public void Given_VirtualListRowsThatArePortalsAroundAComponentRow_When_AScrollBringsThemIntoTheRange_Then_TheirChildrenMountInTheTarget()
        {
            // Arrange — the list renders no range until a scroll asks for one. "c" is a Component, whose render
            // outside a pass is a top-level pass of its own.
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: new[] { "a", "b", "c", "d" },
                    keySelector: item => item,
                    itemHeight: 10f,
                    renderer: item => item switch
                    {
                        "b" or "d" => V.Portal(s_target, new VNode[] { V.Label(text: item, name: item) }),
                        "c" => V.Component(ComponentRow, item, key: item),
                        _ => V.Div(name: item),
                    },
                    overscan: 0),
            }));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[List];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(string.Join(",", s_target.Children().Select(child => child.name)), Is.EqualTo("b,d"));
        }

        private static Ref<VirtualListHandle> s_otherList;
        private static string s_written;
        private static string s_rowRead;
        private static string s_renderedInside;
        private static int s_rowRenders;
        private static int[] s_scrollTargets;

        // A list of another root, 100 items 50 tall, mounted on a panel so that a scroller's change reaches it, with the
        // 200-tall viewport its GeometryChangedEvent would record. Item 50's row records what it read where it rendered.
        private static MountedTree MountAnotherRootsList(HeadlessEditorPanelHost host)
        {
            s_otherList = new Ref<VirtualListHandle>();
            var items = Enumerable.Range(0, 100).Select(i => "item-" + i).ToArray();
            var mounted = V.Mount(host.Root, V.VirtualList(items, item => item, itemHeight: 50f, renderer: item =>
            {
                if (item == "item-50")
                {
                    s_rowRenders++;
                    s_rowRead = s_written;
                }
                return V.Label(text: item);
            }, overscan: 0, listRef: s_otherList));
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(mounted.Root.Reconciler.Context.VirtualListControllers[host.Root.Q<ScrollView>()], 200f);
            s_written = "before the owner's commit";
            return mounted;
        }

        // Scrolls the other root's list to each target, items 49 and 50 unless a case sets others, the last one item 50,
        // from inside its own walk, and writes in its layout effect.
        [Component]
        private static VNode ScrollsAnotherRootsList()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_written = "written by the owner's commit";
                return null;
            }), Array.Empty<object>());
            return V.ScrollView(onCreated: _ =>
            {
                foreach (var target in s_scrollTargets) s_otherList.Current.ScrollToItem(target, VirtualListAlign.Start);
                s_renderedInside = "renders:" + s_rowRenders;
            });
        }

        [Test]
        public void Given_AnotherRootsListScrolledFromAWalk_When_TheScrollReturns_Then_ItsRowHasNotRendered()
        {
            // Arrange
            using var host = new HeadlessEditorPanelHost();
            using var other = MountAnotherRootsList(host);

            // Act
            using var owner = V.Mount(new VisualElement(), V.Component(ScrollsAnotherRootsList));

            // Assert
            Assert.That(s_renderedInside, Is.EqualTo("renders:0"));
        }

        [Test]
        public void Given_AnotherRootsListScrolledFromAWalk_When_TheOwnersMountReturns_Then_ItsRowHasRenderedReadingTheOwnersCommit()
        {
            // Arrange
            using var host = new HeadlessEditorPanelHost();
            using var other = MountAnotherRootsList(host);

            // Act
            using var owner = V.Mount(new VisualElement(), V.Component(ScrollsAnotherRootsList));

            // Assert — null where the row never rendered.
            Assert.That(s_rowRead, Is.EqualTo("written by the owner's commit"));
        }

        private static string s_rowsSeenInLayout;
        private static StateUpdater<int> s_setVersion;
        private static ScrollView s_ownList;

        // Renders a list whose items change with its version, and reads the list's rows in its layout effect.
        [Component]
        private static VNode ListHostReadingItsRows()
        {
            var (version, setVersion) = Hooks.UseState(0);
            s_setVersion = setVersion;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_rowsSeenInLayout = s_ownList == null
                    ? null
                    : string.Join(",", s_ownList.contentContainer.ElementAt(1).Children().Cast<Label>().Select(row => row.text));
                return null;
            }), new object[] { version });
            return V.VirtualList(new[] { "a" + version, "b" + version }, item => item, itemHeight: 50f,
                renderer: item => V.Label(text: item), overscan: 0);
        }

        // GREEN_ON_BASE(characterization): the base renders the range of a list its host renders again inside that render.
        // Holding a range asked for from outside that render leaves this one as it was.
        [Test]
        public void Given_AListItsHostRendersAgainWithAViewport_When_TheHostsLayoutEffectRuns_Then_ItReadsTheNewRows()
        {
            // Arrange
            s_ownList = null;
            using var mounted = V.Mount(_root, V.Component(ListHostReadingItsRows, key: "host"));
            s_ownList = List;
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(mounted.Root.Reconciler.Context.VirtualListControllers[List], 200f);
            s_setVersion.Invoke(1);

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_rowsSeenInLayout, Is.EqualTo("a1,b1"));
        }

        // GREEN_ON_BASE(characterization): the base renders each scroll's range where the scroll is made, so nothing
        // waits to be counted against the nested-update limit.
        [Test]
        public void Given_AnotherRootsListScrolledMoreTimesThanTheNestedUpdateLimitFromAWalk_When_TheOwnersMountReturns_Then_NothingIsThrown()
        {
            // Arrange — sixty scrolls, each to a further item, then item 50.
            using var host = new HeadlessEditorPanelHost();
            using var other = MountAnotherRootsList(host);
            s_scrollTargets = Enumerable.Range(1, 60).Append(50).ToArray();
            Exception thrown = null;

            // Act
            try
            {
                using var owner = V.Mount(new VisualElement(), V.Component(ScrollsAnotherRootsList));
            }
            catch (Exception exception)
            {
                thrown = exception;
            }

            // Assert
            Assert.That(thrown, Is.Null);
        }

        private static void SetViewport(MountedTree mounted, ScrollView scrollView, float height)
            => typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(mounted.Root.Reconciler.Context.VirtualListControllers[scrollView], height);

        private static FiberVirtualListController ControllerOf(MountedTree mounted, ScrollView scrollView)
            => mounted.Root.Reconciler.Context.VirtualListControllers[scrollView];

        private static readonly string[] HundredItems = Enumerable.Range(0, 100).Select(i => "item-" + i).ToArray();

        private static Ref<VirtualListHandle> s_ownListRef;
        private static StateUpdater<int> s_setCount;

        // Appends items as its count grows, and scrolls the last of them to the viewport's end in its layout effect.
        [Component]
        private static VNode AppendsAndScrollsToItsLastItem()
        {
            var (count, setCount) = Hooks.UseState(3);
            s_setCount = setCount;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (count > 3) s_ownListRef.Current.ScrollToItem(count - 1, VirtualListAlign.End);
                return null;
            }), new object[] { count });
            return V.VirtualList(HundredItems.Take(count).ToArray(), item => item, itemHeight: 50f,
                renderer: item => V.Label(text: item), overscan: 0, listRef: s_ownListRef);
        }

        // GREEN_ON_BASE(characterization): the base runs the scroll's callback where the scroller changes, target kept.
        // Holding the whole callback instead would replay it as a scroll the list did not make, cancelling the target.
        [Test]
        public void Given_AHostThatAppendsAndScrollsToItsLastItemInALayoutEffect_When_TheContentIsNextLaidOut_Then_TheListReachesThatItem()
        {
            // Arrange — on a panel, so that the scroller's change reaches the list. The scroller's range is the one the
            // content's last layout left, short of the ten items the drain's render appends.
            using var host = new HeadlessEditorPanelHost();
            s_ownListRef = new Ref<VirtualListHandle>();
            using var mounted = V.Mount(host.Root, V.Component(AppendsAndScrollsToItsLastItem));
            var scrollView = host.Root.Q<ScrollView>();
            SetViewport(mounted, scrollView, 200f);
            scrollView.verticalScroller.highValue = 100f;
            s_setCount.Invoke(10);
            mounted.DrainForTest();

            // Act
            scrollView.verticalScroller.highValue = 300f;
            using var evt = GeometryChangedEvent.GetPooled(UnityEngine.Rect.zero, UnityEngine.Rect.zero);
            scrollView.contentContainer.SimulateEvent(evt);

            // Assert
            Assert.That(scrollView.verticalScroller.value, Is.EqualTo(300f));
        }

        // Sets its own state once from its layout effect, as a row measuring itself does.
        [Component]
        private static VNode SetsItsStateOnMount(string item)
        {
            var (tick, setTick) = Hooks.UseState(0);
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (tick == 0) setTick.Invoke(1);
                return null;
            }), Array.Empty<object>());
            return V.Label(name: item, text: item + ":" + tick);
        }

        // React renders an update a layout effect makes where the commit ends, whatever else is waiting then.
        [Test]
        public void Given_ARowWhoseLayoutEffectSetsItsState_When_AScrollRendersTheRow_Then_TheUpdateHasRenderedWhenTheScrollReturns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.VirtualList(HundredItems, item => item, itemHeight: 50f,
                renderer: item => V.Component<string>(SetsItsStateOnMount, item, key: item), overscan: 0));
            var scrollView = _root.Q<ScrollView>();
            SetViewport(mounted, scrollView, 200f);

            // Act
            ControllerOf(mounted, scrollView).UpdateVisibleRange(2500f, 200f);

            // Assert — null where the row did not render
            Assert.That(_root.Q<Label>("item-50")?.text, Is.EqualTo("item-50:1"));
        }

        [Component]
        private static VNode ThrowsFromItsLayoutEffect(string item)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() => throw new InvalidOperationException("row " + item)),
                Array.Empty<object>());
            return V.Label(name: item, text: item);
        }

        // GREEN_ON_BASE(characterization): the base rendered the fallback where the range's commit ended, as this does.
        // FiberEffects.CommitStrandedLayoutWork renders the errors a commit captured once that commit is over, and the
        // range calls it as it returns.
        [Test]
        public void Given_ARowWhoseLayoutEffectThrowsIntoItsOwnBoundary_When_AScrollRendersTheRow_Then_TheFallbackIsShownWhenTheScrollReturns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.VirtualList(HundredItems, item => item, itemHeight: 50f,
                renderer: item => V.ErrorBoundary(_ => V.Label(name: "fallback-" + item, text: "fallback"),
                    new VNode[] { V.Component<string>(ThrowsFromItsLayoutEffect, item, key: "row") }, key: item),
                overscan: 0), CaughtErrors.Unlogged);
            var scrollView = _root.Q<ScrollView>();
            SetViewport(mounted, scrollView, 200f);

            // Act
            ControllerOf(mounted, scrollView).UpdateVisibleRange(2500f, 200f);

            // Assert
            Assert.That(_root.Q<Label>("fallback-item-50"), Is.Not.Null);
        }

        private static readonly System.Collections.Generic.List<MountedTree> s_thumbs = new();

        [TearDown]
        public void DisposeTheThumbs()
        {
            foreach (var thumb in s_thumbs) thumb.Dispose();
            s_thumbs.Clear();
        }

        // GREEN_ON_BASE(characterization): the base rendered the root where the row's ref mounted it, inside the range.
        [Test]
        public void Given_ARowRefThatMountsARoot_When_TheListScrollsOutsideEveryEntry_Then_TheRootHasRenderedWhenTheScrollReturns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.VirtualList(HundredItems, item => item, itemHeight: 50f,
                renderer: item => V.Div(name: item, refCallback: element =>
                {
                    s_thumbs.Add(V.Mount(element, V.Label(text: "thumb")));
                    return () => { };
                }), overscan: 0));
            var scrollView = _root.Q<ScrollView>();
            SetViewport(mounted, scrollView, 200f);

            // Act
            ControllerOf(mounted, scrollView).UpdateVisibleRange(2500f, 200f);

            // Assert — -1 where the row did not render
            Assert.That(_root.Q<VisualElement>("item-50")?.childCount ?? -1, Is.EqualTo(1));
        }

        private static StateUpdater<bool> s_setScrolled;
        private static string s_rowAfterTheScroll;

        // Scrolls its own list to item 50 from its layout effect once asked, and records whether that row was in the list
        // when ScrollToItem returned.
        [Component]
        private static VNode ScrollsItsOwnList()
        {
            var (scrolled, setScrolled) = Hooks.UseState(false);
            s_setScrolled = setScrolled;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (!scrolled) return null;
                s_ownListRef.Current.ScrollToItem(50, VirtualListAlign.Start);
                s_rowAfterTheScroll = s_ownListRef.Current.Element.Q<Label>("item-50") == null ? "absent" : "present";
                return null;
            }), new object[] { scrolled });
            return V.VirtualList(HundredItems, item => item, itemHeight: 50f,
                renderer: item => V.Label(name: item, text: item), overscan: 0, listRef: s_ownListRef);
        }

        private MountedTree MountAListThatScrollsItself(HeadlessEditorPanelHost host)
        {
            s_ownListRef = new Ref<VirtualListHandle>();
            s_rowAfterTheScroll = null;
            var mounted = V.Mount(host.Root, V.Component(ScrollsItsOwnList));
            var scrollView = host.Root.Q<ScrollView>();
            SetViewport(mounted, scrollView, 200f);
            scrollView.verticalScroller.highValue = 4800f;
            s_setScrolled.Invoke(true);
            return mounted;
        }

        // React's list renders a scrollToItem made in a layout effect as a state update, once that commit has ended.
        [Test]
        public void Given_AListsOwnLayoutEffectThatScrollsIt_When_ScrollToItemReturns_Then_TheNewRowsHaveNotRendered()
        {
            // Arrange
            using var host = new HeadlessEditorPanelHost();
            using var mounted = MountAListThatScrollsItself(host);

            // Act
            mounted.DrainForTest();

            // Assert
            Assert.That(s_rowAfterTheScroll, Is.EqualTo("absent"));
        }

        // GREEN_ON_BASE(characterization): the base rendered the range where the scroll was made, inside the drain.
        // The row is in the list by the time the drain returns either way.
        [Test]
        public void Given_AListsOwnLayoutEffectThatScrollsIt_When_TheDrainReturns_Then_TheNewRowsHaveRendered()
        {
            // Arrange
            using var host = new HeadlessEditorPanelHost();
            using var mounted = MountAListThatScrollsItself(host);

            // Act
            mounted.DrainForTest();

            // Assert
            Assert.That(host.Root.Q<Label>("item-50"), Is.Not.Null);
        }

        private static readonly System.Collections.Generic.List<string> s_rangeLog = new();

        // Logs each item the list renders, and logs once item-10's row, as it is created, has scrolled the list to item 50.
        private static MountedTree MountAListWhoseRowScrollsIt(HeadlessEditorPanelHost host)
        {
            s_ownListRef = new Ref<VirtualListHandle>();
            var mounted = V.Mount(host.Root, V.VirtualList(HundredItems, item => item, itemHeight: 50f, renderer: item =>
            {
                s_rangeLog.Add(item);
                return V.TextField(name: item, onCreated: item != "item-10" ? null : _ =>
                {
                    s_ownListRef.Current.ScrollToItem(50, VirtualListAlign.Start);
                    s_rangeLog.Add("scrolled");
                });
            }, overscan: 0, listRef: s_ownListRef));
            var scrollView = host.Root.Q<ScrollView>();
            SetViewport(mounted, scrollView, 200f);
            scrollView.verticalScroller.highValue = 4800f;
            s_rangeLog.Clear();
            return mounted;
        }

        [Test]
        public void Given_ARowWhoseOnCreatedScrollsItsOwnList_When_TheRangeRenders_Then_TheSecondRangeRendersAfterTheFirstEnds()
        {
            // Arrange — on a panel, so that the scroller's change reaches the list
            using var host = new HeadlessEditorPanelHost();
            using var mounted = MountAListWhoseRowScrollsIt(host);

            // Act
            ControllerOf(mounted, host.Root.Q<ScrollView>()).UpdateVisibleRange(500f, 200f);

            // Assert
            Assert.That(string.Join(",", s_rangeLog),
                Is.EqualTo("item-10,scrolled,item-11,item-12,item-13,item-50,item-51,item-52,item-53"));
        }
    }

    /// <summary>
    /// Specifies where the ring band of a <see cref="V.VirtualList{T}"/> row stands once a range rendered from a scroll
    /// has placed the rows again: directly after its row, for a row in its slot and for one a <c>z-*</c> class
    /// relocated.
    /// </summary>
    /// <remarks>
    /// A row put back in the list has its band put beside it by its attach callback, which only a panel raises, so the
    /// fixture takes a real one. <c>ring-2</c> is realised from C#, so no stylesheet is attached.
    /// </remarks>
    [TestFixture]
    internal sealed class VirtualListRowBandPanelTests : PanelTestBase
    {
        private VisualElement MountRows(string bClasses)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.VirtualList(
                items: new[] { "a", "b", "c" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: item => V.Div(name: item, className: item == "b" ? $"ring-2 {bClasses}" : "ring-2"),
                overscan: 0,
                name: "vlist",
                className: "h-[200px]"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            return _window.rootVisualElement.Q<ScrollView>("vlist").contentContainer.ElementAt(1);
        }

        private void ScrollToTheFirstTwoRows()
        {
            var scrollView = _window.rootVisualElement.Q<ScrollView>("vlist");
            _mounted.Root.Reconciler.Context.VirtualListControllers[scrollView].UpdateVisibleRange(scrollY: 0f, viewportHeight: 15f);
            ForcePanelUpdate(_window.rootVisualElement.panel);
        }

        // Physical index of the row's ring band less the row's own: 1 where the band stands directly after it.
        private int BandOffset(VisualElement rows, string name)
        {
            var row = _window.rootVisualElement.Q<VisualElement>(name);
            return row == null || !_mounted.Root.Reconciler.Context.RingBindings.TryGetValue(row, out var ring)
                ? int.MinValue
                : rows.IndexOf(ring.Overlay) - rows.IndexOf(row);
        }

        // GREEN_ON_BASE(characterization): the base empties the row container and adds the rows back at its end.
        // Each band follows its row there, since the row's attach puts the band directly after it; inserting each
        // row back at its logical slot instead puts it ahead of the band the row before it left there.
        [Test]
        public void Given_RingedVirtualListRows_When_AScrollRendersTheRangeAgain_Then_TheFirstRowsBandStandsDirectlyAfterIt()
        {
            // Arrange
            var rows = MountRows("");

            // Act
            ScrollToTheFirstTwoRows();

            // Assert
            Assert.That(BandOffset(rows, "a"), Is.EqualTo(1));
        }

        [Test]
        public void Given_ARingedVirtualListRowWithAZIndex_When_AScrollRendersTheRangeAgain_Then_ItsBandStandsDirectlyAfterIt()
        {
            // Arrange
            var rows = MountRows("relative z-10");

            // Act
            ScrollToTheFirstTwoRows();

            // Assert
            Assert.That(BandOffset(rows, "b"), Is.EqualTo(1));
        }
    }
}
