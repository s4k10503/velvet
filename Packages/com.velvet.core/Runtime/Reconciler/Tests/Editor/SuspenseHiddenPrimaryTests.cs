using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a Suspense that has shown its children does when they suspend again, as React does: it keeps
    /// their elements in the tree, hidden with an inline <c>display: none</c> ahead of the fallback, and reveals
    /// the same elements when the read resolves.
    /// <list type="bullet">
    /// <item>A component inside a host element of the children keeps its state, its passive effect stays
    /// connected, and its layout effect is cleaned up while hidden, each time it is hidden, and set up again on
    /// reveal.</item>
    /// <item>A component whose read resolves shows the value on reveal, one inside a host element of the
    /// children included.</item>
    /// <item>A nested boundary still waiting keeps its own children hidden, and their layout effects down, when
    /// the enclosing one reveals.</item>
    /// <item>Removing the boundary while it hides its children unmounts them once, and a boundary put back in
    /// its place has not shown anything, so it discards what its first render built.</item>
    /// <item>An error boundary that catches in the render that hid the children leaves no hidden element
    /// written to after the catch takes it back, and a boundary above the Suspense's owner still reports a
    /// catch it took in that render.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseHiddenPrimaryTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setCount = default;
            s_setOwn = default;
            s_setShown = default;
            s_source = null;
            s_layoutSetups = 0;
            s_layoutCleanups = 0;
            s_passiveCleanups = 0;
            s_setOuterOwn = default;
            s_setInnerOwn = default;
            s_outerSource = null;
            s_setOwnerTick = default;
            s_innerLayoutSetups = 0;
            s_deepLayoutSetups = 0;
            s_setRestoreShown = default;
            s_setRestoreTick = default;
            s_setGhostTick = default;
            s_ghostThrows = false;
            s_setCatchTick = default;
            s_catchFactoryRuns = 0;
            s_setSwapped = default;
            s_reorderHost = null;
            s_orderedFirst = null;
            s_orderedSecond = null;
            s_setListTick = default;
            s_setListOther = default;
            s_rowSource = null;
            s_countingRenders = 0;
            s_afterRenders = 0;
            s_setAbandonTick = default;
            s_setOwnSibling = default;
            s_outsideSource = null;
            s_rootlessSource = new VelvetTaskCompletionSource<int>();
            s_bareListHost = null;
            s_abandonHost = null;
            s_portalTarget = new VisualElement();
            s_refElement = null;
            s_setRefShown = default;
            s_refHostRenders = 0;
            s_refAttachedRender = 0;
            s_setPortalLater = default;
            s_setAbortTick = default;
            s_setAbortOther = default;
            s_nestedRefElement = null;
        }

        [Test]
        public void Given_ARevealedBoundary_When_AChildsOwnUpdateSuspendsIt_Then_ThePrimaryElementStaysInTheTree()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var primary = _root.Q<VisualElement>("primary");

            // Act
            Resuspend(mounted);

            // Assert
            Assert.That(primary.parent, Is.SameAs(_root.Q<VisualElement>("container")),
                "React hides the children a boundary has shown rather than removing them");
        }

        [Test]
        public void Given_ARevealedBoundary_When_AChildsOwnUpdateSuspendsIt_Then_ThePrimaryElementIsDisplayedNone()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var primary = _root.Q<VisualElement>("primary");

            // Act
            Resuspend(mounted);

            // Assert
            Assert.That(primary.style.display.value, Is.EqualTo(DisplayStyle.None),
                "React hides the children a boundary has shown with display: none while the fallback shows");
        }

        // GREEN_ON_BASE(characterization): the base removes the hidden element, and the pool clears its display.
        // What this pins is that the element the boundary kept hidden has its display cleared on reveal.
        [Test]
        public void Given_ABoundaryHidingItsPrimary_When_TheReadResolves_Then_ThePrimaryElementsDisplayIsClearedAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var primary = _root.Q<VisualElement>("primary");
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(primary.style.display.keyword, Is.EqualTo(StyleKeyword.Null),
                "React takes the display: none off the children it reveals");
        }

        [Test]
        public void Given_AStatefulComponentInsideAnElementOfARevealedPrimary_When_TheBoundarySuspendsAgainAndReveals_Then_ItKeepsItsState()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(_root.Q<VisualElement>("primary")?.Q<Label>()?.text, Is.EqualTo("count:1"),
                "React keeps the state of a tree a Suspense hides");
        }

        // GREEN_ON_BASE(characterization): the base shows the resolved value in elements it creates again.
        // What this pins is that a reader kept hidden is rendered for the reveal rather than ahead of it.
        [Test]
        public void Given_AReaderInARevealedPrimary_When_ItsOwnUpdateSuspendsTheBoundaryAndItsReadResolves_Then_ItShowsTheValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(_root.Q<Label>("reader")?.text, Is.EqualTo("reader:5"),
                "The boundary that reveals the reader shows what its resolved read rendered");
        }

        [Test]
        public void Given_AReaderInsideAnElementOfARevealedPrimary_When_ItsOwnUpdateSuspendsTheBoundaryAndItsReadResolves_Then_ItShowsTheValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ElementReaderHostRender, key: "element-reader-host"));
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(_root.Q<Label>("reader")?.text, Is.EqualTo("reader:5"),
                "React reveals the reader it kept hidden with what its resolved read rendered");
        }

        [Test]
        public void Given_AComponentInsideAnElementOfARevealedPrimary_When_TheBoundarySuspendsAgain_Then_ItsPassiveEffectStaysConnected()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Resuspend(mounted);
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_passiveCleanups, Is.EqualTo(0),
                "React does not unmount the passive effects of a tree a Suspense hides");
        }

        // GREEN_ON_BASE(characterization): the base runs this cleanup when it disposes the component.
        // What this pins is that a component kept hidden inside an element has its layout effect cleaned up.
        [Test]
        public void Given_AComponentInsideAnElementOfARevealedPrimary_When_TheBoundarySuspendsAgain_Then_ItsLayoutEffectIsCleanedUp()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));

            // Act
            Resuspend(mounted);

            // Assert
            Assert.That(s_layoutCleanups, Is.EqualTo(1),
                "React cleans up the layout effects of a tree a Suspense hides");
        }

        // GREEN_ON_BASE(characterization): the base sets the effect up again when it mounts the component anew.
        // What this pins is that a component kept hidden inside an element has its layout effect set up again.
        [Test]
        public void Given_AComponentInsideAnElementOfAHiddenPrimary_When_TheBoundaryReveals_Then_ItsLayoutEffectIsSetUpAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(s_layoutSetups, Is.EqualTo(2),
                "React sets the layout effects of a tree a Suspense reveals up again, whatever their dependencies");
        }

        // GREEN_ON_BASE(characterization): the base disposes the component each time the boundary suspends.
        // What this pins is that a component the boundary revealed is hidden again, its layout effect with it.
        [Test]
        public void Given_AComponentInsideAnElementTheBoundaryRevealedAfterHidingIt_When_TheBoundarySuspendsAgain_Then_ItsLayoutEffectIsCleanedUpAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            Resuspend(mounted);
            Resolve(mounted, 5);

            // Act
            s_setOwn.Invoke(2);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_layoutCleanups, Is.EqualTo(2),
                "React cleans up the layout effects of a tree each time a Suspense hides it");
        }

        // GREEN_ON_BASE(characterization): the base runs this cleanup once, when the boundary suspends.
        // What this pins is that removing a boundary that keeps its primary hidden takes that primary with it.
        [Test]
        public void Given_ABoundaryHidingItsPrimary_When_TheBoundaryIsRemoved_Then_ThePrimaryIsUnmountedOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            mounted.FlushEffectsForTest();
            Resuspend(mounted);
            mounted.FlushEffectsForTest();

            // Act
            s_setShown.Invoke(false);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — every label is read, hidden ones included, since a hidden primary left behind shows nowhere
            Assert.That((s_passiveCleanups, AllLabelTexts()), Is.EqualTo((1, "removed")),
                "React unmounts the hidden children with the boundary");
        }

        // GREEN_ON_BASE(characterization): the base keeps no primary of a boundary that has not shown it.
        // What this pins is that a boundary put back where a revealed one was removed has not shown anything.
        [Test]
        public void Given_ARevealedBoundaryRemovedAndPutBack_When_ItsFirstRenderSuspends_Then_ItKeepsNothingHidden()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RestoreHostRender, key: "restore-host"));
            s_setRestoreShown.Invoke(false);
            mounted.FlushStateForTest();

            // Act
            s_setRestoreTick.Invoke(1);
            s_setRestoreShown.Invoke(true);
            mounted.FlushStateForTest();

            // Assert — every label is read, hidden ones included, since a kept primary would be hidden
            Assert.That(AllLabelTexts(), Is.EqualTo("loading"),
                "React discards what a Suspense's first render built when it suspends");
        }

        [Test]
        public void Given_NestedBoundariesBothHidingTheirPrimaries_When_OnlyTheOuterReadResolves_Then_TheInnerPrimaryStaysHidden()
        {
            // Arrange — the inner boundary suspends first, then the outer one
            using var mounted = V.Mount(_root, V.Component(NestedHostRender, key: "nested-host"));
            s_setInnerOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_setOuterOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            s_outerSource.TrySetResult(5);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(_root.DisplayedLabelTexts("|"), Is.EqualTo("outer:5|inner-loading"),
                "The inner boundary still waits, so React keeps its children hidden and its fallback shown");
        }

        // GREEN_ON_BASE(characterization): the base mounts the inner children again under the inner fallback.
        // What this pins is that the inner boundary, which hid a component after the outer one did, keeps it.
        [Test]
        public void Given_AComponentAnInnerBoundaryHidAfterTheOuterOneDid_When_OnlyTheOuterReadResolves_Then_ItsLayoutEffectStaysDown()
        {
            // Arrange — the outer boundary hides first; the inner one then suspends inside it
            using var mounted = HideOuterThenInner();

            // Act
            s_outerSource.TrySetResult(5);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_innerLayoutSetups, Is.EqualTo(1),
                "The inner boundary still waits, so React keeps the layout effects of its children down");
        }

        // GREEN_ON_BASE(characterization): the base disposes the inner children when the outer boundary hides.
        // What this pins is that a component inside an element of the inner primary stays with the inner boundary.
        [Test]
        public void Given_AComponentInsideAnElementAnInnerBoundaryHidAfterTheOuterOneDid_When_OnlyTheOuterReadResolves_Then_ItsLayoutEffectStaysDown()
        {
            // Arrange — the outer boundary hides first; the inner one then suspends inside it
            using var mounted = HideOuterThenInner();

            // Act
            s_outerSource.TrySetResult(5);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_deepLayoutSetups, Is.EqualTo(1),
                "The inner boundary still waits, so React keeps the layout effects of its children down");
        }

        // GREEN_ON_BASE(characterization): the base hides nothing, so it writes to no element after the catch.
        // What this pins is that the hide a caught render recorded is not applied to the elements it took back.
        [Test]
        public void Given_AnErrorBoundaryCatchingInTheRenderThatHidItsSuspensesPrimary_When_ALaterRenderCreatesALabel_Then_TheLabelIsDisplayed()
        {
            // Arrange — the catch takes back the hidden primary's rows, whose elements go back to the pool
            using var mounted = V.Mount(_root, V.Component(GhostHostRender, key: "ghost-host"), new MountOptions((_, _) => { }));
            s_ghostThrows = true;
            s_setGhostTick.Invoke(1);
            mounted.FlushStateForTest();
            s_ghostThrows = false;

            // Act
            s_setGhostTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.DisplayedLabelTexts("|"), Is.EqualTo("error|extra"),
                "A boundary's catch takes the hidden primary back, and nothing hides an element after it");
        }

        [Test]
        public void Given_ABoundaryAboveASuspenseOwnerCatchingAnElementCallbackErrorInAPrimaryItHides_When_ThatRenderCommits_Then_TheCatchIsReported()
        {
            // Arrange
            var reports = 0;
            using var mounted = V.Mount(_root, V.Div(children: new VNode[] { V.Component(CatchBoundaryRender, key: "outer") }),
                new MountOptions((_, _) => reports++));

            // Act
            s_setCatchTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — the factory's runs are read with it, since a callback that never ran is reported by nobody
            Assert.That((s_catchFactoryRuns, reports), Is.EqualTo((1, 1)),
                "React reports a catch the boundary above took, whatever the Suspense below it does with its render");
        }

        [Test]
        public void Given_ABoundaryHidingItsPrimary_When_ItsHostSwapsTwoChildren_Then_TheirFibersTakeTheNewOrder()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReorderHostRender, key: "reorder-host"));
            Resuspend(mounted);

            // Act
            s_setSwapped.Invoke(true);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(OrderedChildren(), Is.EqualTo("second|first"),
                "A primary kept hidden keeps its rows, so its components are ordered as their rows are");
        }

        [Test]
        public void Given_ARevealedBoundary_When_AVirtualListRowItMountsSuspends_Then_ThePrimaryElementStaysInTheTree()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ListHostRender, key: "list-host"));
            SeedViewport(mounted);
            var primary = _root.Q<VisualElement>("primary");

            // Act
            MountSuspendingRow(mounted);

            // Assert
            Assert.That(primary.parent, Is.SameAs(_root.Q<VisualElement>("container")),
                "React hides the children a boundary has shown whichever child suspends, a list row included");
        }

        [Test]
        public void Given_ABoundaryHidingItsPrimaryForAVirtualListRow_When_TheRowsReadResolves_Then_TheRowShowsTheValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ListHostRender, key: "list-host"));
            SeedViewport(mounted);
            MountSuspendingRow(mounted);

            // Act
            s_rowSource.TrySetResult(5);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(_root.Q<Label>("row")?.text, Is.EqualTo("row:5"),
                "The row the boundary held renders its resolved read once the boundary reveals it");
        }

        [Test]
        public void Given_ABoundaryHidingItsPrimaryForAVirtualListRowStillWaiting_When_ItsHostRendersAgain_Then_ThePrimaryStaysHidden()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ListHostRender, key: "list-host"));
            SeedViewport(mounted);
            var primary = _root.Q<VisualElement>("primary");
            MountSuspendingRow(mounted);

            // Act
            s_setListOther.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(primary.style.display.value, Is.EqualTo(DisplayStyle.None),
                "A row no walk renders is still waiting, so React keeps the boundary on its fallback");
        }

        // GREEN_ON_BASE(characterization): the base discards the inner children, and the outer boundary stays shown.
        // What this pins is that a row the inner boundary waits on does not suspend the outer boundary too.
        [Test]
        public void Given_AVirtualListRowSuspendingUnderAnInnerBoundaryAnotherComponentRenders_When_TheOuterBoundaryRenders_Then_OnlyTheInnerShowsItsFallback()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(OuterListHostRender, key: "outer-list-host"));
            SeedViewport(mounted);

            // Act
            MountSuspendingRow(mounted);

            // Assert
            Assert.That(_root.DisplayedLabelTexts("|"), Is.EqualTo("outer-content|inner-loading"),
                "A Suspense waits only for the rows its own boundary is the nearest one above");
        }

        // GREEN_ON_BASE(characterization): the base mounts the component again on reveal, which renders it once.
        // What this pins is that a component the boundary kept hidden renders once for the reveal, not twice.
        [Test]
        public void Given_AComponentInsideAnElementOfAHiddenPrimary_When_TheBoundaryReveals_Then_ItRendersOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CountingHostRender, key: "counting-host"));
            Resuspend(mounted);
            var rendersBefore = s_countingRenders;

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(s_countingRenders - rendersBefore, Is.EqualTo(1),
                "The reveal renders a hidden component it reaches once");
        }

        // GREEN_ON_BASE(characterization): the base stops a boundary's first render at the component that suspends.
        // What this pins is that a boundary that has shown nothing does not hold the suspend to render on.
        [Test]
        public void Given_ABoundaryThatHasNotShownItsChildren_When_AChildSuspends_Then_TheSiblingAfterItIsNotRenderedInThatPass()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(SiblingAfterHostRender, key: "sibling-after-host"));

            // Assert
            Assert.That(s_afterRenders, Is.EqualTo(0),
                "A Suspense that discards its first render's children stops that render where it suspends");
        }

        [Test]
        public void Given_ARevealedBoundaryWhoseChildSuspendsInAPassASuspendOutsideItGivesUp_When_ThePassIsGivenUp_Then_TheBoundaryIsNotRecordedShowingItsFallback()
        {
            // Arrange — the reader outside the Suspense has no boundary above, so the pass that hides the
            // boundary's children is given up after the Suspense has recorded its fallback
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(AbandonHostRender, key: "abandon-host"));
            s_setOwn.Invoke(1);
            s_setAbandonTick.Invoke(1);

            // Act
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(mounted.Root.Reconciler.Context.IsBoundaryShowingFallback(s_abandonHost), Is.False,
                "A pass that is given up commits no fallback, so none is recorded for the boundary");
        }

        [Test]
        public void Given_ABoundaryHidingItsPrimaryWhoseRevealIsGivenUpByASuspendOutsideIt_When_TheRevealIsRetried_Then_AComponentInsideAnElementOfItSetsItsLayoutEffectUpAgain()
        {
            // Arrange — the reader's resolve and the outside reader's suspend land in one pass, which is given up
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(AbandonHostRender, key: "abandon-host"));
            s_setCount.Invoke(1);
            mounted.FlushStateForTest();
            Resuspend(mounted);
            s_source.TrySetResult(5);
            s_setAbandonTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            s_outsideSource.TrySetResult(7);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the count is read with it, since a counter mounted again sets its effect up a second time too
            Assert.That((s_layoutSetups, _root.Q<VisualElement>("primary")?.Q<Label>()?.text), Is.EqualTo((2, "count:1")),
                "The given-up reveal showed nothing, so the reveal that commits sets the hidden effects up again");
        }

        [Test]
        public void Given_ARevealedBoundaryWhoseHideIsGivenUpByASuspendOutsideIt_When_TheHideIsRetried_Then_AComponentInsideAnElementOfItHasItsLayoutEffectCleanedUp()
        {
            // Arrange — the reader's suspend and the outside reader's land in one pass, which is given up
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(AbandonHostRender, key: "abandon-host"));
            var primary = _root.Q<VisualElement>("primary");
            s_setOwn.Invoke(1);
            s_setAbandonTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            s_outsideSource.TrySetResult(7);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the display is read with it, since a primary the boundary removes cleans the effect up too
            Assert.That((s_layoutCleanups, primary.style.display.value), Is.EqualTo((1, DisplayStyle.None)),
                "The given-up hide hid nothing, so the hide that commits takes the effects down");
        }

        [Test]
        public void Given_AReaderBesideARevealedBoundaryWhosePassItGaveUpHasResolved_When_AChildOfTheBoundarySuspends_Then_TheBoundaryHidesIt()
        {
            // Arrange — the host renders the Suspense, so it is the nearest boundary fiber above the reader beside it
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(AbandonHostRender, key: "abandon-host"));
            var primary = _root.Q<VisualElement>("primary");
            s_setAbandonTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_outsideSource.TrySetResult(7);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            Resuspend(mounted);

            // Assert — the reader's value is read with it, since a host never rendered again shows neither
            Assert.That((_root.Q<Label>("outside")?.text, primary.style.display.value),
                Is.EqualTo(("outside:7", DisplayStyle.None)),
                "The resolve retries the pass the reader gave up, as React retries it, so the host no longer waits on it");
        }

        [Test]
        public void Given_NestedBoundariesOneComponentRenders_When_AChildOfTheInnerOneSuspendsItsOwnUpdate_Then_OnlyTheInnerShowsItsFallback()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SameComponentNestedHostRender, key: "same-nested-host"));

            // Act
            s_setInnerOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(_root.DisplayedLabelTexts("|"), Is.EqualTo("outer-content|inner-loading"),
                "A Suspense waits only for what no Suspense inside it waits for, one the same component renders included");
        }

        // GREEN_ON_BASE(characterization): the base discards the inner children, and the outer boundary stays shown.
        // What this pins is that a row the inner boundary hides does not suspend the outer one when one component
        // renders both.
        [Test]
        public void Given_NestedBoundariesOneComponentRenders_When_AVirtualListRowOfTheInnerOneSuspendsAsItMounts_Then_OnlyTheInnerShowsItsFallback()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SameComponentNestedListHostRender, key: "same-nested-list-host"));
            SeedViewport(mounted);

            // Act
            MountSuspendingRow(mounted);

            // Assert
            Assert.That(_root.DisplayedLabelTexts("|"), Is.EqualTo("outer-content|inner-loading"),
                "A Suspense waits only for the rows no Suspense inside it hides, one the same component renders included");
        }

        [Test]
        public void Given_APortalInARevealedPrimary_When_TheBoundarySuspendsAgain_Then_ThePortalsChildIsDisplayedNone()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PortalHostRender, key: "portal-host"));
            var portaled = s_portalTarget.Q<Label>("portaled");

            // Act
            Resuspend(mounted);

            // Assert
            Assert.That(portaled.style.display.value, Is.EqualTo(DisplayStyle.None),
                "React hides a Portal's children with the rest of the children a boundary hides");
        }

        // GREEN_ON_BASE(characterization): the base hides nothing, so a Portal beside the boundary keeps its child shown.
        // What this pins is that the hide reaches the children of the Portals inside the boundary alone.
        [Test]
        public void Given_APortalBesideARevealedBoundary_When_TheBoundarySuspendsAgain_Then_ThePortalsChildStaysDisplayed()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PortalHostRender, key: "portal-host"));
            var beside = s_portalTarget.Q<Label>("beside");

            // Act
            Resuspend(mounted);

            // Assert
            Assert.That(beside.style.display.keyword, Is.EqualTo(StyleKeyword.Null),
                "A boundary hides its own children, and a Portal written beside it is not among them");
        }

        [Test]
        public void Given_ABoundaryHidingItsPrimary_When_ARenderAddsAPortalToItsChildren_Then_ThePortalsChildIsDisplayedNone()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PortalHostRender, key: "portal-host"));
            Resuspend(mounted);

            // Act
            s_setPortalLater.Invoke(true);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_portalTarget.Q<Label>("later")?.style.display.value, Is.EqualTo(DisplayStyle.None),
                "A Portal created among the children a boundary hides has its children hidden with them");
        }

        [Test]
        public void Given_ABoundaryHidingItsPrimaryWhoseRevealIsStoppedByACatchAfterIt_When_ItsHostRendersAgain_Then_ItRevealsItsChildren()
        {
            // Arrange — the boundary beside the Suspense catches an element callback's error in the pass that
            // reveals, which stops that pass after the Suspense recorded the reveal
            using var mounted = V.Mount(_root, V.Component(AbortHostRender, key: "abort-host"), new MountOptions((_, _) => { }));
            var container = _root.Q<VisualElement>("container");
            var primary = _root.Q<VisualElement>("primary");
            Resuspend(mounted);
            s_source.TrySetResult(5);
            s_setAbortTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            s_setAbortOther.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the parent is read with it, since a primary created again is displayed too, and the
            // boundary's fallback, which shows that the catch stopped the pass
            Assert.That((primary.parent == container, primary.style.display.keyword, _root.DisplayedLabelTexts("|").Contains("caught")),
                Is.EqualTo((true, StyleKeyword.Null, true)),
                "The stopped pass revealed nothing, so the boundary still hides its children and reveals them now");
        }

        // GREEN_ON_BASE(characterization): the base discards the inner children, so their ref is not attached.
        // What this pins is that an outer reveal leaves the ref an inner boundary still hides detached.
        [Test]
        public void Given_ARefUnderAnInnerBoundaryStillHidingIt_When_TheOuterBoundaryReveals_Then_TheRefStaysDetached()
        {
            // Arrange — the outer boundary hides first; the inner one then suspends inside it
            using var mounted = V.Mount(_root, V.Component(NestedRefHostRender, key: "nested-ref-host"));
            var attachedBeforeTheHide = s_nestedRefElement != null;
            s_setOuterOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_setOwnerTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            s_outerSource.TrySetResult(5);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — whether the ref was attached at all is read with it, since one never attached is not after
            Assert.That((attachedBeforeTheHide, s_nestedRefElement == null), Is.EqualTo((true, true)),
                "The inner boundary still waits, so React keeps the refs of its children detached");
        }

        [Test]
        public void Given_APortalInAHiddenPrimary_When_TheBoundaryReveals_Then_ItsPlaceholderIsDisplayedNoneAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PortalHostRender, key: "portal-host"));
            var container = _root.Q<VisualElement>("container");
            var placeholder = container[0];
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert — the parent is read with it, since a placeholder created again is displayed none as well
            Assert.That((placeholder.parent == container, placeholder.style.display.value),
                Is.EqualTo((true, DisplayStyle.None)),
                "A reveal gives an element back the display it had, and a Portal's placeholder has no box");
        }

        // GREEN_ON_BASE(characterization): the base runs the ref's cleanup when it removes the element.
        // What this pins is that an element the boundary keeps hidden has its ref detached.
        [Test]
        public void Given_AnElementWithARefInARevealedPrimary_When_TheBoundarySuspendsAgain_Then_ItsRefIsDetached()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RefHostRender, key: "ref-host"));

            // Act
            Resuspend(mounted);

            // Assert
            Assert.That(s_refElement, Is.Null,
                "React 18 detaches the host refs of a tree a Suspense hides");
        }

        [Test]
        public void Given_AnElementWithARefInAHiddenPrimary_When_TheBoundaryReveals_Then_ItsRefIsAttachedToTheSameElement()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RefHostRender, key: "ref-host"));
            var inner = _root.Q<VisualElement>("inner");
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(s_refElement, Is.SameAs(inner),
                "React 18 attaches the host refs of a tree a Suspense reveals again, to the elements it kept");
        }

        // GREEN_ON_BASE(characterization): the base creates the element again on reveal, with the reveal's ref.
        // What this pins is that a kept element gets the ref its reveal render passed, not the one it had.
        [Test]
        public void Given_AnElementWithARefInAHiddenPrimary_When_TheBoundaryReveals_Then_TheRefItsRevealRenderPassedIsAttached()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RefHostRender, key: "ref-host"));
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert
            Assert.That(s_refAttachedRender, Is.EqualTo(s_refHostRenders),
                "React attaches the ref of the render it commits");
        }

        [Test]
        public void Given_AnElementWhoseRefABoundaryDetached_When_TheBoundaryIsRemoved_Then_NoRefIsKeptForIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RefHostRender, key: "ref-host"));
            Resuspend(mounted);

            // Act
            s_setRefShown.Invoke(false);
            mounted.FlushStateForTest();

            // Assert — whether the table exists is read with it, since a tree without one keeps no ref in it
            Assert.That(RefsKeptWhileHidden(mounted.Root.Reconciler.Context), Is.EqualTo((true, 0)),
                "An element that leaves while hidden has had its ref's cleanup, so nothing attaches it again");
        }

        [Test]
        public void Given_ABoundaryHidingItsPrimaryWhoseRevealIsGivenUpByASuspendOutsideIt_When_TheOutsideReadResolves_Then_ItRevealsItsChildren()
        {
            // Arrange — the reader's resolve and the outside reader's suspend land in one pass, which is given up
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(AbandonHostRender, key: "abandon-host"));
            var reader = _root.Q<Label>("reader");
            Resuspend(mounted);
            s_source.TrySetResult(5);
            s_setAbandonTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_outsideSource.TrySetResult(7);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the display is read with the text, since a reader patched while hidden reads 5 too
            Assert.That((reader.parent != null, reader.text, reader.style.display.value),
                Is.EqualTo((true, "reader:5", DisplayStyle.Flex)),
                "The given-up pass revealed nothing, so the boundary still hides its children and reveals them now");
        }

        [Test]
        public void Given_ARootlessRevealedBoundaryWhoseRenderIsGivenUpByASuspendOutsideIt_When_TheRenderThrows_Then_NoFallbackIsRecorded()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var shown = RootlessAbandonTree(waiting: false);
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), shown);

            // Act — no Suspense is above the outside reader, so its suspend leaves the reconcile
            try
            {
                scope.Reconciler.Reconcile(scope.Root, shown, RootlessAbandonTree(waiting: true));
            }
            catch (FiberSuspendSignal)
            {
            }

            // Assert
            Assert.That(RootlessFallbackCount(scope.Reconciler), Is.EqualTo(0),
                "A render that is given up commits no fallback, so none is recorded");
        }

        [Test]
        public void Given_ABoundaryThatRevealedItsPrimary_When_TheRevealCommits_Then_NoElementOfItIsRecordedHidden()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var reader = _root.Q<Label>("reader");
            Resuspend(mounted);

            // Act
            Resolve(mounted, 5);

            // Assert — whether the record exists is read with it, since a tree without one records nothing hidden
            Assert.That(RecordedHidden(reader), Is.EqualTo((true, false)),
                "An element the boundary revealed is the anchoring's to show and hide again");
        }

        [Test]
        public void Given_AnElementABoundaryHid_When_TheBoundaryIsRemovedAndTheElementGoesBackToThePool_Then_ItIsNoLongerRecordedHidden()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var reader = _root.Q<Label>("reader");
            Resuspend(mounted);

            // Act
            s_setShown.Invoke(false);
            mounted.FlushStateForTest();

            // Assert — whether the record exists is read with it, since a tree without one records nothing hidden
            Assert.That(RecordedHidden(reader), Is.EqualTo((true, false)),
                "A pooled element's next consumer is shown and hidden by its own anchoring");
        }

        // GREEN_ON_BASE(characterization): the base gives up every pass in which a mounting row suspends.
        // What this pins is that only a Suspense keeping the children it showed holds a row's suspend.
        [Test]
        public void Given_AVirtualListRowWithNoSuspenseAbove_When_ItSuspendsAsItMounts_Then_TheHostsPassIsGivenUp()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(BareListHostRender, key: "bare-list-host"));
            SeedViewport(mounted);

            // Act
            MountSuspendingRow(mounted);

            // Assert
            Assert.That(MarkedSuspended(s_bareListHost), Is.True,
                "With no Suspense to hold it, a row's suspend gives up the pass that mounted it, as React's does");
        }

        [Test]
        public void Given_ARootlessRevealedBoundary_When_ItsChildSuspends_Then_ThePrimaryElementIsDisplayedNone()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var shown = RootlessTree(waiting: false);
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), shown);
            var primary = scope.Root.Q<Label>("rootless-primary");

            // Act
            scope.Reconciler.Reconcile(scope.Root, shown, RootlessTree(waiting: true));

            // Assert
            Assert.That(primary.style.display.value, Is.EqualTo(DisplayStyle.None),
                "A boundary no component renders hides the children it has shown as any other does");
        }

        // GREEN_ON_BASE(characterization): the base keeps no primary of a boundary that has not shown it.
        // What this pins is that a rootless boundary on its first fallback is not read as hiding its children.
        [Test]
        public void Given_ARootlessBoundaryOnItsFirstFallback_When_TheReadResolvesAndItRendersAgain_Then_ItShowsItsChildren()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Suspense(V.Label(text: "loading"), new VNode[]
                {
                    V.Label(text: "first"),
                    V.Component(RootlessReaderRender, key: "rootless-reader"),
                }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Act
            s_rootlessSource.TrySetResult(3);
            scope.Reconciler.Reconcile(scope.Root, tree, tree);

            // Assert
            Assert.That(string.Join("|", scope.Root.Query<Label>().ToList().Select(label => label.text)),
                Is.EqualTo("first|rootless:3"),
                "React reveals what the boundary's render builds once nothing it reads is pending");
        }

        private static void Resuspend(MountedTree mounted)
        {
            s_setOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static void Resolve(MountedTree mounted, int value)
        {
            s_source.TrySetResult(value);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private MountedTree HideOuterThenInner()
        {
            var mounted = V.Mount(_root, V.Component(OwnershipHostRender, key: "ownership-host"));
            s_setOuterOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_setOwnerTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            return mounted;
        }

        private string AllLabelTexts() => string.Join("|", _root.Query<Label>().ToList().Select(label => label.text));

        private static StateUpdater<int> s_setCount;
        private static StateUpdater<int> s_setOwn;
        private static StateUpdater<bool> s_setShown;
        private static VelvetTaskCompletionSource<int> s_source;
        private static int s_layoutSetups;
        private static int s_layoutCleanups;
        private static int s_passiveCleanups;

        [Component]
        private static VNode CounterRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            Hooks.UseLayoutEffect(() =>
            {
                s_layoutSetups++;
                return (Action)(() => s_layoutCleanups++);
            }, Array.Empty<object>());
            Hooks.UseEffect(() => () => s_passiveCleanups++, Array.Empty<object>());
            return V.Label(text: "count:" + count);
        }

        [Component]
        private static VNode ReaderRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : (s_source = new VelvetTaskCompletionSource<int>()).Task, own);
            return V.Label(name: "reader", text: "reader:" + value);
        }

        // Pending for good once tick is not zero.
        [Component]
        private static VNode TickReaderRender(int tick)
        {
            var value = Hooks.Use<int>(_ => tick == 0
                ? VelvetTask.FromResult(0)
                : new VelvetTaskCompletionSource<int>().Task, tick);
            return V.Label(text: "tick:" + value);
        }

        // The counter sits inside a host element of the primary, which that element's own reconcile mounts; the
        // reader sits in the Suspense's own walk.
        [Component]
        private static VNode HostRender()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(name: "container", children: new VNode[]
            {
                shown
                    ? V.Suspense(
                        fallback: V.Label(text: "loading"),
                        children: new VNode[]
                        {
                            V.Div(name: "primary", children: new VNode[] { V.Component(CounterRender, key: "counter") }),
                            V.Component(ReaderRender, key: "reader"),
                        })
                    : V.Label(text: "removed"),
            });
        }

        [Component]
        private static VNode ElementReaderHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Div(name: "reader-host", children: new VNode[] { V.Component(ReaderRender, key: "reader") }),
                    }),
            });

        private static StateUpdater<int> s_setOuterOwn;
        private static StateUpdater<int> s_setInnerOwn;
        private static VelvetTaskCompletionSource<int> s_outerSource;

        [Component]
        private static VNode OuterReaderRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setOuterOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : (s_outerSource = new VelvetTaskCompletionSource<int>()).Task, own);
            return V.Label(text: "outer:" + value);
        }

        [Component]
        private static VNode InnerReaderRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setInnerOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : new VelvetTaskCompletionSource<int>().Task, own);
            return V.Label(text: "inner:" + value);
        }

        // The inner Suspense sits in the outer one's own walk, so one walk expands both.
        [Component]
        private static VNode NestedHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "outer-loading"),
                    children: new VNode[]
                    {
                        V.Component(OuterReaderRender, key: "outer"),
                        V.Suspense(
                            fallback: V.Label(text: "inner-loading"),
                            children: new VNode[]
                            {
                                V.Div(name: "inner-primary", children: new VNode[]
                                {
                                    V.Component(InnerReaderRender, key: "inner"),
                                }),
                            }),
                    }),
            });

        private static StateUpdater<int> s_setOwnerTick;
        private static int s_innerLayoutSetups;
        private static int s_deepLayoutSetups;

        [Component]
        private static VNode InnerLayoutRender()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_innerLayoutSetups++;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(text: "inner-layout");
        }

        [Component]
        private static VNode DeepLayoutRender()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_deepLayoutSetups++;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(text: "deep-layout");
        }

        // The inner Suspense sits inside a host element of the outer primary, so that element's own reconcile
        // expands it; one of its children sits in its own walk and one inside a host element of it.
        [Component]
        private static VNode OwnershipHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnerTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "outer-loading"),
                    children: new VNode[]
                    {
                        V.Component(OuterReaderRender, key: "outer"),
                        V.Div(name: "inner-host", children: new VNode[]
                        {
                            V.Suspense(
                                fallback: V.Label(text: "inner-loading"),
                                children: new VNode[]
                                {
                                    V.Component(InnerLayoutRender, key: "inner-layout"),
                                    V.Div(name: "deep", children: new VNode[]
                                    {
                                        V.Component(DeepLayoutRender, key: "deep-layout"),
                                    }),
                                    V.Component(TickReaderRender, tick, key: "tick-reader"),
                                }),
                        }),
                    }),
            });
        }

        private static StateUpdater<bool> s_setRestoreShown;
        private static StateUpdater<int> s_setRestoreTick;

        [Component]
        private static VNode RestoreHostRender()
        {
            var (shown, setShown) = Hooks.UseState(true);
            var (tick, setTick) = Hooks.UseState(0);
            s_setRestoreShown = setShown;
            s_setRestoreTick = setTick;
            return V.Div(children: new VNode[]
            {
                shown
                    ? V.Suspense(
                        fallback: V.Label(text: "loading"),
                        children: new VNode[]
                        {
                            V.Label(text: "restored"),
                            V.Component(TickReaderRender, tick, key: "tick-reader"),
                        })
                    : V.Label(text: "removed"),
            });
        }

        private static StateUpdater<int> s_setGhostTick;
        private static bool s_ghostThrows;

        [Component(Compiler = false)]
        private static VNode GhostThrowerRender()
        {
            if (s_ghostThrows) throw new InvalidOperationException("ghost thrower failed");
            return V.Label(text: "thrower");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode GhostBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "error"));
            return V.Fragment(new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(TickReaderRender, tick, key: "tick-reader") }),
                V.Component(GhostThrowerRender, key: "thrower"),
            });
        }

        // The boundary is rendered by this host's walk, which is where its catch takes its rows back.
        [Component(Compiler = false)]
        private static VNode GhostHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setGhostTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Component(GhostBoundaryRender, tick, key: "boundary"),
                tick >= 2 ? V.Label(text: "extra") : null,
            });
        }

        private static StateUpdater<int> s_setCatchTick;
        private static int s_catchFactoryRuns;

        [Component(Compiler = false)]
        private static VNode CallbackThrowingRender()
            => V.ScrollView(onCreated: _ => throw new InvalidOperationException("Element callback throw"));

        // Its own update is the pass, so the boundary above it takes the callback's error on the aborting path.
        [Component(Compiler = false)]
        private static VNode CatchHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setCatchTick = setTick;
            return V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Component(TickReaderRender, tick, key: "tick-reader"),
                    tick == 0 ? null : V.Component(CallbackThrowingRender, key: "callback"),
                });
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode CatchBoundaryRender()
        {
            Hooks.UseFallback(_ =>
            {
                s_catchFactoryRuns++;
                return V.Label(text: "outer-fallback");
            });
            return V.Component(CatchHostRender, key: "host");
        }

        private static void SeedViewport(MountedTree mounted)
        {
            // The viewport height a geometry pass would leave, which a headless mount measures none of.
            var scrollView = mounted.Root.Reconciler.Context.VirtualListControllers.Keys.First();
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(mounted.Root.Reconciler.Context.VirtualListControllers[scrollView], 50f);
        }

        private static void MountSuspendingRow(MountedTree mounted)
        {
            s_setListTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private static string OrderedChildren()
        {
            var names = new System.Collections.Generic.List<string>();
            for (var child = s_reorderHost.Child; child != null; child = child.Sibling)
            {
                if (child == s_orderedFirst) names.Add("first");
                else if (child == s_orderedSecond) names.Add("second");
            }
            return string.Join("|", names);
        }

        private static VNode[] RootlessTree(bool waiting)
            => new VNode[]
            {
                V.Suspense(V.Label(text: "loading"), waiting
                    ? new VNode[] { V.Label(name: "rootless-primary", text: "shown"), V.Component(TickReaderRender, 1, key: "pending") }
                    : new VNode[] { V.Label(name: "rootless-primary", text: "shown") }),
            };

        private static StateUpdater<bool> s_setSwapped;
        private static ComponentFiber s_reorderHost;
        private static ComponentFiber s_orderedFirst;
        private static ComponentFiber s_orderedSecond;

        [Component(Compiler = false)]
        private static VNode OrderedRender(string name)
        {
            if (name == "first") s_orderedFirst = FiberAmbientStack.Current;
            else s_orderedSecond = FiberAmbientStack.Current;
            return V.Label(text: name);
        }

        [Component(Compiler = false)]
        private static VNode ReorderHostRender()
        {
            var (swapped, setSwapped) = Hooks.UseState(false);
            s_setSwapped = setSwapped;
            s_reorderHost = FiberAmbientStack.Current;
            var first = V.Component(OrderedRender, "first", key: "first");
            var second = V.Component(OrderedRender, "second", key: "second");
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        swapped ? second : first,
                        swapped ? first : second,
                        V.Component(ReaderRender, key: "reader"),
                    }),
            });
        }

        private static StateUpdater<int> s_setListTick;
        private static StateUpdater<int> s_setListOther;
        private static VelvetTaskCompletionSource<int> s_rowSource;

        [Component]
        private static VNode RowReaderRender()
        {
            var value = Hooks.Use<int>(_ => (s_rowSource ??= new VelvetTaskCompletionSource<int>()).Task, "row");
            return V.Label(name: "row", text: "row:" + value);
        }

        private static VNode RowList(int tick)
            => V.VirtualList(
                items: tick > 0 ? new[] { "a" } : Array.Empty<string>(),
                keySelector: item => item,
                itemHeight: 50f,
                renderer: item => V.Component(RowReaderRender, key: item),
                overscan: 0,
                key: "list");

        [Component]
        private static VNode ListHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            var (other, setOther) = Hooks.UseState(0);
            s_setListTick = setTick;
            s_setListOther = setOther;
            return V.Div(name: "container", children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Div(name: "primary", children: new VNode[] { V.Component(CounterRender, key: "counter") }),
                        RowList(tick),
                        V.Label(text: "other:" + other),
                    }),
            });
        }

        // Renders the inner Suspense itself, so it is that Suspense's boundary, not the outer host.
        [Component]
        private static VNode InnerListHostRender(int tick)
            => V.Suspense(fallback: V.Label(text: "inner-loading"), children: new VNode[] { RowList(tick) });

        [Component]
        private static VNode OuterListHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setListTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "outer-loading"),
                    children: new VNode[]
                    {
                        V.Label(text: "outer-content"),
                        V.Div(children: new VNode[] { V.Component(InnerListHostRender, tick, key: "inner") }),
                    }),
            });
        }

        private static int s_countingRenders;

        [Component(Compiler = false)]
        private static VNode CountingRender()
        {
            s_countingRenders++;
            return V.Label(text: "counting");
        }

        [Component]
        private static VNode CountingHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Div(name: "primary", children: new VNode[] { V.Component(CountingRender, key: "counting") }),
                        V.Component(ReaderRender, key: "reader"),
                    }),
            });

        private static int s_afterRenders;

        [Component(Compiler = false)]
        private static VNode AfterRender()
        {
            s_afterRenders++;
            return V.Label(text: "after");
        }

        [Component]
        private static VNode SiblingAfterHostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Component(TickReaderRender, 1, key: "pending"),
                    V.Component(AfterRender, key: "after"),
                });

        private static StateUpdater<int> s_setAbandonTick;
        private static StateUpdater<int> s_setOwnSibling;
        private static VelvetTaskCompletionSource<int> s_outsideSource;
        private static ComponentFiber s_abandonHost;

        [Component]
        private static VNode OwnSiblingRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setOwnSibling = setOwn;
            return V.Label(name: "own", text: "own:" + own);
        }

        [Component]
        private static VNode OutsideReaderRender(int tick)
        {
            var value = Hooks.Use<int>(_ => tick == 0
                ? VelvetTask.FromResult(0)
                : (s_outsideSource ??= new VelvetTaskCompletionSource<int>()).Task, tick);
            return V.Label(name: "outside", text: "outside:" + value);
        }

        // The Suspense and the reader outside it share one container, whose walk the outside reader stops. The
        // counter sits inside a host element of the primary, which that element's own reconcile mounts.
        [Component]
        private static VNode AbandonHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setAbandonTick = setTick;
            s_abandonHost = FiberAmbientStack.Current;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Div(name: "primary", children: new VNode[] { V.Component(CounterRender, key: "counter") }),
                        V.Component(OwnSiblingRender, key: "own"),
                        V.Component(ReaderRender, key: "reader"),
                    }),
                V.Component(OutsideReaderRender, tick, key: "outside"),
            });
        }

        private static VelvetTaskCompletionSource<int> s_rootlessSource;

        [Component]
        private static VNode RootlessReaderRender()
            => V.Label(text: "rootless:" + Hooks.Use<int>(_ => s_rootlessSource.Task, "rootless"));

        // The outside reader sits under a component: a fiber with no parent that suspends gives up its own render
        // alone, and the reconcile goes on.
        private static VNode[] RootlessAbandonTree(bool waiting)
            => new VNode[]
            {
                V.Suspense(V.Label(text: "loading"), new VNode[] { V.Component(TickReaderRender, waiting ? 1 : 0, key: "inside") }),
                V.Component(OutsideWrapperRender, waiting ? 1 : 0, key: "outside"),
            };

        [Component]
        private static VNode OutsideWrapperRender(int tick) => V.Component(TickReaderRender, tick, key: "reader");

        private static int RootlessFallbackCount(Reconciler reconciler)
        {
            var entries = typeof(ReconcilerContext)
                .GetField("_rootlessSuspenseFallbackKeys", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(reconciler.Context);
            return (int)entries.GetType().GetProperty("Count")!.GetValue(entries);
        }

        // Read by name so this file still builds on a tree without the record, which reads as not found.
        private static (bool Found, bool Hidden) RecordedHidden(VisualElement element)
        {
            var isHidden = typeof(ComponentFiber).Assembly.GetType("Velvet.SuspenseHiddenElements")
                ?.GetMethod("IsHidden", BindingFlags.Static | BindingFlags.NonPublic);
            return (isHidden != null, isHidden?.Invoke(null, new object[] { element }) is true);
        }

        // Read by name so this file still builds on a tree without the property, where the case fails instead.
        private static bool MarkedSuspended(ComponentFiber fiber)
            => typeof(ComponentFiber).GetProperty("SuspendedOn", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(fiber) != null;

        private static ComponentFiber s_bareListHost;

        [Component]
        private static VNode SameComponentNestedHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "outer-loading"),
                    children: new VNode[]
                    {
                        V.Label(text: "outer-content"),
                        V.Suspense(
                            fallback: V.Label(text: "inner-loading"),
                            children: new VNode[] { V.Component(InnerReaderRender, key: "inner") }),
                    }),
            });

        [Component]
        private static VNode SameComponentNestedListHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setListTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "outer-loading"),
                    children: new VNode[]
                    {
                        V.Label(text: "outer-content"),
                        V.Suspense(fallback: V.Label(text: "inner-loading"), children: new VNode[] { RowList(tick) }),
                    }),
            });
        }

        private static VisualElement s_portalTarget;

        private static StateUpdater<bool> s_setPortalLater;

        [Component]
        private static VNode PortalHostRender()
        {
            var (later, setLater) = Hooks.UseState(false);
            s_setPortalLater = setLater;
            return V.Div(name: "container", children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Portal(s_portalTarget, children: new VNode[] { V.Label(name: "portaled", text: "portaled") }),
                        V.Component(ReaderRender, key: "reader"),
                        later ? V.Portal(s_portalTarget, children: new VNode[] { V.Label(name: "later", text: "later") }) : null,
                    }),
                V.Portal(s_portalTarget, children: new VNode[] { V.Label(name: "beside", text: "beside") }),
            });
        }

        private static VisualElement s_refElement;
        private static StateUpdater<bool> s_setRefShown;
        private static int s_refHostRenders;
        private static int s_refAttachedRender;

        // The ref sits below the primary's outermost element, and its callback captures, so each render hands a
        // new one, as an inline lambda that captures does.
        [Component(Compiler = false)]
        private static VNode RefHostRender()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setRefShown = setShown;
            var render = ++s_refHostRenders;
            return V.Div(name: "container", children: new VNode[]
            {
                shown
                    ? V.Suspense(
                        fallback: V.Label(text: "loading"),
                        children: new VNode[]
                        {
                            V.Div(name: "primary", children: new VNode[]
                            {
                                V.Div(name: "inner", refCallback: element =>
                                {
                                    s_refElement = element;
                                    s_refAttachedRender = render;
                                    return () => s_refElement = null;
                                }),
                            }),
                            V.Component(ReaderRender, key: "reader"),
                        })
                    : V.Label(text: "removed"),
            });
        }

        // Read by name so this file still builds on a tree without the table, which reads as not found.
        private static (bool Found, int Count) RefsKeptWhileHidden(ReconcilerContext context)
        {
            var refs = typeof(ReconcilerContext)
                .GetField("_refsDetachedWhileHidden", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(context);
            return (refs != null, refs == null ? 0 : (int)refs.GetType().GetProperty("Count")!.GetValue(refs));
        }

        private static StateUpdater<int> s_setAbortTick;
        private static StateUpdater<int> s_setAbortOther;

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode AbortBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "caught"));
            return tick == 0 ? V.Label(text: "fine") : V.Component(CallbackThrowingRender, key: "callback");
        }

        // The error boundary is rendered by this host's walk after the Suspense, and an element callback's error is
        // caught on the aborting path, which stops that walk.
        [Component(Compiler = false)]
        private static VNode AbortHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            var (other, setOther) = Hooks.UseState(0);
            s_setAbortTick = setTick;
            s_setAbortOther = setOther;
            return V.Div(name: "container", children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Div(name: "primary"),
                        V.Component(ReaderRender, key: "reader"),
                    }),
                V.Component(AbortBoundaryRender, tick, key: "boundary"),
                V.Label(text: "other:" + other),
            });
        }

        private static VisualElement s_nestedRefElement;

        // The inner Suspense sits inside a host element of the outer primary, so that element's own reconcile
        // expands it; the ref is on an element of the inner primary.
        [Component]
        private static VNode NestedRefHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnerTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "outer-loading"),
                    children: new VNode[]
                    {
                        V.Component(OuterReaderRender, key: "outer"),
                        V.Div(name: "inner-host", children: new VNode[]
                        {
                            V.Suspense(
                                fallback: V.Label(text: "inner-loading"),
                                children: new VNode[]
                                {
                                    V.Div(name: "nested-ref", refCallback: element =>
                                    {
                                        s_nestedRefElement = element;
                                        return () => s_nestedRefElement = null;
                                    }),
                                    V.Component(TickReaderRender, tick, key: "tick-reader"),
                                }),
                        }),
                    }),
            });
        }

        [Component]
        private static VNode BareListHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setListTick = setTick;
            s_bareListHost = FiberAmbientStack.Current;
            return V.Div(children: new VNode[] { RowList(tick) });
        }
    }
}
