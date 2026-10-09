using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a <see cref="V.VirtualList{T}(IReadOnlyList{T}, Func{T, string}, Func{int, float}, Func{T, VNode}, bool, int, string, string, string, Ref{VirtualListHandle}, FiberEventBinding[])"/>
    /// whose items each take their own height, and the <see cref="VirtualListHandle"/> a list sets on its
    /// <c>listRef</c>, against react-window's <c>List</c>.
    /// <list type="bullet">
    /// <item>The spacer is as tall as the items' heights added up, and follows them when the list renders
    /// again, whether its heights or its number of items changed.</item>
    /// <item>The rendered range runs from the item at the scroll offset through the last item starting before the
    /// viewport's bottom edge — an item ending exactly at the top edge, or starting exactly at the bottom one,
    /// being outside it — and is the last item alone past the list's end, for a list of one height as well; its container sits where its first item starts, and
    /// each row takes its own item's height.</item>
    /// <item><c>ScrollToItem</c> places the item as react-window's <c>scrollToRow</c> does for each
    /// alignment, within the list's ends. Auto leaves an item already in view where it is, one whose start or
    /// end meets the viewport's included, and an item taller than the viewport counts as in view while the
    /// viewport lies within it. Smart is Auto for an item in view and Center otherwise. A target past the
    /// scroller's range is reached at the content's next layout, and only then, unless the list has unmounted
    /// by that layout. An index outside the items throws.</item>
    /// <item>The ref holds the handle while the list is mounted, lets go of it when the list unmounts or
    /// names another ref, holds the new list's when the list remounts under a new key, and the handle's
    /// element is the list's ScrollView.</item>
    /// <item>A null height function throws, and so does a null key selector beside a height function.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListSizingAndScrollTests : ReconcilerTestFixture
    {
        private static readonly string[] Items = Enumerable.Range(0, 100).Select(i => "item-" + i).ToArray();

        private VNode[] _tree;

        private static VirtualListNode VariableList(float[] heights, int overscan = 0)
            => V.VirtualList(
                items: Items.Take(heights.Length).ToArray(),
                keySelector: item => item,
                itemHeight: index => heights[index],
                renderer: item => V.Label(text: item),
                overscan: overscan);

        private static string RowTexts(ScrollView scrollView)
            => string.Join(",", scrollView.contentContainer.ElementAt(1).Children().Cast<Label>().Select(l => l.text));

        // A list of 100 items 50 tall, mounted with its handle on ref and a 200-tall viewport, the size its
        // GeometryChangedEvent records.
        private (ScrollView ScrollView, VirtualListHandle Handle) MountScrollable(
            float[] heights = null, Ref<VirtualListHandle> listRef = null)
        {
            listRef ??= new Ref<VirtualListHandle>();
            var node = heights == null
                ? V.VirtualList(Items, item => item, itemHeight: 50f, renderer: item => V.Label(text: item),
                    overscan: 0, listRef: listRef)
                : V.VirtualList(Items.Take(heights.Length).ToArray(), item => item, itemHeight: index => heights[index],
                    renderer: item => V.Label(text: item), overscan: 0, listRef: listRef);
            _tree = new VNode[] { node };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), _tree);
            var scrollView = (ScrollView)Root.ElementAt(0);
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Reconciler.Context.VirtualListControllers[scrollView], 200f);
            return (scrollView, listRef.Current);
        }

        private static float ScrolledTo(ScrollView scrollView, float from, Action scroll)
        {
            scrollView.verticalScroller.value = from;
            scroll();
            return scrollView.verticalScroller.value;
        }

        #region Heights by index

        [Test]
        public void Given_HeightsByIndex_When_Reconciled_Then_TheSpacerIsAsTallAsTheirSum()
        {
            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[] { VariableList(new[] { 10f, 20f, 30f, 40f }) });

            // Assert
            var spacer = ((ScrollView)Root.ElementAt(0)).contentContainer.ElementAt(0);
            Assert.That(spacer.style.height.value.value, Is.EqualTo(100f).Within(0.01f));
        }

        [Test]
        public void Given_HeightsByIndex_When_TheyChangeOnARender_Then_TheSpacerFollows()
        {
            // Arrange
            var first = new VNode[] { VariableList(new[] { 10f, 20f, 30f, 40f }) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), first);

            // Act
            Reconciler.Reconcile(Root, first, new VNode[] { VariableList(new[] { 20f, 40f, 60f, 80f }) });

            // Assert
            var spacer = ((ScrollView)Root.ElementAt(0)).contentContainer.ElementAt(0);
            Assert.That(spacer.style.height.value.value, Is.EqualTo(200f).Within(0.01f));
        }

        [Test]
        public void Given_HeightsByIndex_When_TheListGrowsByTwoItems_Then_TheSpacerCoversThem()
        {
            // Arrange
            var first = new VNode[] { VariableList(new[] { 10f, 20f, 30f, 40f }) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), first);

            // Act
            Reconciler.Reconcile(Root, first, new VNode[] { VariableList(new[] { 10f, 20f, 30f, 40f, 50f, 60f }) });

            // Assert
            var spacer = ((ScrollView)Root.ElementAt(0)).contentContainer.ElementAt(0);
            Assert.That(spacer.style.height.value.value, Is.EqualTo(210f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): interior variable-height ranges, including one short item, already render these rows; the separate exact-edge case asks the exclusive viewport contract.
        [TestCase(false)]
        [TestCase(true)]
        public void Given_HeightsByIndex_When_TheRangeIsRendered_Then_ItRunsFromTheItemAtTheOffsetToTheItemAtTheBottomEdge(
            bool singleItem)
        {
            // Arrange — the ordinary items end at 100, 110, 130, 160, 260 and 360; the single item ends at 40.
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            var heights = singleItem ? new[] { 40f } : new[] { 100f, 10f, 20f, 30f, 100f, 100f };
            using var controller = new FiberVirtualListController(
                scrollView, VariableList(heights), Reconciler);

            // Act — the ordinary viewport runs from 105 to 155; the short list's runs from 5 to 25.
            controller.UpdateVisibleRange(scrollY: singleItem ? 5f : 105f, viewportHeight: singleItem ? 20f : 50f);

            // Assert
            Assert.That(RowTexts(scrollView), Is.EqualTo(singleItem ? "item-0" : "item-1,item-2,item-3"));
        }

        [Test]
        public void Given_HeightsByIndex_When_TheViewportEdgesFallOnItemEnds_Then_OnlyTheItemBetweenThemRenders()
        {
            // Arrange — the items end at 50, 100, 150 and so on.
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(
                scrollView, VariableList(new[] { 50f, 50f, 50f, 50f, 50f, 50f }), Reconciler);

            // Act
            controller.UpdateVisibleRange(scrollY: 50f, viewportHeight: 50f);

            // Assert — item 0 ends at the top edge; item 2 starts at the bottom one.
            Assert.That(RowTexts(scrollView), Is.EqualTo("item-1"));
        }

        [Test]
        public void Given_HeightsByIndex_When_TheRangeIsRendered_Then_ItsContainerSitsWhereItsFirstItemStarts()
        {
            // Arrange
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(
                scrollView, VariableList(new[] { 100f, 10f, 20f, 30f, 100f, 100f }), Reconciler);

            // Act
            controller.UpdateVisibleRange(scrollY: 105f, viewportHeight: 50f);

            // Assert
            Assert.That(scrollView.contentContainer.ElementAt(1).style.top.value.value, Is.EqualTo(100f).Within(0.01f));
        }

        [Test]
        public void Given_HeightsByIndex_When_TheRangeIsRendered_Then_EachRowTakesItsOwnItemsHeight()
        {
            // Arrange
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(
                scrollView, VariableList(new[] { 100f, 10f, 20f, 30f, 100f, 100f }), Reconciler);

            // Act
            controller.UpdateVisibleRange(scrollY: 105f, viewportHeight: 50f);

            // Assert
            Assert.That(
                string.Join(",", scrollView.contentContainer.ElementAt(1).Children().Select(row => row.style.height.value.value)),
                Is.EqualTo("10,20,30"));
        }

        [Test]
        public void Given_HeightsByIndex_When_ScrolledPastTheListsEnd_Then_TheLastItemRenders()
        {
            // Arrange
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(
                scrollView, VariableList(new[] { 100f, 10f, 20f, 30f, 100f, 100f }), Reconciler);

            // Act
            controller.UpdateVisibleRange(scrollY: 1000f, viewportHeight: 50f);

            // Assert
            Assert.That(RowTexts(scrollView), Is.EqualTo("item-5"));
        }

        [Test]
        public void Given_OneHeight_When_ScrolledPastTheListsEnd_Then_TheLastItemRenders()
        {
            // Arrange
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(
                scrollView,
                V.VirtualList(Items, item => item, itemHeight: 50f, renderer: item => V.Label(text: item), overscan: 0),
                Reconciler);

            // Act
            controller.UpdateVisibleRange(scrollY: 10000f, viewportHeight: 200f);

            // Assert
            Assert.That(RowTexts(scrollView), Is.EqualTo("item-99"));
        }

        [Test]
        public void Given_HeightsByIndexAndANullKeySelector_When_TheListIsBuilt_Then_ItThrows()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => V.VirtualList(
                Items, keySelector: null, itemHeight: index => 50f, renderer: item => V.Label(text: item)));
        }

        [Test]
        public void Given_ANullHeightFunction_When_TheListIsBuilt_Then_ItThrows()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => V.VirtualList(
                Items, item => item, itemHeight: (Func<int, float>)null, renderer: item => V.Label(text: item)));
        }

        #endregion

        #region ScrollToItem

        [Test]
        public void Given_ThePublicSignatures_When_BoundAsMethodGroups_Then_TheyTakeTheEventsArrayLast()
        {
            // Arrange
            Func<IReadOnlyList<string>, Func<string, string>, float, Func<string, VNode>, int,
                string, string, string, Ref<VirtualListHandle>, FiberEventBinding[], VirtualListNode> fixedFactory = V.VirtualList;
            Func<IReadOnlyList<string>, Func<string, string>, Func<int, float>, Func<string, VNode>, int,
                string, string, string, Ref<VirtualListHandle>, FiberEventBinding[], VirtualListNode> variableFactory = V.VirtualList;
            var (_, handle) = MountScrollable();
            Action<int, VirtualListAlign> scroll = handle.ScrollToItem;

            // Act
            var parameterCounts = new[] { fixedFactory.Method.GetParameters().Length,
                variableFactory.Method.GetParameters().Length, scroll.Method.GetParameters().Length };

            // Assert
            Assert.That(parameterCounts, Is.EqualTo(new[] { 10, 10, 2 }));
        }

        [Test]
        public void Given_StartAlignment_When_ScrolledToAnItem_Then_ItsStartMeetsTheViewportsStart()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 0f, () => handle.ScrollToItem(10, VirtualListAlign.Start));

            // Assert
            Assert.That(offset, Is.EqualTo(500f));
        }

        [Test]
        public void Given_StartAlignment_When_ScrolledToAnItemNearTheEnd_Then_TheListsEndMeetsTheViewportsEnd()
        {
            // Arrange — the list is 5000 tall.
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 0f, () => handle.ScrollToItem(99, VirtualListAlign.Start));

            // Assert
            Assert.That(offset, Is.EqualTo(4800f));
        }

        [Test]
        public void Given_StartAlignment_When_ScrolledToTheFirstItem_Then_TheListIsAtItsTop()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 300f, () => handle.ScrollToItem(0, VirtualListAlign.Start));

            // Assert
            Assert.That(offset, Is.EqualTo(0f));
        }

        [Test]
        public void Given_EndAlignment_When_ScrolledToAnItem_Then_ItsEndMeetsTheViewportsEnd()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 0f, () => handle.ScrollToItem(10, VirtualListAlign.End));

            // Assert
            Assert.That(offset, Is.EqualTo(350f));
        }

        [Test]
        public void Given_CenterAlignment_When_ScrolledToAnItem_Then_ItsMiddleMeetsTheViewportsMiddle()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 0f, () => handle.ScrollToItem(10, VirtualListAlign.Center));

            // Assert
            Assert.That(offset, Is.EqualTo(425f));
        }

        [Test]
        public void Given_CenterAlignment_When_ScrolledToTheLastItem_Then_TheListsEndMeetsTheViewportsEnd()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 0f, () => handle.ScrollToItem(99, VirtualListAlign.Center));

            // Assert
            Assert.That(offset, Is.EqualTo(4800f));
        }

        [Test]
        public void Given_CenterAlignment_When_ScrolledToTheFirstItem_Then_TheListIsAtItsTop()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 300f, () => handle.ScrollToItem(0, VirtualListAlign.Center));

            // Assert
            Assert.That(offset, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AutoAlignment_When_TheItemIsBelowTheViewport_Then_ItsEndMeetsTheViewportsEnd()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 0f, () => handle.ScrollToItem(10));

            // Assert
            Assert.That(offset, Is.EqualTo(350f));
        }

        [Test]
        public void Given_AutoAlignment_When_TheItemIsAboveTheViewport_Then_ItsStartMeetsTheViewportsStart()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 1000f, () => handle.ScrollToItem(2));

            // Assert
            Assert.That(offset, Is.EqualTo(100f));
        }

        [Test]
        public void Given_AutoAlignment_When_TheItemIsInView_Then_TheListStays()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 100f, () => handle.ScrollToItem(3));

            // Assert
            Assert.That(offset, Is.EqualTo(100f));
        }

        [Test]
        public void Given_AutoAlignment_When_TheItemStartsAtTheViewportsStart_Then_TheListStays()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 150f, () => handle.ScrollToItem(3));

            // Assert
            Assert.That(offset, Is.EqualTo(150f));
        }

        [Test]
        public void Given_AutoAlignment_When_TheItemEndsAtTheViewportsEnd_Then_TheListStays()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 350f, () => handle.ScrollToItem(10));

            // Assert
            Assert.That(offset, Is.EqualTo(350f));
        }

        [Test]
        public void Given_SmartAlignment_When_TheItemIsInView_Then_TheListStays()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 100f, () => handle.ScrollToItem(3, VirtualListAlign.Smart));

            // Assert
            Assert.That(offset, Is.EqualTo(100f));
        }

        [Test]
        public void Given_SmartAlignment_When_TheItemStartsAtTheViewportsStart_Then_TheListStays()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 150f, () => handle.ScrollToItem(3, VirtualListAlign.Smart));

            // Assert
            Assert.That(offset, Is.EqualTo(150f));
        }

        [Test]
        public void Given_SmartAlignment_When_TheItemIsOutOfView_Then_ItsMiddleMeetsTheViewportsMiddle()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();

            // Act
            var offset = ScrolledTo(scrollView, 0f, () => handle.ScrollToItem(10, VirtualListAlign.Smart));

            // Assert
            Assert.That(offset, Is.EqualTo(425f));
        }

        // Item 1 runs from 100 to 400, so the 200-tall viewport lies within it from 100 to 200.
        [TestCase(100f, TestName = "Given_AnItemTallerThanTheViewport_When_TheViewportStartsAtItsStart_Then_AutoStays")]
        [TestCase(150f, TestName = "Given_AnItemTallerThanTheViewport_When_TheViewportLiesWithinIt_Then_AutoStays")]
        [TestCase(200f, TestName = "Given_AnItemTallerThanTheViewport_When_TheViewportEndsAtItsEnd_Then_AutoStays")]
        public void Given_AnItemTallerThanTheViewport_When_TheViewportLiesWithinIt_Then_AutoStays(float from)
        {
            // Arrange
            var (scrollView, handle) = MountScrollable(new[] { 100f, 300f, 100f, 100f, 100f, 100f });

            // Act
            var offset = ScrolledTo(scrollView, from, () => handle.ScrollToItem(1));

            // Assert
            Assert.That(offset, Is.EqualTo(from));
        }

        [Test]
        public void Given_AnItemTallerThanTheViewport_When_TheViewportIsAboveIt_Then_AutoBringsItsEndIntoView()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable(new[] { 100f, 300f, 100f, 100f, 100f, 100f });

            // Act
            var offset = ScrolledTo(scrollView, 0f, () => handle.ScrollToItem(1));

            // Assert
            Assert.That(offset, Is.EqualTo(200f));
        }

        [Test]
        public void Given_AnItemTallerThanTheViewport_When_TheViewportIsBelowIt_Then_AutoBringsItsStartIntoView()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable(new[] { 100f, 300f, 100f, 100f, 100f, 100f });

            // Act
            var offset = ScrolledTo(scrollView, 500f, () => handle.ScrollToItem(1));

            // Assert
            Assert.That(offset, Is.EqualTo(100f));
        }

        // The scroller's range as the content's last layout left it, short of an item a render has just added.
        private static void LayOutContent(ScrollView scrollView, float scrollableHeight)
        {
            scrollView.verticalScroller.highValue = scrollableHeight;
            using var evt = GeometryChangedEvent.GetPooled(Rect.zero, Rect.zero);
            scrollView.contentContainer.SimulateEvent(evt);
        }

        [Test]
        public void Given_ATargetPastTheScrollersRange_When_TheContentIsNextLaidOut_Then_TheListReachesIt()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            scrollView.verticalScroller.highValue = 100f;
            handle.ScrollToItem(10, VirtualListAlign.Start);

            // Act
            LayOutContent(scrollView, 4800f);

            // Assert
            Assert.That(scrollView.verticalScroller.value, Is.EqualTo(500f));
        }

        [Test]
        public void Given_ATargetReachedOnceTheContentWasLaidOut_When_ItIsLaidOutAgain_Then_TheListStaysWhereItWasScrolled()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            scrollView.verticalScroller.highValue = 100f;
            handle.ScrollToItem(10, VirtualListAlign.Start);
            LayOutContent(scrollView, 4800f);
            scrollView.verticalScroller.value = 0f;

            // Act
            LayOutContent(scrollView, 4800f);

            // Assert
            Assert.That(scrollView.verticalScroller.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ATargetWithinTheScrollersRange_When_TheContentIsNextLaidOut_Then_TheListStaysWhereItWasScrolled()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            scrollView.verticalScroller.highValue = 4800f;
            handle.ScrollToItem(10, VirtualListAlign.Start);
            scrollView.verticalScroller.value = 0f;

            // Act
            LayOutContent(scrollView, 4800f);

            // Assert
            Assert.That(scrollView.verticalScroller.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ATargetPastTheScrollersRange_When_TheListUnmountsBeforeTheContentIsLaidOut_Then_ItIsNotApplied()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            scrollView.verticalScroller.highValue = 100f;
            handle.ScrollToItem(10, VirtualListAlign.Start);
            Reconciler.Reconcile(Root, _tree, Array.Empty<VNode>());

            // Act
            LayOutContent(scrollView, 4800f);

            // Assert
            Assert.That(scrollView.verticalScroller.value, Is.EqualTo(100f));
        }

        [Test]
        public void Given_AnIndexPastTheLastItem_When_ScrolledTo_Then_ItThrowsNamingTheRange()
        {
            // Arrange
            var (_, handle) = MountScrollable();

            // Act + Assert
            Assert.That(() => handle.ScrollToItem(100),
                Throws.TypeOf<ArgumentOutOfRangeException>().With.Message.Contains("0 - 99"));
        }

        [Test]
        public void Given_ANegativeIndex_When_ScrolledTo_Then_ItThrows()
        {
            // Arrange
            var (_, handle) = MountScrollable();

            // Act + Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => handle.ScrollToItem(-1));
        }

        #endregion

        #region listRef

        [Test]
        public void Given_AListRef_When_TheListMounts_Then_ItHoldsAHandleOnTheListsScrollView()
        {
            // Arrange
            var listRef = new Ref<VirtualListHandle>();

            // Act
            var (scrollView, _) = MountScrollable(listRef: listRef);

            // Assert
            Assert.That(listRef.Current?.Element, Is.SameAs(scrollView));
        }

        [Test]
        public void Given_AMountedListsRef_When_TheListUnmounts_Then_ItLetsGoOfTheHandle()
        {
            // Arrange
            var listRef = new Ref<VirtualListHandle>();
            MountScrollable(listRef: listRef);

            // Act
            Reconciler.Reconcile(Root, _tree, Array.Empty<VNode>());

            // Assert
            Assert.That(listRef.Current, Is.Null);
        }

        [Test]
        public void Given_AMountedListsRef_When_TheListRemountsUnderANewKey_Then_ItHoldsTheNewListsHandle()
        {
            // Arrange
            var listRef = new Ref<VirtualListHandle>();
            MountScrollable(listRef: listRef);

            // Act — the new key mounts the new list before the old one is removed.
            Reconciler.Reconcile(Root, _tree, new VNode[]
            {
                V.VirtualList(Items, item => item, itemHeight: 50f, renderer: item => V.Label(text: item),
                    overscan: 0, key: "other", listRef: listRef),
            });

            // Assert
            Assert.That(listRef.Current?.Element, Is.SameAs(Root.ElementAt(0)));
        }

        [Test]
        public void Given_AMountedListsRef_When_TheListNamesAnotherRef_Then_OnlyTheOtherHoldsTheHandle()
        {
            // Arrange
            var first = new Ref<VirtualListHandle>();
            var second = new Ref<VirtualListHandle>();
            MountScrollable(listRef: first);

            // Act
            Reconciler.Reconcile(Root, _tree, new VNode[]
            {
                V.VirtualList(Items, item => item, itemHeight: 50f, renderer: item => V.Label(text: item),
                    overscan: 0, listRef: second),
            });

            // Assert
            Assert.That((first.Current == null, second.Current != null), Is.EqualTo((true, true)));
        }

        #endregion
    }
}
