using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a <see cref="V.VirtualList{T}(System.Collections.Generic.IReadOnlyList{T}, Func{T, string}, float, Func{T, VNode}, bool, int, string, string, string, Ref{VirtualListHandle}, FiberEventBinding[])"/>
    /// made with <c>horizontal: true</c>, FlashList's <c>horizontal</c>, on a panel nothing lays out, so that a
    /// scroller's change reaches the list.
    /// <list type="bullet">
    /// <item>The ScrollView scrolls sideways, the spacer is as wide as the items and takes no height, each row is
    /// as wide as its item, and the rows' container sits where its first item starts along the row.</item>
    /// <item>The horizontal scroller moves the range and the vertical one does not, and <c>ScrollToItem</c>
    /// scrolls the horizontal one.</item>
    /// <item>A list a render turns horizontal leaves its rows no height of the list's writing.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListHorizontalTests : ReconcilerTestFixture
    {
        private static readonly string[] Items = Enumerable.Range(0, 100).Select(i => "item-" + i).ToArray();

        private HeadlessEditorPanelHost _host;

        public override void SetUp()
        {
            base.SetUp();
            _host = new HeadlessEditorPanelHost();
            _host.Root.Add(Root);
        }

        public override void TearDown()
        {
            base.TearDown();
            _host.Dispose();
        }

        private static VirtualListNode List(
            bool horizontal = true, Ref<VirtualListHandle> listRef = null, bool emptyFirstRow = false)
            => V.VirtualList(Items, item => item, itemHeight: 50f,
                renderer: item => emptyFirstRow && item == Items[0] ? null : V.Label(text: item),
                overscan: 0, listRef: listRef, horizontal: horizontal);

        private static string RowTexts(ScrollView scrollView)
            => string.Join(",", scrollView.contentContainer.ElementAt(1).Children().Cast<Label>().Select(l => l.text));

        // Mounted through a reconcile, with the 200-wide viewport its GeometryChangedEvent would record.
        private ScrollView Mount(VirtualListNode node)
        {
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[] { node });
            var scrollView = (ScrollView)Root.ElementAt(0);
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Reconciler.Context.VirtualListControllers[scrollView], 200f);
            return scrollView;
        }

        [Test]
        public void Given_AHorizontalList_When_Reconciled_Then_ItsScrollViewScrollsSideways()
        {
            // Act
            var scrollView = Mount(List());

            // Assert
            Assert.That(scrollView.mode, Is.EqualTo(ScrollViewMode.Horizontal));
        }

        [Test]
        public void Given_AHorizontalList_When_Reconciled_Then_TheSpacerIsAsWideAsTheItemsAndTakesNoHeight()
        {
            // Act
            var spacer = Mount(List()).contentContainer.ElementAt(0);

            // Assert
            Assert.That((spacer.style.width.value.value, spacer.style.height.keyword),
                Is.EqualTo((5000f, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AHorizontalList_When_ARangeIsRendered_Then_EachRowIsAsWideAsItsItem()
        {
            // Arrange
            var scrollView = Mount(List());

            // Act
            scrollView.horizontalScroller.value = 100f;

            // Assert
            var row = scrollView.contentContainer.ElementAt(1).ElementAt(0);
            Assert.That((row.style.width.value.value, row.style.height.keyword), Is.EqualTo((50f, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AHorizontalList_When_ARangeIsRendered_Then_ItsContainerSitsWhereItsFirstItemStartsAlongTheRow()
        {
            // Arrange
            var scrollView = Mount(List());

            // Act
            scrollView.horizontalScroller.value = 100f;

            // Assert
            var container = scrollView.contentContainer.ElementAt(1).style;
            Assert.That((container.left.value.value, container.flexDirection.value), Is.EqualTo((100f, FlexDirection.Row)));
        }

        [Test]
        public void Given_AHorizontalList_When_TheHorizontalScrollerMoves_Then_TheRangeFollowsIt()
        {
            // Arrange
            var scrollView = Mount(List());

            // Act
            scrollView.horizontalScroller.value = 500f;

            // Assert
            Assert.That(RowTexts(scrollView), Is.EqualTo("item-10,item-11,item-12,item-13"));
        }

        [Test]
        public void Given_AHorizontalList_When_TheVerticalScrollerMoves_Then_TheRangeStaysPut()
        {
            // Arrange
            var scrollView = Mount(List());
            scrollView.horizontalScroller.value = 500f;

            // Act
            scrollView.verticalScroller.value = 1000f;

            // Assert
            Assert.That(RowTexts(scrollView), Is.EqualTo("item-10,item-11,item-12,item-13"));
        }

        [Test]
        public void Given_AHorizontalList_When_ScrolledToAnItem_Then_TheHorizontalScrollerTakesItsOffset()
        {
            // Arrange
            var listRef = new Ref<VirtualListHandle>();
            var scrollView = Mount(List(listRef: listRef));

            // Act
            listRef.Current.ScrollToItem(10, VirtualListAlign.Start);

            // Assert
            Assert.That((scrollView.horizontalScroller.value, scrollView.verticalScroller.value),
                Is.EqualTo((500f, 0f)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AVerticalListShowingRows_When_ARenderTurnsItHorizontal_Then_TheRowsKeepNoHeightOfItsWriting(
            bool emptyFirstRow)
        {
            // Arrange
            var vertical = new VNode[] { List(horizontal: false, emptyFirstRow: emptyFirstRow) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), vertical);
            var scrollView = (ScrollView)Root.ElementAt(0);
            var controller = Reconciler.Context.VirtualListControllers[scrollView];
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            var row = scrollView.contentContainer.ElementAt(1).ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, vertical, new VNode[] { List(emptyFirstRow: emptyFirstRow) });

            // Assert — the row is read as still mounted, since a row the render replaced holds no height either.
            Assert.That((row.parent == scrollView.contentContainer.ElementAt(1), row.style.height.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }
    }
}
