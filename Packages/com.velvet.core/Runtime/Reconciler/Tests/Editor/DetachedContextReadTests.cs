using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins renders of a <c>V.VirtualList</c> row or a <c>V.Portal</c> child, and of what renders under either, that
    /// read the nearest Provider above it as it currently is. A row or child that re-renders on its own reads
    /// the new value of a Provider above a memoized host that bailed, and the new value its host put around the list
    /// or Portal, or the list's renderer around the row; a row a scroll mounts reads the Provider its host put around the list rather than a farther one of
    /// the same context; a parked pass resuming, an error boundary's fallback and a Portal's first render read the
    /// current value where a row or a Portal encloses them; a render of the host that aborts or that a Suspense
    /// discards is passed over for the last one committed; and where one node instance stands at two places on the
    /// way down, what the row or child was created under is read rather than the Providers around the other place.
    /// </summary>
    [TestFixture]
    internal sealed class DetachedContextReadTests
    {
        private const int SlicedRows = 8;
        private const int LastSlicedRow = SlicedRows - 1;
        private const string ThrowerError = "Detached context read test throw";
        private const int PortalOwners = 8;
        private const int OwnerRenders = 6;

        private static readonly ComponentContext<string> s_theme = ComponentContext<string>.Create("default");
        private static readonly ComponentContext<string> s_around = ComponentContext<string>.Create("around");
        private static readonly IReadOnlyList<string> s_oneItem = new[] { "row" };
        private static readonly Dictionary<int, string> s_rowReadings = new();
        private static readonly Dictionary<int, ComponentFiber> s_rowFibers = new();
        private static readonly MountOptions s_ignoringCaughtErrors = new() { OnCaughtError = (_, _) => { } };
        private static readonly ComponentFiber[] s_portalOwners = new ComponentFiber[PortalOwners];
        private static readonly StateUpdater<int>[] s_setOwnerTicks = new StateUpdater<int>[PortalOwners];

        private static string s_reading;
        private static string s_lastReading;
        private static StateUpdater<int> s_setLastReader;
        private static StateUpdater<string> s_setDeclaredValue;
        private static StateUpdater<IReadOnlyList<string>> s_setSharedItems;
        private static string s_fallbackReading;
        private static string s_portalReading;
        private static StateUpdater<int> s_setReader;
        private static StateUpdater<int> s_setThrower;
        private static StateUpdater<bool> s_setPortalShown;
        private static StateUpdater<string> s_setTheme;
        private static StateUpdater<string> s_setHostValue;
        private static StateUpdater<HostTurn> s_setTurn;
        private static StateUpdater<string> s_setParentValue;
        private static StateUpdater<PortalRowsTurn> s_setPortalRowsTurn;
        private static StateUpdater<int> s_setParentTick;
        private static ComponentFiber s_bailingOwner;
        private static readonly Dictionary<string, VNode> s_portalRowLists = new();
        private static ComponentFiber s_abortingHost;
        private static bool s_fallbackShown;
        private static VisualElement s_portalTarget;
        private static VNode s_shell;
        private static VNode s_countingShell;
        private static int s_countingRenders;
        private static StateUpdater<int> s_setSuspenderOwn;
        private static VelvetTaskCompletionSource<int> s_pendingRead;
        private static ComponentFiber s_hiddenHostFiber;
        private static ComponentFiber s_slicedListFiber;
        private static int s_generation;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_portalTarget = new VisualElement();
            s_shell = V.Div(name: "shell", children: new VNode[] { RowList(ReaderRow) });
            s_countingShell = V.Div(name: "shell", children: new VNode[] { RowList(CountingRow) });
            s_countingRenders = 0;
            s_rowReadings.Clear();
            s_rowFibers.Clear();
            s_reading = null;
            s_lastReading = null;
            s_setLastReader = default;
            s_setDeclaredValue = default;
            s_setSharedItems = default;
            s_fallbackReading = null;
            s_portalReading = null;
            s_setReader = default;
            s_setThrower = default;
            s_setPortalShown = default;
            s_setTheme = default;
            s_setHostValue = default;
            s_setTurn = default;
            s_setParentValue = default;
            s_setPortalRowsTurn = default;
            s_setParentTick = default;
            s_bailingOwner = null;
            s_portalRowLists.Clear();
            s_abortingHost = null;
            s_fallbackShown = false;
            s_setSuspenderOwn = default;
            s_pendingRead = null;
            s_hiddenHostFiber = null;
            s_slicedListFiber = null;
            s_generation = 0;
        }

        [Test]
        public void Given_AMountedRowUnderAMemoizedHost_When_TheProviderAboveTheHostChangesAndTheRowReRendersAlone_Then_TheRowReadsTheNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ThemeOverMemoizedListRender, key: "root"));
            RenderRange(mounted);
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — cleared before the act, so a row that did not render reads null.
            Assert.That(s_reading, Is.EqualTo("dark"));
        }

        [Test]
        public void Given_AHostThatProvidesAroundTheListUnderAFartherProviderOfTheSameContext_When_AScrollMountsARow_Then_TheRowReadsTheHostsValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(OuterOverProvidingHostRender, key: "root"));

            // Act
            RenderRange(mounted);

            // Assert — null where no row rendered.
            Assert.That(s_reading, Is.EqualTo("host"));
        }

        [Test]
        public void Given_AHostThatPutsANewValueAroundTheList_When_AMountedRowThenReRendersAlone_Then_TheRowReadsTheNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostWithItsOwnValueAroundListRender, key: "host"));
            RenderRange(mounted);
            s_setHostValue.Invoke("second");
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_reading, Is.EqualTo("second"));
        }

        [Test]
        public void Given_ARendererThatPutsANewValueAroundTheRow_When_AMountedRowThenReRendersAlone_Then_TheRowReadsTheNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostProvidingInsideItemsRender, key: "host"));
            RenderRange(mounted);
            LayOutViewport(mounted);
            s_setHostValue.Invoke("second");
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_reading, Is.EqualTo("second"));
        }

        [Test]
        public void Given_AHostThatPutsANewValueAroundTheSameElementHoldingAList_When_AScrollThenMountsARow_Then_TheRowReadsTheNewValue()
        {
            // Arrange — the host hands back one element instance around the list, and no row is mounted to read the
            // Provider when its value changes.
            using var mounted = V.Mount(_root, V.Component(HostWithItsOwnValueAroundOneShellRender, key: "host"));
            s_setHostValue.Invoke("second");
            mounted.FlushStateForTest();

            // Act
            RenderRange(mounted);

            // Assert — null where no row rendered.
            Assert.That(s_reading, Is.EqualTo("second"));
        }

        [Test]
        public void Given_AHostRenderThatAbortsAfterItPatchedTheList_When_ARowRendersAlone_Then_ItReadsTheValueTheCommittedRenderPutAroundTheList()
        {
            // Arrange — the row was created under the first value, the host committed the second, and its render of
            // the third aborts.
            using var mounted = V.Mount(_root, V.Component(AbortingHostRender, "first", key: "host"),
                s_ignoringCaughtErrors);
            RenderRange(mounted);
            s_setTurn.Invoke(new HostTurn("second", Aborts: false, Tick: 0));
            mounted.FlushStateForTest();
            s_setTurn.Invoke(new HostTurn("third", Aborts: true, Tick: 0));
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the pass did not abort or the host kept a tree other than the one from before it.
            Assert.That(HostKeptTheValue("second") ? s_reading : null, Is.EqualTo("second"));
        }

        [Test]
        public void Given_ACommittedHostRenderThatDidNotReachTheList_When_ALaterOneChangesTheValueAroundItAndAborts_Then_ARowRenderingAloneReadsTheCommittedValue()
        {
            // Arrange — after the second value, an update changes only a label beside the list, which is handed back
            // by instance.
            using var mounted = V.Mount(_root, V.Component(AbortingHostRender, "first", key: "host"),
                s_ignoringCaughtErrors);
            RenderRange(mounted);
            s_setTurn.Invoke(new HostTurn("second", Aborts: false, Tick: 0));
            mounted.FlushStateForTest();
            s_setTurn.Invoke(new HostTurn("second", Aborts: false, Tick: 1));
            mounted.FlushStateForTest();
            s_setTurn.Invoke(new HostTurn("third", Aborts: true, Tick: 1));
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the pass did not abort or the host kept a tree other than the one from before it.
            Assert.That(HostKeptTheValue("second") ? s_reading : null, Is.EqualTo("second"));
        }

        [Test]
        public void Given_AHostRenderedInlineWhoseCommittedRenderDidNotReachTheList_When_ALaterOneChangesTheValueAroundItAndAborts_Then_ARowRenderingAloneReadsTheCommittedValue()
        {
            // Arrange — the parent's update renders the host in its walk with the second value; the host's own later
            // updates change only a label beside the list, then the value, in a render that aborts.
            using var mounted = V.Mount(_root, V.Component(ParentOfAbortingHostRender, key: "parent"),
                s_ignoringCaughtErrors);
            RenderRange(mounted);
            s_setParentValue.Invoke("second");
            mounted.FlushStateForTest();
            s_setTurn.Invoke(new HostTurn(null, Aborts: false, Tick: 1));
            mounted.FlushStateForTest();
            s_setTurn.Invoke(new HostTurn("third", Aborts: true, Tick: 1));
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the pass did not abort or the host kept a tree other than the one from before it.
            Assert.That(HostKeptTheValue("second") ? s_reading : null, Is.EqualTo("second"));
        }

        [Test]
        public void Given_AHostRenderThatSuspendsAfterItPatchedTheList_When_AScrollMountsARow_Then_TheRowReadsTheValueTheCommittedRenderPutAroundTheList()
        {
            // Arrange — the host commits the second value; its render of the third suspends, and the Suspense above
            // renders it again in its own walk, which writes the host's output before the read suspends again.
            using var mounted = V.Mount(_root, V.Component(SuspenseOverSuspendingHostRender, key: "root"));
            s_setHostValue.Invoke("second");
            mounted.FlushStateForTest();
            s_setHostValue.Invoke("third");
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var notArranged = s_hiddenHostFiber is not { IsOffscreen: true } ? "the host was not hidden" : null;

            // Act
            RenderRange(mounted);

            // Assert — an arrangement that fell short reads as what it missed rather than as the row's value.
            Assert.That(notArranged ?? s_reading, Is.EqualTo("second"));
        }

        [Test]
        public void Given_APortalInARowWhoseListWasPatchedOnlyForItsCapture_When_AnAbortedHostRenderRendersTheRow_Then_ThePortalsChildRenderingAloneReadsTheCommittedRowValue()
        {
            // Arrange — the renderer's second value reaches the row in a committed render; a change around the list, of
            // a context nothing below reads, patches it for its capture alone; and the renderer's third reaches the row
            // in a render that aborts.
            using var mounted = V.Mount(_root, V.Component(AbortingPortalRowsHostRender, key: "host"),
                s_ignoringCaughtErrors);
            RenderRange(mounted);
            LayOutViewport(mounted);
            s_setPortalRowsTurn.Invoke(new PortalRowsTurn("a1", "v2", Aborts: false));
            mounted.FlushStateForTest();
            s_setPortalRowsTurn.Invoke(new PortalRowsTurn("a2", "v2", Aborts: false));
            mounted.FlushStateForTest();
            s_setPortalRowsTurn.Invoke(new PortalRowsTurn("a2", "v3", Aborts: true));
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the pass did not abort or the host kept a tree other than the one from before it.
            Assert.That(HostKeptTheValue("a2") ? s_reading : null, Is.EqualTo("v2"));
        }

        // GREEN_ON_BASE(characterization): the base pushes the child's whole snapshot over whatever the spine found.
        // What this pins is that the spine does not place the declarer at another occurrence of its own node: take
        // the first occurrence and the child reads the Provider around the other.
        [Test]
        public void Given_OneDeclarerNodePlacedUnderTwoProviders_When_ThePortalChildOfTheSecondRendersAlone_Then_ItReadsTheSecondsProvider()
        {
            // Arrange — the second declarer's Portal mounts last, so its child is the last reader rendered.
            using var mounted = V.Mount(_root, V.Component(OuterOverSharedNodeRender, SharedNode.Declarer, key: "root"));
            s_lastReading = null;

            // Act
            s_setLastReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the child did not render.
            Assert.That(s_lastReading, Is.EqualTo("b"));
        }

        [Test]
        public void Given_OneDeclarerNodePlacedUnderTwoProviders_When_TheSecondChangesTheValueAroundItsPortal_Then_ItsChildRenderingAloneReadsTheNewValue()
        {
            // Arrange — the second declarer renders last, so the setter kept is its own.
            using var mounted = V.Mount(_root, V.Component(OuterOverSharedNodeRender, SharedNode.ValuedDeclarer, key: "root"));
            s_setDeclaredValue.Invoke("declared-second");
            mounted.FlushStateForTest();
            s_lastReading = null;

            // Act
            s_setLastReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the child did not render.
            Assert.That(s_lastReading, Is.EqualTo("declared-second"));
        }

        // GREEN_ON_BASE(characterization): the base pushes the row's whole snapshot over whatever the spine found.
        // What this pins is the same for a row of a list whose host is one node placed twice: take the first
        // occurrence and the row reads the Provider around the other.
        [Test]
        public void Given_OneListHostNodePlacedUnderTwoProviders_When_ARowOfTheSecondRendersAlone_Then_ItReadsTheSecondsProvider()
        {
            // Arrange — only the second list is laid out, so the row the items' arrival renders is its, under the
            // second Provider.
            using var mounted = V.Mount(_root, V.Component(OuterOverSharedNodeRender, SharedNode.ListHost, key: "root"));
            LayOutViewport(mounted, _root.Query<ScrollView>().ToList()[1]);
            s_setSharedItems.Invoke(s_oneItem);
            mounted.FlushStateForTest();
            s_lastReading = null;

            // Act
            s_setLastReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the row did not render.
            Assert.That(s_lastReading, Is.EqualTo("b"));
        }

        // GREEN_ON_BASE(characterization): the base pushes the child's whole snapshot over whatever the spine found.
        // What this pins is the stamp going on again at the second edge the spine cannot place: put it on only at the
        // first and the child reads the Provider found between the two.
        [Test]
        public void Given_TwoNodesEachPlacedTwiceOneUnderTheOther_When_ThePortalChildOfTheLastRendersAlone_Then_ItReadsTheProviderAroundTheLast()
        {
            // Arrange — the declarer node is placed twice by each placement of a component that is itself placed twice
            // under a Provider of the same context between them.
            using var mounted = V.Mount(_root, V.Component(OuterOverSharedNodeRender, SharedNode.Stacked, key: "root"));
            s_lastReading = null;

            // Act
            s_setLastReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the child did not render.
            Assert.That(s_lastReading, Is.EqualTo("d"));
        }

        // GREEN_ON_BASE(characterization): the base keeps no capture of a Portal.
        // What this pins is that an owner that bails, while its parent keeps rendering, has nothing left waiting for a
        // commit it will not make: have each capture wait whatever the owner holds, and one waits from then on.
        [Test]
        public void Given_APortalOwnerThatBailsUnderAParentRenderingAgain_When_TheParentHasRenderedSeveralTimes_Then_NoCaptureWaitsOnTheOwner()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ParentOfBailingPortalOwnerRender, key: "parent"));

            // Act
            for (var render = 1; render <= OwnerRenders; render++)
            {
                s_setParentTick.Invoke(render);
                mounted.FlushStateForTest();
            }

            // Assert — read by name, as the base has no such list.
            Assert.That(CapturesWaitingOn(s_bailingOwner), Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base skips the list the host hands back by instance.
        // What this pins is that the patch the list now takes for its capture renders no row: update the list there
        // and the row renders again.
        [Test]
        public void Given_AListTheHostHandsBackByInstanceUnderAProviderNoRowReads_When_ThatProviderChanges_Then_NoRowRendersAgain()
        {
            // Arrange — laid out, so an update of the list renders its range again.
            using var mounted = V.Mount(_root, V.Component(HostWithItsOwnValueAroundCountingShellRender, key: "host"));
            RenderRange(mounted);
            LayOutViewport(mounted);
            var rendersBefore = s_countingRenders;

            // Act
            s_setHostValue.Invoke("second");
            mounted.FlushStateForTest();

            // Assert — -1 where the range rendered no row to begin with.
            Assert.That(rendersBefore > 0 ? s_countingRenders - rendersBefore : -1, Is.EqualTo(0));
        }

        [Test]
        public void Given_AHostThatPutsANewValueAroundAPortal_When_ItsMountedChildThenReRendersAlone_Then_TheChildReadsTheNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostWithItsOwnValueAroundPortalRender, key: "host"));
            s_setHostValue.Invoke("second");
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_reading, Is.EqualTo("second"));
        }

        [Test]
        public void Given_AListAMemoizedHostLeftUnpatchedWhenTheProviderAboveItChanged_When_ARowTheScrollMountsLaterReRendersAlone_Then_TheRowReadsTheNewValue()
        {
            // Arrange — the host bails on equal props, so the element around the list is reused and the list itself
            // is never reached; no range has rendered before the change.
            using var mounted = V.Mount(_root, V.Component(ThemeOverMemoizedDivListRender, key: "root"));
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();
            RenderRange(mounted);
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_reading, Is.EqualTo("dark"));
        }

        [Test]
        public void Given_APortalPlacedDirectlyInARowOfAnUnpatchedList_When_TheProviderAboveChangesAgainAndItsChildReRendersAlone_Then_TheChildReadsTheNewValue()
        {
            // Arrange — the row mounts after the first change, so its Portal's child mounts reading it; the second
            // change leaves the list unpatched again.
            using var mounted = V.Mount(_root, V.Component(ThemeOverMemoizedDivPortalRowsRender, key: "root"));
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();
            RenderRange(mounted);
            s_setTheme.Invoke("dusk");
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_reading, Is.EqualTo("dusk"));
        }

        [Test]
        public void Given_APortalChildsTransitionPassParkedAfterTheProviderAboveAMemoizedHostChanged_When_TheResumeReRendersALaterRow_Then_TheRowReadsTheNewValue()
        {
            // Arrange — the Portal's child re-renders every row on the Transition lane under a budget too small for
            // one row, so the slice that parks leaves the last row to a resume.
            using var mounted = V.Mount(_root, V.Component(ThemeOverMemoizedPortalListRender, key: "root"));
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();
            var mountedRow = s_rowFibers[LastSlicedRow];
            s_generation = 1;
            s_slicedListFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_slicedListFiber.FlushStateWithTinyBudgetForTest();
            var parkedShortOfTheRow = s_slicedListFiber.HasPendingReconcileWorkForTest()
                && s_rowReadings[LastSlicedRow].StartsWith("gen0:", StringComparison.Ordinal);

            // Act
            s_slicedListFiber.DrainTimeSlicedReconcileForTest();

            // Assert — a first slice that reached the row read it without a resume, and a row the resume replaced is
            // not the one that parked, so either reading is a sentinel rather than the row's value.
            var reading = !parkedShortOfTheRow ? "the first slice reached the row"
                : !ReferenceEquals(s_rowFibers[LastSlicedRow], mountedRow) ? "remounted"
                : s_rowReadings[LastSlicedRow];
            Assert.That(reading, Is.EqualTo("gen1:dark"));
        }

        [Test]
        public void Given_ABoundaryThatIsARowOfAMemoizedHost_When_ItsChildThrowsAfterTheProviderAboveTheHostChanged_Then_TheFallbackReadsTheNewValue()
        {
            // Arrange
            using var mounted = V.Mount(
                _root, V.Component(ThemeOverMemoizedGuardListRender, key: "root"), s_ignoringCaughtErrors);
            RenderRange(mounted);
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();

            // Act
            s_setThrower.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where no fallback rendered.
            Assert.That(s_fallbackReading, Is.EqualTo("dark"));
        }

        [Test]
        public void Given_ARowOfAMemoizedHost_When_ItMountsAPortalAfterTheProviderAboveTheHostChanged_Then_ThePortalsChildReadsTheNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ThemeOverMemoizedDeclarerListRender, key: "root"));
            RenderRange(mounted);
            s_setTheme.Invoke("dark");
            mounted.FlushStateForTest();

            // Act
            s_setPortalShown.Invoke(true);
            mounted.FlushStateForTest();

            // Assert — null where the Portal's child never rendered.
            Assert.That(s_portalReading, Is.EqualTo("dark"));
        }

        [Test]
        public void Given_AListHostInAPrimaryItsSuspenseHid_When_AScrollMountsARow_Then_TheRowReadsTheProviderInsideThePrimary()
        {
            // Arrange — the component beside the host suspends again, so the Suspense keeps the primary it had shown,
            // hidden, with the host and the Provider around it; a farther Provider of the same context sits above the
            // component rendering the Suspense.
            using var mounted = V.Mount(_root, V.Component(OuterOverSuspendingMidRender, key: "root"));
            s_setSuspenderOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var notArranged = s_hiddenHostFiber is not { IsOffscreen: true } ? "the primary was not hidden" : null;

            // Act
            RenderRange(mounted);

            // Assert — an arrangement that fell short reads as what it missed rather than as the row's value.
            Assert.That(notArranged ?? s_reading, Is.EqualTo("inner"));
        }

        [Test]
        public void Given_ARendererPuttingAProviderAroundAMemoizedRow_When_TheHostChangesItsValueTwice_Then_TheRowReadsTheSecondChangeToo()
        {
            // Arrange — the host's whole output is the list, so no walk encloses the rows' patches.
            using var mounted = V.Mount(_root, V.Component(HostProvidingAroundMemoizedRowsRender, key: "host"));
            RenderRange(mounted);
            LayOutViewport(mounted);
            s_setHostValue.Invoke("second");
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setHostValue.Invoke("third");
            mounted.FlushStateForTest();

            // Assert — null where the row did not render again.
            Assert.That(s_reading, Is.EqualTo("third"));
        }

        // GREEN_ON_BASE(characterization): the base keeps no output tree of a component rendering a Portal past its renders.
        // What this pins is that a Portal's record keeps none either: give each capture a strong reference to the tree it
        // names, as captures held it before they named it weakly, and two of every five trees counted here stay alive.
        [Test]
        public void Given_ComponentsRenderingAPortalRenderedAgainSeveralTimes_When_ACollectRuns_Then_MostOfTheirEarlierOutputTreesAreCollected()
        {
            // Arrange
            var mounts = new List<MountedTree>();
            try
            {
                var earlier = MountAndRenderPortalOwnersAgain(mounts);

                // Act
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // Assert — well under the two per owner a record holding its captures' trees keeps.
                Assert.That(earlier.Count(tree => tree.IsAlive), Is.LessThan(PortalOwners));
            }
            finally
            {
                foreach (var mounted in mounts) mounted.Dispose();
            }
        }

        // The tree each render of an owner replaces, the one its last render replaces left out, so what is counted is
        // what outlives a further render. Out of line, so no frame of the case's own holds one.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<WeakReference> MountAndRenderPortalOwnersAgain(List<MountedTree> mounts)
        {
            var earlier = new List<WeakReference>();
            for (var owner = 0; owner < PortalOwners; owner++)
            {
                var mounted = V.Mount(new VisualElement(), V.Component(PortalOwnerRender, new OwnerProps(owner), key: "owner"));
                mounts.Add(mounted);
                for (var render = 1; render <= OwnerRenders; render++)
                {
                    if (render < OwnerRenders) earlier.Add(new WeakReference(s_portalOwners[owner].PreviousTree));
                    s_setOwnerTicks[owner].Invoke(render);
                    mounted.FlushStateForTest();
                }
            }
            return earlier;
        }

        private void RenderRange(MountedTree mounted)
            => mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

        // The viewport height a geometry pass would have left, without which the host's update renders no range
        // (FiberVirtualListController.ForceRefresh), so the rows are never handed the renderer's new node.
        private static int CapturesWaitingOn(ComponentFiber fiber)
            => typeof(ComponentFiber)
                .GetProperty("CapturesAwaitingCommit", BindingFlags.NonPublic | BindingFlags.Instance)?
                .GetValue(fiber) is System.Collections.ICollection waiting ? waiting.Count : 0;

        private void LayOutViewport(MountedTree mounted) => LayOutViewport(mounted, _root.Q<ScrollView>());

        private static void LayOutViewport(MountedTree mounted, ScrollView scrollView)
            => typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(mounted.Root.Reconciler.Context.VirtualListControllers[scrollView], 200f);

        #region Components

        [Component(Compiler = false)]
        private static VNode ParentOfBailingPortalOwnerRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setParentTick = setTick;
            return V.Div(name: "parent-" + tick, children: new VNode[] { V.Component(BailingPortalOwnerRender, key: "owner") });
        }

        // Bails on its equal props, so each render of the parent walks the output it rendered first.
        [Component(Compiler = false, Memoize = true)]
        private static VNode BailingPortalOwnerRender()
        {
            s_bailingOwner = FiberAmbientStack.Current;
            return V.Portal(s_portalTarget, new VNode[] { V.Label(text: "in the portal") });
        }

        private enum SharedNode
        {
            Declarer,
            ValuedDeclarer,
            ListHost,
            Stacked,
        }

        [Component(Compiler = false)]
        private static VNode OuterOverSharedNodeRender(SharedNode shape)
            => V.Provider(s_theme, "outer", new VNode[] { V.Component(SharedNodeMidRender, shape, key: "mid") });

        // One node instance, built once per render, placed in two elements under two Providers.
        [Component(Compiler = false)]
        private static VNode SharedNodeMidRender(SharedNode shape)
        {
            var (items, setItems) = Hooks.UseState<IReadOnlyList<string>>(Array.Empty<string>());
            s_setSharedItems = setItems;
            var shared = shape switch
            {
                SharedNode.Declarer => V.Component(LastReaderPortalRender, key: "d"),
                SharedNode.ValuedDeclarer => V.Component(ValuedLastReaderPortalRender, key: "d"),
                SharedNode.ListHost => V.Component(LastReaderListRender, items, key: "d"),
                _ => V.Component(BetweenOverSharedDeclarerRender, key: "d"),
            };
            return PlacedTwice(shared, "a", "b");
        }

        private static VNode PlacedTwice(VNode shared, string first, string second)
            => V.Fragment(children: new VNode[]
            {
                V.Div(name: first, children: new VNode[] { V.Provider(s_theme, first, new[] { shared }) }),
                V.Div(name: second, children: new VNode[] { V.Provider(s_theme, second, new[] { shared }) }),
            });

        [Component(Compiler = false)]
        private static VNode BetweenOverSharedDeclarerRender()
            => V.Provider(s_theme, "between", new VNode[] { V.Component(SharedDeclarerInsideRender, key: "x") });

        [Component(Compiler = false)]
        private static VNode SharedDeclarerInsideRender()
            => PlacedTwice(V.Component(LastReaderPortalRender, key: "e"), "c", "d");

        [Component(Compiler = false)]
        private static VNode LastReaderPortalRender()
            => V.Portal(s_portalTarget, new VNode[] { V.Component(LastReaderRender, key: "reader") });

        [Component(Compiler = false)]
        private static VNode ValuedLastReaderPortalRender()
        {
            var (value, setValue) = Hooks.UseState("declared-first");
            s_setDeclaredValue = setValue;
            return V.Provider(s_theme, value, new VNode[]
            {
                V.Portal(s_portalTarget, new VNode[] { V.Component(LastReaderRender, key: "reader") }),
            });
        }

        [Component(Compiler = false)]
        private static VNode LastReaderListRender(IReadOnlyList<string> items)
            => V.VirtualList(items, id => id, itemHeight: 50f, renderer: id => V.Component(LastReaderRender, key: id),
                overscan: 0);

        // Keeps the setter and the reading of whichever reader rendered last.
        [Component(Compiler = false)]
        private static VNode LastReaderRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setLastReader = setTick;
            s_lastReading = Hooks.UseContext(s_theme);
            return V.Label(text: s_lastReading + tick);
        }

        private sealed record SlicedRowProps(int Index, int Generation);

        private sealed record OwnerProps(int Index);

        private static bool HostKeptTheValue(string value)
            => s_fallbackShown && (s_abortingHost?.PreviousTree?[0] as ContextProviderNode<string>)?.Value == value;

        // The value the host puts around the list in place of the one it is given, whether a ref cleanup beside the
        // list fails in this render's walk, and a label's text, which changes the output without touching the list.
        private sealed record HostTurn(string Value, bool Aborts, int Tick);

        // The list comes ahead of the boundary in the walk, so it is patched before the ref cleanup fails.
        [Component(Compiler = false)]
        private static VNode AbortingHostRender(string given)
        {
            s_abortingHost = FiberAmbientStack.Current;
            var (turn, setTurn) = Hooks.UseState(new HostTurn(null, Aborts: false, Tick: 0));
            s_setTurn = setTurn;
            return V.Provider(s_theme, turn.Value ?? given, new VNode[]
            {
                s_shell,
                V.Div(key: "boundary", children: new VNode[]
                {
                    V.Component(RefDroppingBoundary, turn.Aborts, key: "b"),
                }),
                V.Label(text: turn.Tick.ToString()),
            });
        }

        [Component(Compiler = false)]
        private static VNode ParentOfAbortingHostRender()
        {
            var (value, setValue) = Hooks.UseState("first");
            s_setParentValue = setValue;
            return V.Div(name: "parent", children: new VNode[] { V.Component(AbortingHostRender, value, key: "host") });
        }

        // The value of another context around the list, the one the renderer puts around each row's Portal, and
        // whether a ref cleanup after the list fails in this render's walk.
        private sealed record PortalRowsTurn(string Around, string Renderer, bool Aborts);

        // One list per renderer value, so a turn that keeps it hands the list back by instance.
        [Component(Compiler = false)]
        private static VNode AbortingPortalRowsHostRender()
        {
            s_abortingHost = FiberAmbientStack.Current;
            var (turn, setTurn) = Hooks.UseState(new PortalRowsTurn("a1", "v1", Aborts: false));
            s_setPortalRowsTurn = setTurn;
            var value = turn.Renderer;
            if (!s_portalRowLists.TryGetValue(value, out var list))
            {
                s_portalRowLists[value] = list = RowList(item => V.Provider(s_theme, value, new VNode[]
                {
                    V.Portal(s_portalTarget, new VNode[] { V.Component(ReaderRender, key: item) }),
                }));
            }
            return V.Provider(s_around, turn.Around, new VNode[]
            {
                V.Div(name: "rows", children: new[] { list }),
                V.Div(key: "boundary", children: new VNode[]
                {
                    V.Component(RefDroppingBoundary, turn.Aborts, key: "b"),
                }),
            });
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode RefDroppingBoundary(bool aborts)
        {
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Label(name: "fallback", text: "caught");
            });
            return V.Component(RefDroppingChild, aborts, key: "child");
        }

        // A ref removed outright is cleaned up where the patch drops it — see ReconcilerContext.SyncRefCallback.
        [Component(Compiler = false)]
        private static VNode RefDroppingChild(bool aborts)
            => V.Label(text: "child", refCallback: aborts ? null : _ => () => throw new Exception(ThrowerError));

        [Component(Compiler = false)]
        private static VNode SuspenseOverSuspendingHostRender()
            => V.Suspense(fallback: V.Label(text: "loading"), children: new VNode[]
            {
                V.Component(HostSuspendingAfterItsListRender, key: "host"),
            });

        // The list comes ahead of the reader in the walk, so it is patched before the read suspends.
        [Component(Compiler = false)]
        private static VNode HostSuspendingAfterItsListRender()
        {
            s_hiddenHostFiber = FiberAmbientStack.Current;
            var (value, setValue) = Hooks.UseState("first");
            s_setHostValue = setValue;
            return V.Provider(s_theme, value, new VNode[]
            {
                s_shell,
                V.Div(key: "after", children: new VNode[] { V.Component(PendingForTheThirdRender, value, key: "read") }),
            });
        }

        // Pending for the third value and read at once for any other.
        [Component(Compiler = false)]
        private static VNode PendingForTheThirdRender(string value)
        {
            var read = Hooks.Use<int>(_ => value != "third"
                ? VelvetTask.FromResult(0)
                : (s_pendingRead = new VelvetTaskCompletionSource<int>()).Task, value);
            return V.Label(text: "read:" + read);
        }

        [Component(Compiler = false)]
        private static VNode OuterOverSuspendingMidRender()
            => V.Provider(s_theme, "outer", new VNode[] { V.Component(SuspendingMidRender, key: "mid") });

        [Component(Compiler = false)]
        private static VNode SuspendingMidRender()
            => V.Suspense(fallback: V.Label(text: "loading"), children: new VNode[]
            {
                V.Component(ResuspendingRender, key: "suspender"),
                V.Provider(s_theme, "inner", new VNode[] { V.Component(HiddenListHostRender, key: "host") }),
            });

        // Reads a value at once until its own update asks for one that is still pending.
        [Component(Compiler = false)]
        private static VNode ResuspendingRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setSuspenderOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : (s_pendingRead = new VelvetTaskCompletionSource<int>()).Task, own);
            return V.Label(text: "suspender:" + value);
        }

        [Component(Compiler = false)]
        private static VNode HiddenListHostRender()
        {
            s_hiddenHostFiber = FiberAmbientStack.Current;
            return RowList(ReaderRow);
        }

        // A new output tree each render, which reaches the Portal and so captures under it.
        [Component(Compiler = false)]
        private static VNode PortalOwnerRender(OwnerProps props)
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_portalOwners[props.Index] = FiberAmbientStack.Current;
            s_setOwnerTicks[props.Index] = setTick;
            return V.Fragment(children: new VNode[]
            {
                V.Label(text: tick.ToString()),
                V.Portal(s_portalTarget, new VNode[] { V.Label(text: "in the portal") }),
            });
        }

        [Component(Compiler = false)]
        private static VNode HostProvidingAroundMemoizedRowsRender()
        {
            var (value, setValue) = Hooks.UseState("first");
            s_setHostValue = setValue;
            return RowList(item => V.Provider(s_theme, value, new VNode[]
            {
                V.Component(MemoizedReaderRender, key: item),
            }));
        }

        // Bails on its equal props, so only the Provider's change propagating to it renders it again.
        [Component(Compiler = false, Memoize = true)]
        private static VNode MemoizedReaderRender()
        {
            s_reading = Hooks.UseContext(s_theme);
            return V.Label(text: s_reading);
        }

        private static VNode RowList(Func<string, VNode> renderer)
            => V.VirtualList(s_oneItem, item => item, itemHeight: 50f, renderer: renderer, overscan: 0);

        private static VNode ReaderRow(string item) => V.Component(ReaderRender, key: item);

        [Component(Compiler = false)]
        private static VNode ReaderRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setReader = setTick;
            s_reading = Hooks.UseContext(s_theme);
            return V.Label(text: s_reading + tick);
        }

        // The Provider above a memoized host, whose value a test changes.
        private static VNode UseThemeAround(Func<VNode> host)
        {
            var (theme, setTheme) = Hooks.UseState("light");
            s_setTheme = setTheme;
            return V.Provider(s_theme, theme, new VNode[] { V.Component(host, key: "host") });
        }

        [Component(Compiler = false)]
        private static VNode ThemeOverMemoizedListRender() => UseThemeAround(MemoizedListHostRender);

        // The list is the host's whole output, so the walk that reuses the host still reaches it.
        [Component(Compiler = false, Memoize = true)]
        private static VNode MemoizedListHostRender() => RowList(ReaderRow);

        [Component(Compiler = false)]
        private static VNode OuterOverProvidingHostRender()
            => V.Provider(s_theme, "outer", new VNode[] { V.Component(HostProvidingAroundListRender, key: "host") });

        [Component(Compiler = false)]
        private static VNode HostProvidingAroundListRender()
            => V.Provider(s_theme, "host", new VNode[] { RowList(ReaderRow) });

        [Component(Compiler = false)]
        private static VNode HostWithItsOwnValueAroundListRender()
        {
            var (value, setValue) = Hooks.UseState("first");
            s_setHostValue = setValue;
            return V.Provider(s_theme, value, new VNode[] { RowList(ReaderRow) });
        }

        [Component(Compiler = false)]
        private static VNode HostProvidingInsideItemsRender()
        {
            var (value, setValue) = Hooks.UseState("first");
            s_setHostValue = setValue;
            return RowList(item => V.Provider(s_theme, value, new VNode[] { V.Component(ReaderRender, key: item) }));
        }

        [Component(Compiler = false)]
        private static VNode HostWithItsOwnValueAroundCountingShellRender()
        {
            var (value, setValue) = Hooks.UseState("first");
            s_setHostValue = setValue;
            return V.Provider(s_theme, value, new[] { s_countingShell });
        }

        private static VNode CountingRow(string item) => V.Component(CountingRowRender, key: item);

        [Component(Compiler = false)]
        private static VNode CountingRowRender()
        {
            s_countingRenders++;
            return V.Label(text: "row");
        }

        [Component(Compiler = false)]
        private static VNode HostWithItsOwnValueAroundOneShellRender()
        {
            var (value, setValue) = Hooks.UseState("first");
            s_setHostValue = setValue;
            return V.Provider(s_theme, value, new[] { s_shell });
        }

        [Component(Compiler = false)]
        private static VNode HostWithItsOwnValueAroundPortalRender()
        {
            var (value, setValue) = Hooks.UseState("first");
            s_setHostValue = setValue;
            return V.Provider(s_theme, value, new VNode[]
            {
                V.Portal(s_portalTarget, new VNode[] { V.Component(ReaderRender, key: "child") }),
            });
        }

        [Component(Compiler = false)]
        private static VNode ThemeOverMemoizedDivListRender() => UseThemeAround(MemoizedDivListHostRender);

        [Component(Compiler = false, Memoize = true)]
        private static VNode MemoizedDivListHostRender()
            => V.Div(name: "shell", children: new VNode[] { RowList(ReaderRow) });

        [Component(Compiler = false)]
        private static VNode ThemeOverMemoizedDivPortalRowsRender() => UseThemeAround(MemoizedDivPortalRowsHostRender);

        [Component(Compiler = false, Memoize = true)]
        private static VNode MemoizedDivPortalRowsHostRender()
            => V.Div(name: "shell", children: new VNode[]
            {
                RowList(item => V.Portal(s_portalTarget, new VNode[] { V.Component(ReaderRender, key: item) })),
            });

        [Component(Compiler = false)]
        private static VNode ThemeOverMemoizedPortalListRender() => UseThemeAround(MemoizedPortalListHostRender);

        [Component(Compiler = false, Memoize = true)]
        private static VNode MemoizedPortalListHostRender()
            => V.Portal(s_portalTarget, new VNode[] { V.Component(SlicedListRender, key: "list") });

        // Keyed elements at the top level keep the list's own reconcile on the path that time-slices.
        [Component(Compiler = false)]
        private static VNode SlicedListRender()
        {
            s_slicedListFiber = FiberAmbientStack.Current;
            var rows = new VNode[SlicedRows];
            for (var i = 0; i < SlicedRows; i++)
            {
                rows[i] = V.Div(key: "row" + i, children: new VNode[]
                {
                    V.Component(SlicedRowRender, new SlicedRowProps(i, s_generation), key: "row"),
                });
            }
            return V.Fragment(children: rows);
        }

        [Component(Compiler = false)]
        private static VNode SlicedRowRender(SlicedRowProps props)
        {
            var theme = Hooks.UseContext(s_theme);
            s_rowFibers[props.Index] = FiberAmbientStack.Current;
            s_rowReadings[props.Index] = "gen" + props.Generation + ":" + theme;
            return V.Label(text: s_rowReadings[props.Index]);
        }

        [Component(Compiler = false)]
        private static VNode ThemeOverMemoizedGuardListRender() => UseThemeAround(MemoizedGuardListHostRender);

        [Component(Compiler = false, Memoize = true)]
        private static VNode MemoizedGuardListHostRender()
            => RowList(item => V.Component(GuardRowRender, key: item));

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode GuardRowRender()
        {
            Hooks.UseFallback(_ => V.Component(FallbackReaderRender, key: "fallback"));
            return V.Component(ThrowOnUpdateRender, key: "thrower");
        }

        [Component(Compiler = false)]
        private static VNode ThrowOnUpdateRender()
        {
            var (update, setUpdate) = Hooks.UseState(0);
            s_setThrower = setUpdate;
            if (update > 0) throw new InvalidOperationException(ThrowerError);
            return V.Label(text: "thrower");
        }

        [Component(Compiler = false)]
        private static VNode FallbackReaderRender()
        {
            s_fallbackReading = Hooks.UseContext(s_theme);
            return V.Label(text: s_fallbackReading);
        }

        [Component(Compiler = false)]
        private static VNode ThemeOverMemoizedDeclarerListRender() => UseThemeAround(MemoizedDeclarerListHostRender);

        [Component(Compiler = false, Memoize = true)]
        private static VNode MemoizedDeclarerListHostRender()
            => RowList(item => V.Component(PortalDeclarerRender, key: item));

        [Component(Compiler = false)]
        private static VNode PortalDeclarerRender()
        {
            var (shown, setShown) = Hooks.UseState(false);
            s_setPortalShown = setShown;
            return shown
                ? V.Portal(s_portalTarget, new VNode[] { V.Component(PortalReaderRender, key: "reader") })
                : V.Label(text: "no portal");
        }

        [Component(Compiler = false)]
        private static VNode PortalReaderRender()
        {
            s_portalReading = Hooks.UseContext(s_theme);
            return V.Label(text: s_portalReading);
        }

        #endregion
    }

    /// <summary>
    /// A row a scroll mounts inside a presence child its presence's first render created under <c>initial: false</c>
    /// plays no mount enter; and where an exiting presence child holds a component, a Portal's declarer, a Portal's
    /// children or a list's host, that component, the Portal's child and a row read the nearest Provider above them as
    /// it is now, inside the exiting child or above the presence, whichever of two presences at one SlotPath holds
    /// them, a pass that aborted after expanding both included.
    /// </summary>
    [TestFixture]
    internal sealed class DetachedContextMotionTests : MotionSimulatedPanelTestsBase
    {
        private static readonly ComponentContext<string> s_theme = ComponentContext<string>.Create("default");

        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private static readonly StyleTransitionConfig s_child = new() { DurationSec = 0.05f };

        private static readonly Dictionary<string, string> s_taggedReadings = new();
        private static readonly Dictionary<string, StateUpdater<int>> s_taggedSetters = new();

        private static string s_reading;
        private static StateUpdater<int> s_setReader;
        private static StateUpdater<bool> s_setPresent;
        private static StateUpdater<bool> s_setFlipped;
        private static StateUpdater<string> s_setHostValue;
        private static StateUpdater<string> s_setInner;
        private static StateUpdater<string> s_setOuter;
        private static StateUpdater<string> s_setInside;
        private static ComponentFiber s_presencesFiber;
        private static bool s_fallbackShown;
        private static VisualElement s_portalTarget;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_reading = null;
            s_setReader = default;
            s_setPresent = default;
            s_setFlipped = default;
            s_setHostValue = default;
            s_setInner = default;
            s_setOuter = default;
            s_setInside = default;
            s_presencesFiber = null;
            s_fallbackShown = false;
            s_taggedReadings.Clear();
            s_taggedSetters.Clear();
            s_portalTarget = new VisualElement();
            Root.Add(s_portalTarget);
        }

        [Test]
        public void Given_AComponentInsideAnExitingPresenceChild_When_ItRendersOnItsOwn_Then_ItReadsTheProviderAboveThePresence()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(PresenceOverReaderRender, key: "outer"));
            s_setPresent.Invoke(false);
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the component did not render.
            Assert.That(s_reading, Is.EqualTo("outer"));
        }

        [Test]
        public void Given_APortalDeclaredUnderAProviderInsideAnExitingPresenceChild_When_ThePortalsChildRendersAlone_Then_ItReadsThatProvidersValueNow()
        {
            // Arrange — the Provider's value changes while the child is present, after the Portal's child was created
            // under the first one, and the exit starts after.
            using var mounted = V.Mount(Root, V.Component(OuterOverLeavingPortalMidRender, key: "outer"));
            s_setInner.Invoke("inner-second");
            mounted.FlushStateForTest();
            s_setPresent.Invoke(false);
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the child did not render.
            Assert.That(s_reading, Is.EqualTo("inner-second"));
        }

        [Test]
        public void Given_AListHostUnderAProviderInsideAnExitingPresenceChild_When_AMountedRowRendersAlone_Then_ItReadsThatProvidersValueNow()
        {
            // Arrange — the row is created under the Provider's first value, which changes while the child is present.
            using var mounted = V.Mount(Root, V.Component(OuterOverLeavingListMidRender, key: "outer"));
            mounted.Root.Reconciler.Context.VirtualListControllers[Root.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            s_setInner.Invoke("inner-second");
            mounted.FlushStateForTest();
            s_setPresent.Invoke(false);
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the row did not render.
            Assert.That(s_reading, Is.EqualTo("inner-second"));
        }

        [Test]
        public void Given_TwoPresencesAtOneSlotPathUnderDifferentElements_When_AComponentInTheSecondsExitingChildRendersAlone_Then_ItReadsTheProviderAboveTheSecond()
        {
            // Arrange — each element's children reconcile in a walk of their own, so both presences sit at one SlotPath
            // of the component, and each exiting child holds the same component at the same place.
            using var mounted = V.Mount(Root, V.Component(TwoLeavingPresencesRender, key: "outer"));
            s_setPresent.Invoke(false);
            mounted.FlushStateForTest();
            s_taggedReadings["second"] = null;

            // Act
            s_taggedSetters["second"].Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the component did not render.
            Assert.That(s_taggedReadings["second"], Is.EqualTo("second"));
        }

        [Test]
        public void Given_AListHostInAnExitingChildOfOneOfTwoPresencesAtOneSlotPath_When_ARowRendersAloneAfterAnAbortedPass_Then_ItReadsTheProviderInsideTheChild()
        {
            // Arrange — the value inside the first child changes while it is present, after the row was created; the
            // flip's pass after the exit aborts on a ref cleanup that fails in its walk, once both presences expanded.
            using var mounted = V.Mount(Root, V.Component(OuterOverAbortingPresencesRender, key: "outer"),
                CaughtErrors.Unlogged);
            mounted.Root.Reconciler.Context.VirtualListControllers[Root.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            s_setInner.Invoke("inner-second");
            mounted.FlushStateForTest();
            s_setPresent.Invoke(false);
            mounted.FlushStateForTest();
            s_setFlipped.Invoke(true);
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the pass did not abort, the component's tree is not the one from before it, or the
            // two states do not share a SlotPath.
            Assert.That(AbortLeftTwoStatesAtOneSlot(mounted) ? s_reading : null, Is.EqualTo("inner-second"));
        }

        [Test]
        public void Given_AListHostWithItsOwnValueInAnExitingChildOfOneOfTwoPresencesAtOneSlotPath_When_ItChangesThatValueAfterAnAbortedPass_Then_ARowRenderingAloneReadsTheNewValue()
        {
            // Arrange — the flip's pass aborts as above, then the host puts a new value around the list.
            using var mounted = V.Mount(Root, V.Component(OuterOverAbortingValuedPresencesRender, key: "outer"),
                CaughtErrors.Unlogged);
            mounted.Root.Reconciler.Context.VirtualListControllers[Root.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            s_setPresent.Invoke(false);
            mounted.FlushStateForTest();
            s_setFlipped.Invoke(true);
            mounted.FlushStateForTest();
            s_setHostValue.Invoke("host-second");
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the pass did not abort, the component's tree is not the one from before it, or the
            // two states do not share a SlotPath.
            Assert.That(AbortLeftTwoStatesAtOneSlot(mounted) ? s_reading : null, Is.EqualTo("host-second"));
        }

        // GREEN_ON_BASE(characterization): the base mounts these rows under the whole snapshot of the list.
        // What this pins is that the enter flags still reach them now that what a capture pushes on top is only what
        // the host put around the list: push no snapshot of the capture under the range's spine and the row enters.
        [Test]
        public void Given_AListInAPresenceChildCreatedUnderInitialFalse_When_AScrollMountsARowMotion_Then_ThatMotionDoesNotEnter()
        {
            // Arrange
            using var mounted = V.Mount(Root, V.Component(PresenceListHostRender, key: "host"));

            // Act
            mounted.Root.Reconciler.Context.VirtualListControllers[Root.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert — at rest from the start rather than at its initial pose.
            Assert.That(Root.Q<VisualElement>("r0").ClassListContains("opacity-100"), Is.True);
        }

        [Test]
        public void Given_APortalChildWhosePresenceChildIsExiting_When_ItReRendersAlone_Then_ItReadsTheProviderInsideThePortalAsItIsNow()
        {
            // Arrange — the Provider inside the Portal changes value while the child is present, so the Portal's
            // children when the child was created hold the first one.
            using var mounted = V.Mount(Root, V.Component(ExitingPortalChildHostRender, key: "host"));
            s_setInside.Invoke("inside-second");
            mounted.FlushStateForTest();
            s_setPresent.Invoke(false);
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the child did not render.
            Assert.That(s_reading, Is.EqualTo("inside-second"));
        }

        [Test]
        public void Given_APortalDeclaredInsideAnExitingPresenceChild_When_ThePortalsChildReRendersAlone_Then_ItReadsTheProviderAboveThePresenceAsItIsNow()
        {
            // Arrange — the Provider above the presence changes value while the child is present, after the Portal's
            // child was created under the first one.
            using var mounted = V.Mount(Root, V.Component(PresenceOverPortalDeclarerRender, key: "outer"));
            s_setOuter.Invoke("outer-second");
            mounted.FlushStateForTest();
            s_setPresent.Invoke(false);
            mounted.FlushStateForTest();
            s_reading = null;

            // Act
            s_setReader.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — null where the child did not render.
            Assert.That(s_reading, Is.EqualTo("outer-second"));
        }

        [Test]
        public void Given_AListWhoseHostIsInAnExitingPresenceChild_When_AScrollMountsARow_Then_TheRowReadsTheProviderAboveThePresenceAsItIsNow()
        {
            // Arrange — the Provider above the presence changes value while the child is present, and a farther one of
            // the same context sits above the component rendering the presence.
            using var mounted = V.Mount(Root, V.Component(FarOverPresenceListHostRender, key: "far"));
            s_setOuter.Invoke("outer-second");
            mounted.FlushStateForTest();
            s_setPresent.Invoke(false);
            mounted.FlushStateForTest();

            // Act
            mounted.Root.Reconciler.Context.VirtualListControllers[Root.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert — null where no row rendered.
            Assert.That(s_reading, Is.EqualTo("outer-second"));
        }

        // The exit holds the presence child until the panel ticks, which nothing here does.
        private static VNode UseLeavingAround(VNode child)
        {
            var (present, setPresent) = Hooks.UseState(true);
            var (outer, setOuter) = Hooks.UseState("outer");
            s_setPresent = setPresent;
            s_setOuter = setOuter;
            return V.Provider(s_theme, outer, new VNode[]
            {
                V.AnimatePresence(key: "presence", children: new VNode[]
                {
                    present
                        ? V.Motion(key: "leaving", name: "leaving", variants: s_fade, initial: "hidden",
                            animate: "visible", exit: "hidden", transition: s_child, children: new[] { child })
                        : null,
                }),
            });
        }

        [Component(Compiler = false)]
        private static VNode PresenceOverReaderRender() => UseLeavingAround(V.Component(ReaderRender, key: "reader"));

        [Component(Compiler = false)]
        private static VNode OuterOverLeavingPortalMidRender()
            => V.Provider(s_theme, "outer", new VNode[] { V.Component(LeavingPortalMidRender, key: "mid") });

        [Component(Compiler = false)]
        private static VNode LeavingPortalMidRender() => UseLeavingUnderInner(V.Component(PortalDeclarerRender, key: "declarer"));

        [Component(Compiler = false)]
        private static VNode OuterOverLeavingListMidRender()
            => V.Provider(s_theme, "outer", new VNode[] { V.Component(LeavingListMidRender, key: "mid") });

        [Component(Compiler = false)]
        private static VNode LeavingListMidRender() => UseLeavingUnderInner(V.Component(ListHostRender, key: "host"));

        // The exit holds the presence child until the panel ticks, which nothing here does.
        private static VNode UseLeavingUnderInner(VNode child)
        {
            var (present, setPresent) = Hooks.UseState(true);
            var (inner, setInner) = Hooks.UseState("inner");
            s_setPresent = setPresent;
            s_setInner = setInner;
            return V.AnimatePresence(key: "presence", children: new VNode[]
            {
                present
                    ? V.Motion(key: "dlg", name: "dlg", variants: s_fade, initial: "hidden", animate: "visible",
                        exit: "hidden", transition: s_child, children: new VNode[]
                        {
                            V.Provider(s_theme, inner, new[] { child }),
                        })
                    : null,
            });
        }

        private sealed record TagProps(string Tag);

        [Component(Compiler = false)]
        private static VNode TwoLeavingPresencesRender()
        {
            var (present, setPresent) = Hooks.UseState(true);
            s_setPresent = setPresent;
            return V.Fragment(children: new VNode[] { LeavingSide("first", present), LeavingSide("second", present) });
        }

        private static VNode LeavingSide(string value, bool present)
            => V.Provider(s_theme, value, new VNode[]
            {
                V.Div(name: value, children: new VNode[]
                {
                    V.AnimatePresence(key: "presence", children: new VNode[]
                    {
                        present
                            ? V.Motion(key: "m", name: "m-" + value, variants: s_fade, initial: "hidden",
                                animate: "visible", exit: "hidden", transition: s_child, children: new VNode[]
                                {
                                    V.Component(TaggedReaderRender, new TagProps(value), key: "r"),
                                })
                            : null,
                    }),
                }),
            });

        private static bool AbortLeftTwoStatesAtOneSlot(MountedTree mounted)
            => s_fallbackShown
                && (s_presencesFiber?.PreviousTree?[0] as BaseElementNode)?.Name == "initial"
                && mounted.Root.Reconciler.Context.PresenceStates.Keys
                    .Where(key => ReferenceEquals(key.boundary, s_presencesFiber))
                    .GroupBy(key => key.presenceKey)
                    .Any(slot => slot.Count() > 1);

        [Component(Compiler = false)]
        private static VNode OuterOverAbortingPresencesRender()
            => V.Provider(s_theme, "outer", new VNode[] { V.Component(AbortingPresencesRender, key: "presences") });

        [Component(Compiler = false)]
        private static VNode AbortingPresencesRender() => UseAbortingPresences(V.Component(ListHostRender, key: "host"));

        [Component(Compiler = false)]
        private static VNode OuterOverAbortingValuedPresencesRender()
            => V.Provider(s_theme, "outer", new VNode[]
            {
                V.Component(AbortingValuedPresencesRender, key: "presences"),
            });

        [Component(Compiler = false)]
        private static VNode AbortingValuedPresencesRender()
            => UseAbortingPresences(V.Component(ValuedListHostRender, key: "host"));

        [Component(Compiler = false)]
        private static VNode ValuedListHostRender()
        {
            var (value, setValue) = Hooks.UseState("host-first");
            s_setHostValue = setValue;
            return V.Provider(s_theme, value, new VNode[] { ReaderRows() });
        }

        // The host's presence comes first in the walk and the boundary last, so both presences expand before the
        // ref cleanup fails.
        private static VNode UseAbortingPresences(VNode host)
        {
            s_presencesFiber = FiberAmbientStack.Current;
            var (present, setPresent) = Hooks.UseState(true);
            var (flipped, setFlipped) = Hooks.UseState(false);
            var (inner, setInner) = Hooks.UseState("inner");
            s_setPresent = setPresent;
            s_setFlipped = setFlipped;
            s_setInner = setInner;
            return V.Div(name: flipped ? "flipped" : "initial", children: new VNode[]
            {
                LeavingAround("first", present, V.Provider(s_theme, inner, new[] { host })),
                LeavingAround("second", present, V.Label(text: "second")),
                V.Div(key: "boundary", children: new VNode[] { V.Component(RefDroppingBoundary, flipped, key: "b") }),
            });
        }

        private static VNode LeavingAround(string value, bool present, VNode content)
            => V.Div(key: value, name: value, children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: new VNode[]
                {
                    present
                        ? V.Motion(key: "m", name: "m-" + value, variants: s_fade, initial: "hidden",
                            animate: "visible", exit: "hidden", transition: s_child, children: new[] { content })
                        : null,
                }),
            });

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode RefDroppingBoundary(bool flipped)
        {
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Label(name: "fallback", text: "caught");
            });
            return V.Component(RefDroppingChild, flipped, key: "child");
        }

        // A ref removed outright is cleaned up where the patch drops it — see ReconcilerContext.SyncRefCallback.
        [Component(Compiler = false)]
        private static VNode RefDroppingChild(bool flipped)
            => V.Label(text: "child", refCallback: flipped ? null : _ => () => throw new Exception("ref cleanup"));

        [Component(Compiler = false)]
        private static VNode TaggedReaderRender(TagProps props)
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_taggedSetters[props.Tag] = setTick;
            s_taggedReadings[props.Tag] = Hooks.UseContext(s_theme);
            return V.Label(text: s_taggedReadings[props.Tag] + tick);
        }

        [Component(Compiler = false)]
        private static VNode PresenceOverPortalDeclarerRender()
            => UseLeavingAround(V.Component(PortalDeclarerRender, key: "declarer"));

        [Component(Compiler = false)]
        private static VNode PortalDeclarerRender()
            => V.Portal(s_portalTarget, new VNode[] { V.Component(ReaderRender, key: "reader") });

        [Component(Compiler = false)]
        private static VNode FarOverPresenceListHostRender()
            => V.Provider(s_theme, "far", new VNode[] { V.Component(PresenceOverListHostRender, key: "outer") });

        [Component(Compiler = false)]
        private static VNode PresenceOverListHostRender() => UseLeavingAround(V.Component(ListHostRender, key: "host"));

        [Component(Compiler = false)]
        private static VNode ListHostRender() => ReaderRows();

        private static VNode ReaderRows()
            => V.VirtualList(new List<string> { "r0" }, id => id, itemHeight: 30f,
                renderer: id => V.Component(ReaderRender, key: id), overscan: 0);

        [Component(Compiler = false)]
        private static VNode PresenceListHostRender()
            => V.AnimatePresence(key: "presence", initial: false, children: new VNode[]
            {
                V.Div(key: "a", children: new VNode[]
                {
                    V.VirtualList(new List<string> { "r0" }, id => id, itemHeight: 30f,
                        renderer: id => V.Motion(key: id, name: id, variants: s_fade, initial: "hidden",
                            animate: "visible", transition: s_child),
                        overscan: 0),
                }),
            });

        [Component(Compiler = false)]
        private static VNode ExitingPortalChildHostRender()
        {
            var (present, setPresent) = Hooks.UseState(true);
            var (inside, setInside) = Hooks.UseState("inside");
            s_setPresent = setPresent;
            s_setInside = setInside;
            return V.Portal(s_portalTarget, new VNode[]
            {
                V.Provider(s_theme, inside, new VNode[]
                {
                    V.AnimatePresence(key: "presence", children: new VNode[]
                    {
                        present
                            ? V.Motion(key: "leaving", name: "leaving", variants: s_fade, initial: "hidden",
                                animate: "visible", exit: "hidden", transition: s_child,
                                children: new VNode[] { V.Component(ReaderRender, key: "reader") })
                            : null,
                    }),
                }),
            });
        }

        [Component(Compiler = false)]
        private static VNode ReaderRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setReader = setTick;
            s_reading = Hooks.UseContext(s_theme);
            return V.Label(text: s_reading + tick);
        }
    }
}
