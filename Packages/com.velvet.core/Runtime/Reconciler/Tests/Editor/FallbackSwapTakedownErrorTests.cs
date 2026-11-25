using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where an error goes that is thrown as a boundary's swap to its fallback takes down the children it showed —
    /// the panel-detach callback of an element the boundary rendered, a <c>z-*</c> one's included, or the cleanup of such
    /// an element's <c>refCallback:</c>: to that boundary, which reports it after the error it caught, as React captures
    /// what a deletion throws from the nearest mounted ancestor of the deleted child. The detach callback's is pinned
    /// where the swap follows a render error the boundary's own render caught, one a child's own render threw, an error a
    /// commit captured for the boundary, and a <c>UseFrame</c> callback's error, and where, with no boundary above, the
    /// root renders nothing for a child's render error and reports both as uncaught; the <c>z-*</c> element's and the ref
    /// cleanup's where the swap follows the first. A removal a later drain makes under another boundary still goes to
    /// that one.
    /// </summary>
    /// <remarks>
    /// The tree mounts on a real panel, which is what runs a detach callback.
    /// </remarks>
    [TestFixture]
    internal sealed class FallbackSwapTakedownErrorTests
    {
        private HeadlessEditorPanelHost _host;
        private VisualElement _mountPoint;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _mountPoint = new VisualElement();
            _host.Root.Add(_mountPoint);
        }

        [TearDown]
        public void TearDown()
        {
            _host?.Dispose();
            _host = null;
        }

        // Stands in for a V.Custom<T> whose element registers a panel-detach callback that throws. Its message says
        // whether the element attached as a relocated z-* element, so the z-* case fails where the arrangement stops
        // making one.
        internal sealed class DetachThrower : VisualElement
        {
            private string _arranged = "";

            public DetachThrower()
            {
                RegisterCallback<AttachToPanelEvent>(_ =>
                    _arranged = FiberZLayerCoordinator.IsRelocatedReal(this) ? ":z" : "");
                RegisterCallback<DetachFromPanelEvent>(_ =>
                    throw new InvalidOperationException("detach:" + name + _arranged));
            }
        }

        // One delegate for every render, so a render of the boundary leaves the installed ref as it is.
        private static readonly Func<VisualElement, Action> s_refCleanupThrows =
            _ => () => throw new InvalidOperationException("ref:T");

        private MountedTree Mount(EventLog log, VNode tree)
            => V.Mount(_mountPoint, log.Provide(tree), log.ReportingErrors());

        // What the act throws is recorded as an entry, so a throw fails the case at its one assertion.
        private static void RecordingWhatItThrows(Action act, Action<string> record)
        {
            try
            {
                act();
            }
            catch (Exception exception)
            {
                record("threw:" + exception.Message);
            }
        }

        [Component]
        private static VNode ThrowsInRenderWhen(bool throws)
            => throws ? throw new InvalidOperationException("render:Child") : V.Div();

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode CatchesOverADetachThrower()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Custom<DetachThrower>(name: "T", key: "thrower"),
                V.Component(ThrowsInRenderWhen, throws, key: "child"));
        }

        [Test]
        public void Given_ABoundaryWhoseOwnRenderCatchesAChildsRenderError_When_ItsSwapRemovesAnElementThatThrowsAsItDetaches_Then_TheBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(CatchesOverADetachThrower, key: "b"));
            log.Clear();

            // Act
            log.Setter<bool>("Throws").Invoke(true);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:render:Child caught:B:detach:T"));
        }

        [Component]
        private static VNode ThrowsOnItsOwnUpdate()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            if (throws) throw new InvalidOperationException("render:Child");
            return V.Div();
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode HoldsAnUpdatingChildBesideADetachThrower()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Custom<DetachThrower>(name: "T", key: "thrower"),
                V.Component(ThrowsOnItsOwnUpdate, key: "child"));
        }

        [Test]
        public void Given_ABoundaryOverAChildWhoseOwnRenderThrows_When_ItsSwapRemovesAnElementThatThrowsAsItDetaches_Then_TheBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(HoldsAnUpdatingChildBesideADetachThrower, key: "b"));
            log.Clear();

            // Act
            log.Setter<bool>("Throws").Invoke(true);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:render:Child caught:B:detach:T"));
        }

        [Test]
        public void Given_NoBoundaryAboveAChildWhoseOwnRenderThrows_When_TheRootsTakedownRemovesAnElementThatThrowsAsItDetaches_Then_TheRootReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Fragment(
                V.Custom<DetachThrower>(name: "T", key: "thrower"),
                V.Component(ThrowsOnItsOwnUpdate, key: "child")));
            log.Clear();

            // Act
            log.Setter<bool>("Throws").Invoke(true);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("uncaught:render:Child uncaught:detach:T"));
        }

        [Component(Compiler = false)]
        private static VNode ThrowsInALayoutEffectOnItsOwnUpdate()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            Hooks.UseLayoutEffect(() =>
            {
                if (throws) throw new InvalidOperationException("layout:Child");
                return (Action)null;
            }, new object[] { throws });
            return V.Div();
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode HoldsALayoutEffectThrowerBesideADetachThrower()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Custom<DetachThrower>(name: "T", key: "thrower"),
                V.Component(ThrowsInALayoutEffectOnItsOwnUpdate, key: "child"));
        }

        [Test]
        public void Given_ABoundaryOverAChildWhoseLayoutEffectThrows_When_ItsSwapForTheCapturedErrorRemovesAnElementThatThrowsAsItDetaches_Then_TheBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(HoldsALayoutEffectThrowerBesideADetachThrower, key: "b"));
            log.Clear();

            // Act
            log.Setter<bool>("Throws").Invoke(true);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:layout:Child caught:B:detach:T"));
        }

        [Component]
        private static VNode ThrowsOnAFrame()
        {
            Hooks.UseFrame(_ => throw new InvalidOperationException("frame:Child"));
            return V.Div();
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode HoldsAFrameThrowerBesideADetachThrower()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Custom<DetachThrower>(name: "T", key: "thrower"),
                V.Component(ThrowsOnAFrame, key: "child"));
        }

        [Test]
        public void Given_ABoundaryOverAChildWhoseFrameCallbackThrows_When_ItsSwapRemovesAnElementThatThrowsAsItDetaches_Then_TheBoundaryReportsBothErrors()
        {
            // Arrange — the frame callback takes its baseline on the panel's first pass, which throws nothing
            var log = new EventLog();
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            using var mounted = Mount(log, V.Component(HoldsAFrameThrowerBesideADetachThrower, key: "b"));
            mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            log.Clear();

            // Act
            UseFrameFakeClockHost.Ms += 16;
            RecordingWhatItThrows(() => EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel), log.Add);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:frame:Child caught:B:detach:T"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode CatchesOverAnElementWhoseRefCleanupThrows()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Div(name: "T", refCallback: s_refCleanupThrows, key: "ref"),
                V.Component(ThrowsInRenderWhen, throws, key: "child"));
        }

        [Test]
        public void Given_ABoundaryWhoseOwnRenderCatchesAChildsRenderError_When_ItsSwapRemovesAnElementWhoseRefCleanupThrows_Then_TheBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(CatchesOverAnElementWhoseRefCleanupThrows, key: "b"));
            log.Clear();

            // Act
            log.Setter<bool>("Throws").Invoke(true);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:render:Child caught:B:ref:T"));
        }

        // The relocated element's parent, the mount point, outlives the swap, so the element still stands there once its
        // placeholder is taken down, and leaves once the pass's writes have applied.
        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode CatchesOverAZPlacedDetachThrower()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Custom<DetachThrower>(className: "absolute z-10", name: "T", key: "thrower"),
                V.Component(ThrowsInRenderWhen, throws, key: "child"));
        }

        [Test]
        public void Given_ABoundaryWhoseOwnRenderCatchesAChildsRenderError_When_ItsSwapRemovesAZPlacedElementThatThrowsAsItDetaches_Then_TheBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(CatchesOverAZPlacedDetachThrower, key: "b"));
            log.Clear();

            // Act
            log.Setter<bool>("Throws").Invoke(true);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:render:Child caught:B:detach:T:z"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode CatchesOverAQuietRow()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(V.Div(key: "row"), V.Component(ThrowsInRenderWhen, throws, key: "child"));
        }

        [Component]
        private static VNode RemovesADetachThrower()
        {
            var (kept, _) = EventLog.UseState("Kept", true);
            return V.Div(children: new VNode[] { kept ? V.Custom<DetachThrower>(name: "U", key: "thrower") : null });
        }

        [Component(IsErrorBoundary = true, DisplayName = "C")]
        private static VNode HoldsARemover()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-c"));
            return V.Component(RemovesADetachThrower, key: "remover");
        }

        [Component]
        private static VNode SwapperBesideARemover()
            => V.Fragment(V.Component(CatchesOverAQuietRow, key: "b"), V.Component(HoldsARemover, key: "c"));

        // GREEN_ON_BASE(characterization): the base keeps no takedown mark for a later removal to read.
        // Leaving ReconcilePass.TakingDownRowsOf set past FiberErrorBoundary.UnmountShownRows, or past
        // DeletionCommit.Teardown, hands the later removal's error to the boundary that swapped, and reddens it.
        [Test]
        public void Given_ABoundaryThatHasSwappedItsFallbackIn_When_ALaterDrainRemovesAnElementThatThrowsAsItDetachesUnderAnotherBoundary_Then_TheOtherBoundaryReportsIt()
        {
            // Arrange — the first boundary's swap, whose deletion holds a component, runs in a drain of its own
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(SwapperBesideARemover, key: "both"));
            log.Setter<bool>("Throws").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Kept").Invoke(false);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:C:detach:U"));
        }
    }
}
