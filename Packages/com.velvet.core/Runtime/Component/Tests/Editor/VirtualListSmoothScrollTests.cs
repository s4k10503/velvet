using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="VirtualListHandle.ScrollToItem"/> with <see cref="VirtualListScrollBehavior.Smooth"/>,
    /// react-window's <c>scrollToRow</c> with <c>behavior: "smooth"</c>, on a list in a panel whose animation
    /// phase a substitute clock drives.
    /// <list type="bullet">
    /// <item>Partway through, the list is between where it was and the item; once the animation has run, at the
    /// item.</item>
    /// <item>A target past the scroller's range is reached at the content's next layout after the animation
    /// ends, and a target an earlier call left for that layout does not end the animation.</item>
    /// <item>A scroll from anywhere else, or a further <c>ScrollToItem</c> that leaves the list where it is,
    /// ends the animation where it stands.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListSmoothScrollTests : ReconcilerTestFixture
    {
        private static readonly string[] Items = Enumerable.Range(0, 100).Select(i => "item-" + i).ToArray();

        private HeadlessEditorPanelHost _host;
        private double _now;

        public override void SetUp()
        {
            base.SetUp();
            _host = new HeadlessEditorPanelHost();
            _now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, () => _now);
            _host.Root.Add(Root);
        }

        public override void TearDown()
        {
            base.TearDown();
            _host.Dispose();
        }

        // A list of 100 items 50 tall with a 200-tall viewport, the size its GeometryChangedEvent records.
        private (ScrollView ScrollView, VirtualListHandle Handle) MountScrollable()
        {
            var listRef = new Ref<VirtualListHandle>();
            var node = V.VirtualList(Items, item => item, itemHeight: 50f, renderer: item => V.Label(text: item),
                overscan: 0, listRef: listRef);
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[] { node });
            var scrollView = (ScrollView)Root.ElementAt(0);
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Reconciler.Context.VirtualListControllers[scrollView], 200f);
            return (scrollView, listRef.Current);
        }

        private void Advance(double seconds)
        {
            _now += seconds;
            EditorPanelTestHelpers.DriveAnimationsOnce(_host.Panel);
        }

        // The scroller's range as the content's last layout left it, as VirtualListSizingAndScrollTests lays it out.
        private static void LayOutContent(ScrollView scrollView, float scrollableHeight)
        {
            scrollView.verticalScroller.highValue = scrollableHeight;
            using var evt = GeometryChangedEvent.GetPooled(Rect.zero, Rect.zero);
            scrollView.contentContainer.SimulateEvent(evt);
        }

        [Test]
        public void Given_ASmoothScroll_When_PartOfItsDurationHasPassed_Then_TheListIsOnItsWayToTheItem()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            handle.ScrollToItem(10, VirtualListAlign.Start, VirtualListScrollBehavior.Smooth);

            // Act
            Advance(0.1);

            // Assert
            Assert.That(scrollView.verticalScroller.value, Is.GreaterThan(0f).And.LessThan(500f));
        }

        [Test]
        public void Given_ASmoothScrollThatHasStepped_When_ItsDurationHasPassed_Then_TheListIsAtTheItem()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            handle.ScrollToItem(10, VirtualListAlign.Start, VirtualListScrollBehavior.Smooth);
            Advance(0.1);

            // Act
            Advance(0.5);

            // Assert
            Assert.That(scrollView.verticalScroller.value, Is.EqualTo(500f));
        }

        [Test]
        public void Given_ASmoothScrollPastTheScrollersRange_When_ItEndsAndTheContentIsNextLaidOut_Then_TheListReachesIt()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            scrollView.verticalScroller.highValue = 100f;
            handle.ScrollToItem(10, VirtualListAlign.Start, VirtualListScrollBehavior.Smooth);
            Advance(0.5);

            // Act
            LayOutContent(scrollView, 4800f);

            // Assert
            Assert.That(scrollView.verticalScroller.value, Is.EqualTo(500f));
        }

        [Test]
        public void Given_ATargetAnEarlierScrollLeftForTheNextLayout_When_ASmoothScrollRunsAcrossThatLayout_Then_ItReachesItsOwnItem()
        {
            // Arrange — the instant scroll to item 10 falls short of the range and leaves 500 for the next layout.
            var (scrollView, handle) = MountScrollable();
            scrollView.verticalScroller.highValue = 100f;
            handle.ScrollToItem(10, VirtualListAlign.Start);
            handle.ScrollToItem(1, VirtualListAlign.Start, VirtualListScrollBehavior.Smooth);

            // Act
            LayOutContent(scrollView, 4800f);
            Advance(0.5);

            // Assert
            Assert.That(scrollView.verticalScroller.value, Is.EqualTo(50f));
        }

        [Test]
        public void Given_ASmoothScrollInFlight_When_TheListIsScrolledFromElsewhere_Then_TheAnimationLeavesItThere()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            handle.ScrollToItem(10, VirtualListAlign.Start, VirtualListScrollBehavior.Smooth);
            Advance(0.1);

            // Act
            scrollView.verticalScroller.value = 20f;
            Advance(0.5);

            // Assert
            Assert.That(scrollView.verticalScroller.value, Is.EqualTo(20f));
        }

        [Test]
        public void Given_ASmoothScrollInFlight_When_AnInstantScrollLeavesTheListWhereItIs_Then_TheAnimationLeavesItThere()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            handle.ScrollToItem(10, VirtualListAlign.Start, VirtualListScrollBehavior.Smooth);
            Advance(0.1);
            var partway = scrollView.verticalScroller.value;

            // Act — the first row starting in the viewport is in view, so Auto scrolls nowhere.
            handle.ScrollToItem((int)Math.Ceiling(partway / 50f), VirtualListAlign.Auto);
            Advance(0.5);

            // Assert
            Assert.That(scrollView.verticalScroller.value, Is.EqualTo(partway));
        }

        [Test]
        public void Given_ASmoothScrollInFlight_When_TheListIsDisposed_Then_TheAnimationLeavesItThere()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            handle.ScrollToItem(10, VirtualListAlign.Start, VirtualListScrollBehavior.Smooth);
            Advance(0.1);
            var partway = scrollView.verticalScroller.value;

            // Act
            Reconciler.Context.VirtualListControllers[scrollView].Dispose();
            Advance(0.5);

            // Assert
            Assert.That(scrollView.verticalScroller.value, Is.EqualTo(partway));
        }
    }
}
