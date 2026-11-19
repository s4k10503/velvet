using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;
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
        private VNode[] _tree;

        internal enum ScrollInterruption
        {
            BetweenTicks,
            DuringTick,
            BeforeFirstTick,
            AxisFlip,
        }

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
            _tree = new VNode[] { node };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), _tree);
            var scrollView = (ScrollView)Root.ElementAt(0);
            SetViewportLength(scrollView);
            return (scrollView, listRef.Current);
        }

        // The headless panel has no layout phase; its next geometry event would measure this length.
        private void SetViewportLength(ScrollView scrollView)
        {
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Reconciler.Context.VirtualListControllers[scrollView], 200f);
        }

        private void TurnHorizontal()
        {
            var next = new VNode[]
            {
                V.VirtualList(Items, item => item, itemHeight: 50f, renderer: item => V.Label(text: item),
                    horizontal: true, overscan: 0),
            };
            Reconciler.Reconcile(Root, _tree, next);
            _tree = next;
        }

        private void Advance(double seconds)
        {
            _now += seconds;
            EditorPanelTestHelpers.DriveAnimationsOnce(_host.Panel);
        }

        // The scroller's range as the content's last layout left it, as VirtualListSizingAndScrollTests lays it out.
        private static void LayOutContent(ScrollView scrollView, float scrollableHeight, bool horizontal = false)
        {
            var scroller = horizontal ? scrollView.horizontalScroller : scrollView.verticalScroller;
            scroller.highValue = scrollableHeight;
            using var evt = GeometryChangedEvent.GetPooled(Rect.zero, Rect.zero);
            scrollView.contentContainer.SimulateEvent(evt);
        }

        private object ControllerState(ScrollView scrollView, string field)
            => typeof(FiberVirtualListController).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(Reconciler.Context.VirtualListControllers[scrollView]);

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

        [TestCase(false)]
        [TestCase(true)]
        public void Given_ASmoothScrollThatHasStepped_When_ItsDurationHasPassed_Then_TheListIsAtTheItem(
            bool flipAndClamp)
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            if (flipAndClamp)
            {
                scrollView.verticalScroller.highValue = 100f;
                scrollView.verticalScroller.value = 20f;
                scrollView.horizontalScroller.highValue = 100f;
                scrollView.horizontalScroller.value = 100f;
                TurnHorizontal();
                SetViewportLength(scrollView);
            }
            var scroller = flipAndClamp ? scrollView.horizontalScroller : scrollView.verticalScroller;
            handle.ScrollToItem(10, VirtualListAlign.Start, VirtualListScrollBehavior.Smooth);
            if (flipAndClamp) scroller.highValue = 50f;
            var clampedBeforeTick = scroller.value;
            Advance(0.1);

            // Act
            Advance(0.5);
            if (flipAndClamp) LayOutContent(scrollView, 4800f, horizontal: true);

            // Assert
            Assert.That((!flipAndClamp || clampedBeforeTick == 50f, scroller.value), Is.EqualTo((true, 500f)));
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

        [TestCase(ScrollInterruption.BetweenTicks)]
        [TestCase(ScrollInterruption.DuringTick)]
        [TestCase(ScrollInterruption.BeforeFirstTick)]
        [TestCase(ScrollInterruption.AxisFlip)]
        public void Given_ASmoothScrollInFlight_When_AnExternalChangeInterruptsIt_Then_TheOldAnimationLeavesTheListThere(
            ScrollInterruption interruption)
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            var initialRange = (scrollView.verticalScroller.lowValue, scrollView.verticalScroller.highValue);
            var initialValue = 0f;
            if (interruption == ScrollInterruption.BeforeFirstTick)
            {
                scrollView.verticalScroller.slider.SetValueWithoutNotify(50f);
                initialValue = scrollView.verticalScroller.value;
            }
            var interrupted = false;
            var stoppingEarlierAnimation = false;
            var earlierCompletions = 0;
            var earlierCompletionDuringStop = false;
            ValueAnimation<float> earlierAnimation = null;
            if (interruption == ScrollInterruption.DuringTick)
            {
                // Unity ticks a snapshot: this earlier animation stops the list's animation before its tick.
                earlierAnimation = scrollView.experimental.animation.Start(0f, 1f, 1000, (_, _) =>
                {
                    if (interrupted) return;
                    interrupted = true;
                    scrollView.verticalScroller.value = 20f;
                }).KeepAlive();
                earlierAnimation.OnCompleted(() =>
                {
                    earlierCompletions++;
                    earlierCompletionDuringStop = stoppingEarlierAnimation;
                });
            }

            try
            {
                handle.ScrollToItem(10, VirtualListAlign.Start, VirtualListScrollBehavior.Smooth);
                if (interruption == ScrollInterruption.BetweenTicks) Advance(0.1);

                // Act
                switch (interruption)
                {
                    case ScrollInterruption.BetweenTicks:
                        scrollView.verticalScroller.value = 20f;
                        break;
                    case ScrollInterruption.BeforeFirstTick:
                        scrollView.verticalScroller.value = 0f;
                        break;
                    case ScrollInterruption.AxisFlip:
                        TurnHorizontal();
                        break;
                }
                Advance(0.5);
                var stopCompletedInline = true;
                if (earlierAnimation != null)
                {
                    var completionsBeforeStop = earlierCompletions;
                    stoppingEarlierAnimation = true;
                    earlierAnimation.Stop();
                    stoppingEarlierAnimation = false;
                    var completedInline = completionsBeforeStop == 0 && earlierCompletions == 1 && earlierCompletionDuringStop;
                    earlierAnimation.Stop();
                    stopCompletedInline = completedInline && earlierCompletions == 1;
                }

                // Assert
                var unchangedRange = initialRange.Equals(
                    (scrollView.verticalScroller.lowValue, scrollView.verticalScroller.highValue));
                var setupHeld = interruption != ScrollInterruption.BeforeFirstTick
                    || initialValue == 50f && initialRange.Item2 > 50f && unchangedRange;
                var expectedVertical = interruption is ScrollInterruption.BeforeFirstTick or ScrollInterruption.AxisFlip
                    ? 0f : 20f;
                Assert.That((setupHeld, interruption != ScrollInterruption.DuringTick || interrupted, stopCompletedInline,
                        scrollView.verticalScroller.value, scrollView.horizontalScroller.value),
                    Is.EqualTo((true, true, true, expectedVertical, 0f)));
            }
            finally
            {
                stoppingEarlierAnimation = false;
                earlierAnimation?.Stop();
            }
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

        [Test]
        public void Given_ARetainedHandleWithAPendingTarget_When_SmoothIsRequestedAfterDisposal_Then_NoScrollWorkIsCreated()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            scrollView.verticalScroller.highValue = 100f;
            handle.ScrollToItem(10, VirtualListAlign.Start);
            var pendingBefore = ControllerState(scrollView, "_pendingScrollTarget") != null;
            Reconciler.Context.VirtualListControllers[scrollView].Dispose();

            // Act
            handle.ScrollToItem(10, VirtualListAlign.Start, VirtualListScrollBehavior.Smooth);
            var pendingAfter = ControllerState(scrollView, "_pendingScrollTarget") != null;
            var animationAfter = ControllerState(scrollView, "_smoothScroll") != null;
            Advance(0.5);

            // Assert
            Assert.That((pendingBefore, pendingAfter, animationAfter, scrollView.verticalScroller.value),
                Is.EqualTo((true, false, false, 100f)));
        }

        [TestCase(VirtualListScrollBehavior.Instant, false)]
        [TestCase(VirtualListScrollBehavior.Smooth, false)]
        [TestCase(VirtualListScrollBehavior.Instant, true)]
        public void Given_APendingTarget_When_AnExternalScrollPrecedesLayout_Then_TheOldTargetIsCancelled(
            VirtualListScrollBehavior behavior, bool silentValueChange)
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            scrollView.verticalScroller.highValue = 100f;
            var changedSilently = false;
            if (silentValueChange)
            {
                scrollView.verticalScroller.valueChanged += _ =>
                {
                    if (changedSilently) return;
                    changedSilently = true;
                    scrollView.verticalScroller.slider.SetValueWithoutNotify(0f);
                };
            }
            handle.ScrollToItem(10, VirtualListAlign.Start, behavior);
            Advance(0.5);
            var pendingBefore = ControllerState(scrollView, "_pendingScrollTarget") != null;
            var externalValue = silentValueChange ? 50f : 20f;

            // Act
            if (silentValueChange) scrollView.verticalScroller.highValue = 50f;
            scrollView.verticalScroller.value = externalValue;
            LayOutContent(scrollView, 4800f);

            // Assert
            Assert.That((pendingBefore, !silentValueChange || changedSilently, scrollView.verticalScroller.value),
                Is.EqualTo((true, true, externalValue)));
        }

        // GREEN_ON_BASE(characterization): the existing deferred target survives a scroller range change.
        [Test]
        public void Given_APendingTarget_When_ARangeClampPrecedesLayout_Then_TheTargetStillApplies()
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            scrollView.verticalScroller.highValue = 100f;
            handle.ScrollToItem(10, VirtualListAlign.Start);

            // Act
            scrollView.verticalScroller.highValue = 50f;
            var clamped = scrollView.verticalScroller.value;
            LayOutContent(scrollView, 4800f);

            // Assert
            Assert.That(new[] { clamped, scrollView.verticalScroller.value }, Is.EqualTo(new[] { 50f, 500f }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Given_AValueChangedHandlerIssuingANewerRequest_When_TheOuterScrollReturns_Then_OnlyTheNewTargetSurvives(
            bool useHandle)
        {
            // Arrange
            var (scrollView, handle) = MountScrollable();
            scrollView.verticalScroller.highValue = 100f;
            var requested = false;
            scrollView.verticalScroller.valueChanged += _ =>
            {
                if (requested) return;
                requested = true;
                if (useHandle) handle.ScrollToItem(1, VirtualListAlign.Start);
                else scrollView.verticalScroller.value = 50f;
            };

            // Act
            handle.ScrollToItem(10, VirtualListAlign.Start);
            LayOutContent(scrollView, 4800f);

            // Assert
            Assert.That((requested, scrollView.verticalScroller.value), Is.EqualTo((true, 50f)));
        }
    }
}
