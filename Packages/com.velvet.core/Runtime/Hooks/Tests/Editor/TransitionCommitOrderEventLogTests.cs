using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the order a transition's writes commit in, as one <see cref="EventLog"/> string per case, each what
    /// react-reconciler 19.1 commits for the same tree: they land on one transition lane, which an urgent update
    /// outranks and a second transition writing the same queue is entangled with (<c>entangleTransitionUpdate</c>),
    /// and a concurrent render commits nothing until it completes. The probes record the committed state and not
    /// <c>isPending</c>, whose clearing commits a render later here than in React (react-migration.md's
    /// <c>useTransition</c> row).
    /// </summary>
    [TestFixtureSource(typeof(EventLogRenderer), nameof(EventLogRenderer.All))]
    internal sealed class TransitionCommitOrderEventLogTests
    {
        private readonly EventLogRenderer _renderer;

        public TransitionCommitOrderEventLogTests(EventLogRenderer renderer) => _renderer = renderer;

        [Component]
        private static VNode Pending()
        {
            EventLog.UseTransition("T");
            var (value, _) = EventLog.UseState("V", 0);
            var (urgent, _) = EventLog.UseState("U", 0);
            EventLog.UseCommit("T", value + "," + urgent);
            return V.Div();
        }

        private MountedEventLogTree MountPending(EventLog log)
        {
            var mounted = _renderer.Mount(log.Provide(V.Component(Pending)));
            mounted.DrainForTest();
            log.Clear();
            return mounted;
        }

        // GREEN_ON_BASE(characterization): the base keeps the transition's write out of the urgent commits ahead of it.
        // Have HookUpdateQueueSlot.Process apply a Transition-lane update in a render of any lane and the log reads
        // `commit:T=1,0 commit:T=1,1`.
        [Test]
        public void Given_APendingTransition_When_AnUrgentUpdateArrivesBeforeItRenders_Then_TheUrgentUpdateCommitsFirstAndTheLastCommitCarriesBoth()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountPending(log);
            log.Transition("T").Invoke(() => log.Setter<int>("V").Invoke(1));
            mounted.DrainForTest();

            // Act
            log.Setter<int>("U").Invoke(1);
            mounted.DrainTransitionsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("commit:T=0,1 commit:T=1,1"));
        }

        // GREEN_ON_BASE(characterization): the base renders both transitions' writes to one state in a single commit.
        // Have HookUpdateQueueSlot.Process defer each Transition-lane update after the first it applies and the log reads
        // `commit:T=1,0`.
        [Test]
        public void Given_AComponentWithATransition_When_TwoTransitionsWriteTheSameStateBeforeEitherCommits_Then_NoCommitShowsTheFirstValue()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountPending(log);

            // Act
            log.Transition("T").Invoke(() => log.Setter<int>("V").Invoke(1));
            log.Transition("T").Invoke(() => log.Setter<int>("V").Invoke(2));
            mounted.DrainTransitionsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("commit:T=2,0"));
        }

        private sealed class Slicing
        {
            internal ComponentFiber? Fiber;
            internal int Version;
        }

        private sealed record Dep(string Name, int Version);

        private const int SlicedRowCount = 3;

        [Component]
        private static VNode SlicedRow(Dep props)
        {
            EventLog.UseRender(props.Name);
            EventLog.UseInsertionAndLayoutEffects(props.Name, new object[] { props.Version });
            return V.Fragment(Array.Empty<VNode>());
        }

        // Built as PortalCommitOrderEventLogTests.SlicedRows is, and for its reasons.
        [Component(Compiler = false)]
        private static VNode SlicedRows(Slicing slicing)
        {
            slicing.Fiber = FiberAmbientStack.Current;
            EventLog.UseRender("Rows");
            var version = slicing.Version;
            var rows = new VNode[SlicedRowCount];
            for (var i = 0; i < SlicedRowCount; i++)
            {
                rows[i] = V.Div(key: "row" + i,
                    children: new VNode?[] { V.Component(SlicedRow, new Dep("R" + i, version), key: "row") });
            }
            return V.Fragment(rows);
        }

        [TestFixture]
        internal sealed class UIToolkitOnlyTransitions
        {
            // GREEN_ON_BASE(characterization): the base holds a parked pass's insertion and layout work until its last slice.
            // Have FiberEffects.IsHeld answer false and `ins-x:R0 ins:R0 lay-x:R0 lay:R0` lands ahead of `parked`.
            [Test]
            public void Given_ATimeSlicedTransitionOfRowsWithEffects_When_ItsFirstSliceParks_Then_OnlyRendersAreLoggedUntilItsLastSliceCommits()
            {
                // Arrange
                var log = new EventLog();
                var slicing = new Slicing();
                using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(SlicedRows, slicing)));
                mounted.DrainForTest();
                log.Clear();
                slicing.Version = 1;

                // Act
                slicing.Fiber!.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
                slicing.Fiber.FlushStateWithTinyBudgetForTest();
                log.Add(slicing.Fiber.HasPendingReconcileWorkForTest() ? "parked" : "never-parked");
                slicing.Fiber.DrainTimeSlicedReconcileForTest();
                mounted.DrainForTest();

                // Assert — a pass that never parked commits in its first slice, so that reading is a sentinel
                Assert.That(log.ToString(), Is.EqualTo(
                    "render:Rows render:R0 parked render:R1 render:R2"
                    + " ins-x:R0 ins:R0 lay-x:R0 ins-x:R1 ins:R1 lay-x:R1 ins-x:R2 ins:R2 lay-x:R2"
                    + " lay:R0 lay:R1 lay:R2"));
            }
        }
    }
}
