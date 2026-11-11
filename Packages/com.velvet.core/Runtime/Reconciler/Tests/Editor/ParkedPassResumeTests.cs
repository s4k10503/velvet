using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what the rows a parked time-sliced pass reaches after it parked see and leave behind. A row a resumed
    /// slice re-renders reads the Provider above the parked component, as the rows of the slice that parked read it,
    /// and a component outside that Provider rendering after the resume reads the default again. A row that throws
    /// there, under a boundary above the parked component, is reported once by that boundary, whether the scheduled
    /// resume reaches it or a render of the parked component drains the pass first.
    /// </summary>
    [TestFixture]
    internal sealed class ParkedPassResumeTests
    {
        private const int RowCount = 8;
        private const int LastRow = RowCount - 1;
        private const string RowError = "Parked pass resume test throw";

        private static readonly ComponentContext<string> s_context = ComponentContext<string>.Create("default");
        private static readonly Dictionary<int, string> s_readings = new();
        private static readonly Dictionary<int, ComponentFiber> s_rowFibers = new();
        private static readonly List<string> s_reports = new();
        private static int s_generation;
        private static bool s_lastRowThrows;
        private static ComponentFiber s_listFiber;
        private static ComponentFiber s_outsiderFiber;
        private static string s_outsiderReading;

        private static readonly MountOptions s_recordingErrors = new()
        {
            OnCaughtError = (error, _) => s_reports.Add("caught:" + error.Message),
            OnUncaughtError = (error, _) => s_reports.Add("uncaught:" + error.Message),
        };

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_readings.Clear();
            s_rowFibers.Clear();
            s_reports.Clear();
            s_generation = 0;
            s_lastRowThrows = false;
            s_listFiber = null;
            s_outsiderFiber = null;
            s_outsiderReading = null;
            Application.logMessageReceived += RecordLoggedError;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= RecordLoggedError;
        }

        [Test]
        public void Given_AComponentUnderAProviderWhoseTransitionPassParked_When_TheResumeReRendersALaterRow_Then_TheRowReadsTheProvidersValue()
        {
            // Arrange — the list re-renders every row on the Transition lane under a budget too small for one row,
            // so the slice that parks leaves the last row to a resume.
            using var mounted = V.Mount(_root, V.Component(ProvidingRootRender, key: "root"));
            var mountedRow = s_rowFibers[LastRow];
            s_generation = 1;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_listFiber.FlushStateWithTinyBudgetForTest();
            var parkedShortOfTheRow = s_listFiber.HasPendingReconcileWorkForTest()
                && s_readings[LastRow].StartsWith("gen0:", StringComparison.Ordinal);

            // Act
            s_listFiber.DrainTimeSlicedReconcileForTest();

            // Assert — a first slice that reached the row read the Provider whether or not a resume does, so that
            // reading is a sentinel rather than the row's value.
            var reading = parkedShortOfTheRow ? ReadingOf(LastRow, mountedRow) : "the first slice reached the row";
            Assert.That(reading, Is.EqualTo("gen1:provided"));
        }

        // GREEN_ON_BASE(characterization): the base rebuilds no Provider spine at a resume to leave behind.
        // What this pins is that the spine a resume now rebuilds is off the cursor before a later render reads it.
        [Test]
        public void Given_APassResumedUnderAProvider_When_AComponentOutsideTheProviderRendersAfterTheResume_Then_ItReadsTheDefault()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ProviderBesideOutsiderRender, key: "root"));
            s_generation = 1;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_listFiber.FlushStateWithTinyBudgetForTest();
            var parked = s_listFiber.HasPendingReconcileWorkForTest();
            s_listFiber.DrainTimeSlicedReconcileForTest();
            s_outsiderReading = null;

            // Act
            s_outsiderFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_outsiderFiber);

            // Assert — a list that never parked was never resumed, so that reading is a sentinel.
            Assert.That(parked ? s_outsiderReading : "the list never parked", Is.EqualTo("default"));
        }

        [Test]
        public void Given_AComponentUnderABoundaryWhoseTransitionPassParked_When_TheResumeRendersARowThatThrows_Then_TheBoundaryReportsItOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(GuardRender, key: "guard"), s_recordingErrors);
            var parkedShortOfTheThrow = ParkWithTheLastRowThrowing();

            // Act
            s_listFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That(ReportsAfter(parkedShortOfTheThrow), Is.EqualTo("caught:" + RowError));
        }

        [Test]
        public void Given_AComponentUnderABoundaryWhoseTransitionPassParked_When_ItsOwnRenderDrainsARowThatThrows_Then_TheBoundaryReportsItOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(GuardRender, key: "guard"), s_recordingErrors);
            var parkedShortOfTheThrow = ParkWithTheLastRowThrowing();

            // Act — a Normal render of the parked list completes the parked pass before it reconciles its own output.
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_listFiber);

            // Assert
            Assert.That(ReportsAfter(parkedShortOfTheThrow), Is.EqualTo("caught:" + RowError));
        }

        // The list re-renders every row on the Transition lane under a budget too small for one row, the last row
        // throwing in that render. True where the slice that parks stopped short of the last row: a throw in that
        // slice disposes the list, which leaves no parked pass behind.
        private static bool ParkWithTheLastRowThrowing()
        {
            s_lastRowThrows = true;
            s_generation = 1;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_listFiber.FlushStateWithTinyBudgetForTest();
            return s_listFiber.HasPendingReconcileWorkForTest();
        }

        // The fallback is folded in as a sentinel, so a case whose boundary never swapped it in cannot pass on its
        // reports.
        private string ReportsAfter(bool parkedShortOfTheThrow)
        {
            if (!parkedShortOfTheThrow) return "the first slice reached the throwing row";
            var fallbacks = _root.Query<Label>(name: "guard-fallback").ToList().Count;
            return fallbacks == 1 ? string.Join(" | ", s_reports) : "fallbacks shown: " + fallbacks;
        }

        // A row the resume disposed, or replaced with a component of its own, reads as such rather than as what its
        // last render recorded.
        private static string ReadingOf(int row, ComponentFiber mountedRow)
        {
            var fiber = s_rowFibers[row];
            if (!ReferenceEquals(fiber, mountedRow)) return "remounted:" + s_readings[row];
            return fiber.IsDisposed ? "disposed:" + s_readings[row] : s_readings[row];
        }

        private static void RecordLoggedError(string condition, string stackTrace, LogType type)
        {
            if (type is LogType.Exception or LogType.Error) s_reports.Add("logged:" + condition);
        }

        private sealed record RowProps(int Index, int Generation);

        [Component(Compiler = false)]
        private static VNode ProvidingRootRender()
            => V.Provider(s_context, "provided", new VNode[] { V.Component(ListRender, key: "list") });

        [Component(Compiler = false)]
        private static VNode ProviderBesideOutsiderRender()
            => V.Fragment(children: new VNode[]
            {
                V.Provider(s_context, "provided", new VNode[] { V.Component(ListRender, key: "list") }),
                V.Component(OutsiderRender, key: "outsider"),
            });

        [Component(Compiler = false)]
        private static VNode OutsiderRender()
        {
            s_outsiderFiber = FiberAmbientStack.Current;
            s_outsiderReading = Hooks.UseContext(s_context);
            return V.Label(text: s_outsiderReading);
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode GuardRender()
        {
            Hooks.UseFallback(_ => V.Label(name: "guard-fallback", key: "fallback"));
            return V.Component(ListRender, key: "list");
        }

        // Keyed elements at the top level keep the list's own reconcile on the path that time-slices; the row
        // component sits one level down, where an element's own children reach it once that row is reconciled.
        [Component(Compiler = false)]
        private static VNode ListRender()
        {
            s_listFiber = FiberAmbientStack.Current;
            var rows = new VNode[RowCount];
            for (var i = 0; i < RowCount; i++)
            {
                rows[i] = V.Div(key: "row" + i, children: new VNode[]
                {
                    V.Component(RowRender, new RowProps(i, s_generation), key: "row"),
                });
            }
            return V.Fragment(children: rows);
        }

        [Component(Compiler = false)]
        private static VNode RowRender(RowProps props)
        {
            var provided = Hooks.UseContext(s_context);
            if (s_lastRowThrows && props.Generation == 1 && props.Index == LastRow)
            {
                throw new InvalidOperationException(RowError);
            }
            s_rowFibers[props.Index] = FiberAmbientStack.Current;
            s_readings[props.Index] = "gen" + props.Generation + ":" + provided;
            return V.Label(text: s_readings[props.Index]);
        }
    }
}
