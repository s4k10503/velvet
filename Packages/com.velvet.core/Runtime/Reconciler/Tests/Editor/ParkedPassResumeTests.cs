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
    /// resume reaches it or a render of the parked component drains the pass first, and the boundary's fallback reads
    /// the Providers above the boundary rather than those at the row. So does a fallback swapped in for an error an
    /// effect throws, or one thrown in a Portal's drain before the mount has committed its output, and the Providers
    /// pushed for a fallback are off the cursor once it has rendered.
    /// </summary>
    [TestFixture]
    internal sealed class ParkedPassResumeTests
    {
        private const int RowCount = 8;
        private const int LastRow = RowCount - 1;
        private const string RowError = "Parked pass resume test throw";
        private const string EffectError = "Parked pass resume test effect throw";
        private const string CleanupError = "Parked pass resume test cleanup throw";

        private static readonly ComponentContext<string> s_context = ComponentContext<string>.Create("default");
        private static readonly Dictionary<int, string> s_readings = new();
        private static readonly Dictionary<int, ComponentFiber> s_rowFibers = new();
        private static readonly List<string> s_reports = new();
        private static int s_generation;
        private static bool s_lastRowThrows;
        private static ComponentFiber s_listFiber;
        private static ComponentFiber s_outsiderFiber;
        private static string s_outsiderReading;
        private static readonly ComponentContext<string> s_belowGuardOnly =
            ComponentContext<string>.Create("unprovided");
        private static string s_panelReading;
        private static string s_panelBelowGuardOnlyReading;
        private static StateUpdater<int> s_setReaderOwn;
        private static VelvetTaskCompletionSource<int> s_readSource;
        private static VisualElement s_portalTarget;
        private static ComponentFiber s_outerFiber;
        private static bool s_childThrows;
        private static bool s_childShown;
        private static string s_providedValue;
        private static ComponentFiber s_hostFiber;
        private static ComponentFiber s_providerFiber;

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
            s_panelReading = null;
            s_panelBelowGuardOnlyReading = null;
            s_setReaderOwn = default;
            s_readSource = null;
            s_portalTarget = new VisualElement();
            s_outerFiber = null;
            s_childThrows = false;
            s_childShown = true;
            s_providedValue = "first";
            s_hostFiber = null;
            s_providerFiber = null;
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

        [Test]
        public void Given_AMemoizedComponentParkedInAPrimaryItsSuspenseHid_When_FramesPassAndTheSuspenseReveals_Then_ARowItsPassReachesReadsTheProvidersValue()
        {
            // Arrange — the list parks re-rendering its rows, then the reader beside it suspends again, so the
            // Suspense keeps the primary it had shown, hidden, with the list still parked in it.
            using var mounted = V.Mount(_root, V.Component(SuspendingProviderRootRender, key: "root"));
            var mountedRow = s_rowFibers[LastRow];
            s_generation = 1;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_listFiber.FlushStateWithTinyBudgetForTest();
            s_setReaderOwn.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var parkedWhileHidden = s_listFiber.HasPendingReconcileWorkForTest() && s_listFiber.IsOffscreen
                && s_readings[LastRow] == "gen0:provided";

            // Act — a frame's resume for each row while the primary is hidden, then the read resolves and the
            // Suspense reveals the primary.
            for (var frame = 0; frame < RowCount; frame++) FiberWorkLoop.ContinueReconcile(s_listFiber);
            s_readSource.TrySetResult(5);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            var reading = parkedWhileHidden
                ? ReadingOf(LastRow, mountedRow)
                : "the list was not parked in a hidden primary";
            Assert.That(reading, Is.EqualTo("gen1:provided"));
        }

        [Test]
        public void Given_ABoundaryWhoseOutputProvidesAboveAParkedComponent_When_AResumedRowThrows_Then_ItsFallbackReadsTheProviderAboveTheBoundary()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ProvidedGuardRootRender, key: "root"), s_recordingErrors);
            var parkedShortOfTheThrow = ParkWithTheLastRowThrowing();

            // Act
            s_listFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That(PanelReadingAfter(parkedShortOfTheThrow, s_panelReading), Is.EqualTo("outer"));
        }

        [Test]
        public void Given_ABoundaryWhoseOutputProvidesAboveAParkedComponent_When_ItsOwnRenderDrainsARowThatThrows_Then_ItsFallbackReadsTheProviderAboveTheBoundary()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ProvidedGuardRootRender, key: "root"), s_recordingErrors);
            var parkedShortOfTheThrow = ParkWithTheLastRowThrowing();

            // Act — a Normal render of the parked list, which pushes the Providers above it, drains the throwing row.
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_listFiber);

            // Assert
            Assert.That(PanelReadingAfter(parkedShortOfTheThrow, s_panelReading), Is.EqualTo("outer"));
        }

        [Test]
        public void Given_ABoundaryWhoseOutputAloneProvidesAContext_When_ARenderBelowItThrows_Then_ItsFallbackReadsThatContextsDefault()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ProvidedGuardRootRender, key: "root"), s_recordingErrors);
            var parkedShortOfTheThrow = ParkWithTheLastRowThrowing();

            // Act — a Normal render of the parked list, which pushes the Providers above it, drains the throwing row.
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_listFiber);

            // Assert
            Assert.That(
                PanelReadingAfter(parkedShortOfTheThrow, s_panelBelowGuardOnlyReading), Is.EqualTo("unprovided"));
        }

        // GREEN_ON_BASE(characterization): the base reads the Portal's snapshot, which the Portal's drain pushes.
        // What this pins is that a fallback swapped in during that drain reads the mount's output still reconciling.
        [Test]
        public void Given_ABoundaryUnderAProviderWhosePortalChildThrowsInTheMountsDrain_When_TheBoundaryCatches_Then_ItsFallbackReadsTheProvider()
        {
            // Arrange — the Provider is the mount's own output, which the mount commits only after the Portal's
            // drain, at the end of its reconcile, has run.
            var tree = V.Provider(s_context, "app", new VNode[] { V.Component(PortalGuardRender, key: "guard") });

            // Act
            using var mounted = V.Mount(_root, tree, s_recordingErrors);

            // Assert
            Assert.That(s_panelReading ?? "the fallback never rendered", Is.EqualTo("app"));
        }

        [Test]
        public void Given_ABoundaryUnderAProviderWhoseChildsEffectThrows_When_TheEffectsRun_Then_ItsFallbackReadsTheProvider()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(EffectGuardBesideOutsiderRender, key: "root"), s_recordingErrors);

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_panelReading ?? "the fallback never rendered", Is.EqualTo("p"));
        }

        // GREEN_ON_BASE(characterization): the base pushes nothing for a fallback, so it leaves nothing behind it.
        // What this pins is that the Providers pushed for one are off the cursor once it has rendered.
        [Test]
        public void Given_ABoundaryWhoseFallbackAnEffectErrorSwappedIn_When_AComponentOutsideItsProviderRendersAlone_Then_ItReadsTheDefault()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(EffectGuardBesideOutsiderRender, key: "root"), s_recordingErrors);
            mounted.FlushEffectsForTest();
            var swapped = s_panelReading != null;
            s_outsiderReading = null;

            // Act
            s_outsiderFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_outsiderFiber);

            // Assert
            Assert.That(swapped ? s_outsiderReading : "the fallback never rendered", Is.EqualTo("default"));
        }

        [Test]
        public void Given_AFallbackProvidingAroundABoundaryWithAPortal_When_AnEffectErrorSwapsItInAndThePortalsChildThrows_Then_TheInnerFallbackReadsTheOuterFallbacksProvider()
        {
            // Arrange
            var tree = V.Provider(s_context, "app", new VNode[] { V.Component(EffectGuardProvidingFallbackRender, key: "outer") });
            using var mounted = V.Mount(_root, tree, s_recordingErrors);

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_panelReading ?? "the fallback never rendered", Is.EqualTo("fallback"));
        }

        [Test]
        public void Given_AFallbackProvidingAroundABoundaryWithAPortal_When_ItsBoundarysOwnUpdateThrowsBelowItAndThePortalsChildThrows_Then_TheInnerFallbackReadsTheOuterFallbacksProvider()
        {
            // Arrange
            var tree = V.Provider(s_context, "app", new VNode[] { V.Component(UpdateGuardProvidingFallbackRender, key: "outer") });
            using var mounted = V.Mount(_root, tree, s_recordingErrors);
            s_childThrows = true;

            // Act
            s_outerFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_outerFiber);

            // Assert
            Assert.That(s_panelReading ?? "the fallback never rendered", Is.EqualTo("fallback"));
        }

        // GREEN_ON_BASE(characterization): the base's inner fallback reads the outer fallback's Provider here.
        // What this pins is that once the boundary's own update has swapped the fallback in, a spine through the
        // boundary walks the fallback rather than the output that update was reconciling.
        [Test]
        public void Given_AFallbackProvidingAroundABoundaryWithAPortal_When_ItsBoundarysOwnUpdateRemovesAChildWhoseCleanupThrows_Then_TheInnerFallbackReadsTheOuterFallbacksProvider()
        {
            // Arrange — the child's effect has run, so its cleanup runs when the update removes it.
            var tree = V.Provider(s_context, "app", new VNode[] { V.Component(RemovingGuardProvidingFallbackRender, key: "outer") });
            using var mounted = V.Mount(_root, tree, s_recordingErrors);
            mounted.FlushEffectsForTest();
            s_childShown = false;

            // Act
            s_outerFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_outerFiber);

            // Assert
            Assert.That(s_panelReading ?? "the fallback never rendered", Is.EqualTo("fallback"));
        }

        // GREEN_ON_BASE(characterization): the base walks the Provider's PreviousTree, which its parent's update replaced.
        // What this pins is that a reconcile of the Provider's own rows leaves nothing behind for a later spine to walk.
        [Test]
        public void Given_AProviderWhoseOwnUpdateReconciledItsRows_When_ItsParentChangesItsValueAndAComponentBelowItRendersAlone_Then_ItReadsTheNewValue()
        {
            // Arrange — the Provider's own update reconciles its rows; its parent's update then renders it in its walk.
            using var mounted = V.Mount(_root, V.Component(ProviderHostRender, key: "host"));
            s_providerFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_providerFiber);
            s_providedValue = "second";
            s_hostFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_hostFiber);
            s_outsiderReading = null;

            // Act
            s_outsiderFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_outsiderFiber);

            // Assert
            Assert.That(s_outsiderReading, Is.EqualTo("second"));
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

        // A first slice that reached the throw caught it before any resume or drain, and a fallback that never
        // rendered leaves the reading null, so each reads as a sentinel rather than as a value.
        private static string PanelReadingAfter(bool parkedShortOfTheThrow, string reading)
            => !parkedShortOfTheThrow
                ? "the first slice reached the throwing row"
                : reading ?? "the fallback never rendered";

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

        // The boundary provides the context again above the list, and provides a second one no Provider above it
        // does.
        [Component(Compiler = false)]
        private static VNode ProvidedGuardRootRender()
            => V.Provider(s_context, "outer", new VNode[] { V.Component(ProvidingGuardRender, key: "guard") });

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode ProvidingGuardRender()
        {
            Hooks.UseFallback(_ => V.Component(ErrorPanelRender, key: "panel"));
            return V.Provider(s_context, "inner", new VNode[]
            {
                V.Provider(s_belowGuardOnly, "below the guard", new VNode[] { V.Component(ListRender, key: "list") }),
            });
        }

        [Component(Compiler = false)]
        private static VNode ErrorPanelRender()
        {
            s_panelReading = Hooks.UseContext(s_context);
            s_panelBelowGuardOnlyReading = Hooks.UseContext(s_belowGuardOnly);
            return V.Label(name: "panel", text: s_panelReading);
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode PortalGuardRender()
        {
            Hooks.UseFallback(_ => V.Component(ErrorPanelRender, key: "panel"));
            return V.Portal(s_portalTarget, children: new VNode[] { V.Component(ThrowingRender, key: "thrower") });
        }

        [Component(Compiler = false)]
        private static VNode ThrowingRender() => throw new InvalidOperationException(RowError);

        [Component(Compiler = false)]
        private static VNode EffectGuardBesideOutsiderRender()
            => V.Fragment(children: new VNode[]
            {
                V.Provider(s_context, "p", new VNode[] { V.Component(EffectGuardRender, key: "guard") }),
                V.Component(OutsiderRender, key: "outsider"),
            });

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode EffectGuardRender()
        {
            Hooks.UseFallback(_ => V.Component(ErrorPanelRender, key: "panel"));
            return V.Component(EffectThrowingRender, key: "thrower");
        }

        [Component(Compiler = false)]
        private static VNode EffectThrowingRender()
        {
            Hooks.UseEffect(ThrowEffectError, Array.Empty<object>());
            return V.Label(text: "effect thrower");
        }

        private static Action ThrowEffectError() => throw new InvalidOperationException(EffectError);

        private static VNode ProvidingFallbackAroundPortalGuard()
            => V.Provider(s_context, "fallback", new VNode[] { V.Component(PortalGuardRender, key: "guard") });

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode EffectGuardProvidingFallbackRender()
        {
            Hooks.UseFallback(_ => ProvidingFallbackAroundPortalGuard());
            return V.Component(EffectThrowingRender, key: "thrower");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode UpdateGuardProvidingFallbackRender()
        {
            s_outerFiber = FiberAmbientStack.Current;
            Hooks.UseFallback(_ => ProvidingFallbackAroundPortalGuard());
            return V.Component(SwitchedThrowingRender, key: "thrower");
        }

        [Component(Compiler = false)]
        private static VNode SwitchedThrowingRender()
        {
            if (s_childThrows) throw new InvalidOperationException(RowError);
            return V.Label(text: "child");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode RemovingGuardProvidingFallbackRender()
        {
            s_outerFiber = FiberAmbientStack.Current;
            Hooks.UseFallback(_ => ProvidingFallbackAroundPortalGuard());
            return s_childShown ? V.Component(CleanupThrowingRender, key: "thrower") : V.Label(text: "removed");
        }

        [Component(Compiler = false)]
        private static VNode CleanupThrowingRender()
        {
            Hooks.UseEffect(CleanupThatThrows, Array.Empty<object>());
            return V.Label(text: "cleanup thrower");
        }

        private static Action CleanupThatThrows() => ThrowCleanupError;

        private static void ThrowCleanupError() => throw new InvalidOperationException(CleanupError);

        [Component(Compiler = false)]
        private static VNode ProviderHostRender()
        {
            s_hostFiber = FiberAmbientStack.Current;
            return V.Component(ValueProvidingRender, key: "provider");
        }

        [Component(Compiler = false)]
        private static VNode ValueProvidingRender()
        {
            s_providerFiber = FiberAmbientStack.Current;
            return V.Provider(s_context, s_providedValue, new VNode[] { V.Component(OutsiderRender, key: "reader") });
        }

        [Component(Compiler = false)]
        private static VNode SuspendingProviderRootRender()
            => V.Provider(s_context, "provided", new VNode[]
            {
                V.Suspense(fallback: V.Label(text: "loading"), children: new VNode[]
                {
                    V.Component(ResuspendingReaderRender, key: "reader"),
                    V.Component(MemoizedListRender, key: "list"),
                }),
            });

        // Reads a value at once until its own update asks for one that is still pending.
        [Component(Compiler = false)]
        private static VNode ResuspendingReaderRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setReaderOwn = setOwn;
            var value = Hooks.Use<int>(_ => own == 0
                ? VelvetTask.FromResult(0)
                : (s_readSource = new VelvetTaskCompletionSource<int>()).Task, own);
            return V.Label(text: "reader:" + value);
        }

        // Memoized so the walk that hides the primary reuses it as it is, parked, rather than rendering it.
        [Component(Compiler = false, Memoize = true)]
        private static VNode MemoizedListRender() => Rows();

        [Component(Compiler = false)]
        private static VNode ListRender() => Rows();

        // Keyed elements at the top level keep the list's own reconcile on the path that time-slices; the row
        // component sits one level down, where an element's own children reach it once that row is reconciled.
        private static VNode Rows()
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
