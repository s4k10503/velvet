using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where an error goes that removing an element throws — its panel-detach callback's, a <c>z-*</c> or a
    /// <c>V.WorldSpace</c> element's included, or its <c>refCallback:</c> cleanup's: to the nearest boundary at or above
    /// the component whose output held the element, passing over the components the same removal takes down, whether
    /// or not that boundary has caught, as React captures what a deletion throws from the nearest mounted ancestor of the
    /// deleted child. So a boundary's swap to its fallback leaves what taking its children down throws to that boundary,
    /// which reports it after the error it caught: where the swap follows a render error the boundary's own render
    /// caught, one a child's own render threw, one its parent's walk caught, an error a commit captured for the boundary,
    /// that capture taken in its parent's walk, and a <c>UseFrame</c> callback's error, and where, with no boundary above,
    /// the root renders nothing for a child's render error and reports both as uncaught; the <c>z-*</c> and ref cleanup
    /// cases follow the boundary's own render, and the <c>V.WorldSpace</c> cases, one inside a <c>z-*</c> element, its
    /// parent's walk. A boundary the swap takes down with the element is passed over. Where a <c>UseFrame</c> swap's
    /// fallback throws, the boundary above that replaces the swapping one takes what the takedown threw. A boundary
    /// between the component whose update removed the element and the one that rendered it takes the error, from the
    /// flat path and the general walk alike, and so does a boundary taking down its own fallback, and one above the
    /// component rendering a <c>V.VirtualList</c> whose row an update drops or a scroll takes out. A removal a later drain
    /// makes under another boundary still goes to that one.
    /// </summary>
    /// <remarks>
    /// The tree mounts on a real panel, which is what runs a detach callback.
    /// </remarks>
    [TestFixture]
    internal sealed class FallbackSwapTakedownErrorTests
    {
        private HeadlessEditorPanelHost _host;
        private VisualElement _mountPoint;
        private static bool s_armed;
        private static bool s_firstDetachThrown;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _mountPoint = new VisualElement();
            _host.Root.Add(_mountPoint);
            s_armed = true;
            s_firstDetachThrown = false;
        }

        // Disarms the throwers first: where a case leaves one on a panel, as a failing case can, the host's closing below
        // detaches it, and a throw there would fail the case for something it does not assert.
        [TearDown]
        public void TearDown()
        {
            s_armed = false;
            _host?.Dispose();
            _host = null;
        }

        // Stands in for a V.Custom<T> whose element registers a panel-detach callback that throws while armed. Its message
        // says whether the element attached as a relocated z-* element, so the z-* case fails where the arrangement stops
        // making one.
        internal sealed class DetachThrower : VisualElement
        {
            private string _arranged = "";

            public DetachThrower()
            {
                RegisterCallback<AttachToPanelEvent>(_ =>
                    _arranged = FiberZLayerCoordinator.IsRelocatedReal(this) ? ":z" : "");
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    if (s_armed) throw new InvalidOperationException("detach:" + name + _arranged);
                });
            }
        }

        // As DetachThrower, throwing from the first detach in a case alone, whichever instance has it, so a fallback the
        // boundary mounts again after taking that error does not throw it again.
        internal sealed class FirstDetachThrower : VisualElement
        {
            public FirstDetachThrower()
            {
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    if (!s_armed || s_firstDetachThrown) return;
                    s_firstDetachThrown = true;
                    throw new InvalidOperationException("detach:" + name);
                });
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

        // Its element's children are elements alone, which takes the flat path, whose removal names no owner.
        [Component]
        private static VNode RemovesADetachThrower()
        {
            var (kept, _) = EventLog.UseState("Kept", true);
            return V.Div(children: kept
                ? new VNode[] { V.Custom<DetachThrower>(name: "U", key: "thrower") }
                : Array.Empty<VNode>());
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

        // GREEN_ON_BASE(characterization): the base names no removal owner for a later removal to read.
        // Leaving ChildListWrites.RemovalOwner set where ChildListWrites.RemovalOwnerScope closes hands the later removal's
        // error to the boundary that swapped, and reddens it.
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

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode CatchesOverADetachThrowerFor(bool throws)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Custom<DetachThrower>(name: "T", key: "thrower"),
                V.Component(ThrowsInRenderWhen, throws, key: "child"));
        }

        // Its update renders the boundary inside its own walk, which catches the child's render error there.
        [Component]
        private static VNode RendersTheBoundaryInItsWalk()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            return V.Component(CatchesOverADetachThrowerFor, throws, key: "b");
        }

        [Test]
        public void Given_ABoundaryWhoseParentsWalkCatchesAChildsRenderError_When_ItsSwapRemovesAnElementThatThrowsAsItDetaches_Then_TheBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(RendersTheBoundaryInItsWalk, key: "parent"));
            log.Clear();

            // Act
            log.Setter<bool>("Throws").Invoke(true);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:render:Child caught:B:detach:T"));
        }

        // As its error is captured it asks the parent for a render, which the drain that renders the boundary for that
        // error renders as well.
        [Component(Compiler = false)]
        private static VNode ThrowsInALayoutEffectAskingItsParentToRender()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() =>
            {
                if (!throws) return (Action)null;
                log.Setter<int>("Tick").Invoke(1);
                throw new InvalidOperationException("layout:Child");
            }, new object[] { throws });
            return V.Div();
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode CapturesOverADetachThrowerFor(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Custom<DetachThrower>(name: "T", key: "thrower"),
                V.Component(ThrowsInALayoutEffectAskingItsParentToRender, key: "child"));
        }

        [Component]
        private static VNode RendersTheCapturingBoundaryInItsWalk()
        {
            var (tick, _) = EventLog.UseState("Tick", 0);
            return V.Component(CapturesOverADetachThrowerFor, tick, key: "b");
        }

        [Test]
        public void Given_ABoundaryOverAChildWhoseLayoutEffectThrowsAndAsksTheParentToRender_When_TheSwapForTheCapturedErrorRemovesAnElementThatThrowsAsItDetaches_Then_TheBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(RendersTheCapturingBoundaryInItsWalk, key: "parent"));
            log.Clear();

            // Act
            log.Setter<bool>("Throws").Invoke(true);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:layout:Child caught:B:detach:T"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "C")]
        private static VNode KeepsAnElementListWhile(bool shown)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-c"));
            return V.Div(children: shown
                ? new VNode[] { V.Custom<DetachThrower>(name: "U", key: "thrower") }
                : Array.Empty<VNode>());
        }

        [Component]
        private static VNode DropsAnElementListThroughABoundary()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Component(KeepsAnElementListWhile, shown, key: "c");
        }

        [Test]
        public void Given_ABoundaryBetweenTheUpdatedComponentAndAnElementListItRenders_When_TheUpdateRemovesAnElementThatThrowsAsItDetaches_Then_TheBoundaryReportsIt()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(DropsAnElementListThroughABoundary, key: "parent"));
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:C:detach:U"));
        }

        // The child that can be null takes the general walk, which names each removed row's owner itself.
        [Component(IsErrorBoundary = true, DisplayName = "C")]
        private static VNode KeepsAnElementBesideNothingWhile(bool shown)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-c"));
            return V.Div(children: new VNode[] { shown ? V.Custom<DetachThrower>(name: "U", key: "thrower") : null });
        }

        [Component]
        private static VNode DropsAnElementThroughABoundary()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Component(KeepsAnElementBesideNothingWhile, shown, key: "c");
        }

        [Test]
        public void Given_ABoundaryBetweenTheUpdatedComponentAndAnElementItRendersInTheGeneralWalk_When_TheUpdateRemovesAnElementThatThrowsAsItDetaches_Then_TheBoundaryReportsIt()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(DropsAnElementThroughABoundary, key: "parent"));
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:C:detach:U"));
        }

        [Component(Compiler = false)]
        private static VNode ThrowsInALayoutEffectWhenProbed()
        {
            var (probed, _) = EventLog.UseState("Probe", false);
            Hooks.UseLayoutEffect(() =>
            {
                if (probed) throw new InvalidOperationException("layout:Probe");
                return (Action)null;
            }, new object[] { probed });
            return V.Div();
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode FallsBackToADetachThrowerBesideAProbe()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            Hooks.UseFallback(_ => V.Fragment(
                V.Custom<FirstDetachThrower>(name: "F", key: "thrower"),
                V.Component(ThrowsInALayoutEffectWhenProbed, key: "probe")));
            return V.Component(ThrowsInRenderWhen, throws, key: "child");
        }

        [Test]
        public void Given_ABoundaryShowingAFallbackWhoseLayoutEffectThrows_When_TheSwapForThatErrorRemovesAFallbackElementThatThrowsAsItDetaches_Then_TheBoundaryReportsBothErrors()
        {
            // Arrange — the boundary shows its fallback for the child's render error
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(FallsBackToADetachThrowerBesideAProbe, key: "b"));
            log.Setter<bool>("Throws").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Probe").Invoke(true);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:layout:Probe caught:B:detach:F"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode CatchesOverAWorldSpaceHoldingADetachThrowerFor(bool throws)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.WorldSpace(Vector3.zero, key: "ws",
                    children: new VNode[] { V.Custom<DetachThrower>(name: "X", key: "x") }),
                V.Component(ThrowsInRenderWhen, throws, key: "child"));
        }

        // Its walk catches for the boundary, so the V.WorldSpace's rows leave in this component's removal pass, where this
        // component is the one on the stack.
        [Component]
        private static VNode RendersTheWorldSpaceBoundaryInItsWalk()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            return V.Component(CatchesOverAWorldSpaceHoldingADetachThrowerFor, throws, key: "b");
        }

        [Test]
        public void Given_ABoundaryWhoseParentsWalkCatchesAChildsRenderError_When_ItsSwapRemovesAWorldSpaceWhoseElementThrowsAsItDetaches_Then_TheBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(RendersTheWorldSpaceBoundaryInItsWalk, key: "parent"));
            log.Clear();

            // Act
            log.Setter<bool>("Throws").Invoke(true);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:render:Child caught:B:detach:X"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode CatchesOverAStackedElementHoldingAWorldSpaceFor(bool throws)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Div(className: "absolute z-10", key: "stacked", children: new VNode[]
                {
                    V.WorldSpace(Vector3.zero, key: "ws",
                        children: new VNode[] { V.Custom<DetachThrower>(name: "X", key: "x") }),
                }),
                V.Component(ThrowsInRenderWhen, throws, key: "child"));
        }

        [Component]
        private static VNode RendersTheStackedWorldSpaceBoundaryInItsWalk()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            return V.Component(CatchesOverAStackedElementHoldingAWorldSpaceFor, throws, key: "b");
        }

        [Test]
        public void Given_ABoundaryWhoseParentsWalkCatchesAChildsRenderError_When_ItsSwapRemovesAZPlacedElementHoldingAWorldSpaceWhoseElementThrowsAsItDetaches_Then_TheBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(RendersTheStackedWorldSpaceBoundaryInItsWalk, key: "parent"));
            log.Clear();

            // Act
            log.Setter<bool>("Throws").Invoke(true);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:render:Child caught:B:detach:X"));
        }

        // A boundary among the children the swap takes down, which goes with them.
        [Component(IsErrorBoundary = true, DisplayName = "D")]
        private static VNode HoldsADetachThrowerOfItsOwn()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-d"));
            return V.Custom<DetachThrower>(name: "T", key: "thrower");
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode CatchesOverABoundaryHoldingADetachThrower()
        {
            var (throws, _) = EventLog.UseState("Throws", false);
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Component(HoldsADetachThrowerOfItsOwn, key: "d"),
                V.Component(ThrowsInRenderWhen, throws, key: "child"));
        }

        [Test]
        public void Given_ABoundaryWhoseSwapTakesDownABoundaryWhoseElementThrowsAsItDetaches_When_ItsOwnRenderCatchesAChildsRenderError_Then_TheSwappingBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(CatchesOverABoundaryHoldingADetachThrower, key: "b"));
            log.Clear();

            // Act
            log.Setter<bool>("Throws").Invoke(true);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:render:Child caught:B:detach:T"));
        }

        [Component]
        private static VNode ThrowsAsTheFallback() => throw new InvalidOperationException("render:Fallback");

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode FallsBackToAThrowerOverAFrameThrower()
        {
            Hooks.UseFallback(_ => V.Component(ThrowsAsTheFallback, key: "fallback"));
            return V.Fragment(
                V.Custom<DetachThrower>(name: "T", key: "thrower"),
                V.Component(ThrowsOnAFrame, key: "child"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "A")]
        private static VNode CatchesForABoundaryWhoseFallbackThrows()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-a"));
            return V.Component(FallsBackToAThrowerOverAFrameThrower, key: "b");
        }

        [Test]
        public void Given_ABoundaryWhoseFallbackThrowsBelowAnotherBoundary_When_ItsSwapForAFrameCallbacksErrorRemovesAnElementThatThrowsAsItDetaches_Then_TheBoundaryAboveReportsBothErrors()
        {
            // Arrange — the frame callback takes its baseline on the panel's first pass, which throws nothing
            var log = new EventLog();
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            using var mounted = Mount(log, V.Component(CatchesForABoundaryWhoseFallbackThrows, key: "a"));
            mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            log.Clear();

            // Act
            UseFrameFakeClockHost.Ms += 16;
            RecordingWhatItThrows(() => EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel), log.Add);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:A:render:Fallback caught:A:detach:T"));
        }

        private sealed class ConstructionRefusingElement : VisualElement
        {
            public ConstructionRefusingElement() => throw new InvalidOperationException("thrown by the constructor");
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode FallsBackToARefusedElementOverAFrameThrower()
        {
            Hooks.UseFallback(_ => V.Custom<ConstructionRefusingElement>());
            return V.Fragment(
                V.Custom<DetachThrower>(name: "T", key: "thrower"),
                V.Component(ThrowsOnAFrame, key: "child"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "A")]
        private static VNode CatchesForABoundaryWhoseFallbackCannotBeCreated()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-a"));
            return V.Component(FallsBackToARefusedElementOverAFrameThrower, key: "b");
        }

        [Test]
        public void Given_ABoundaryWhoseFallbackCannotBeCreatedBelowAnotherBoundary_When_ItsSwapForAFrameCallbacksErrorRemovesAnElementThatThrowsAsItDetaches_Then_TheBoundaryAboveReportsBothErrors()
        {
            // Arrange — the frame callback takes its baseline on the panel's first pass, which throws nothing
            var log = new EventLog();
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            using var mounted = Mount(log, V.Component(CatchesForABoundaryWhoseFallbackCannotBeCreated, key: "a"));
            mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            log.Clear();
            LogAssert.Expect(LogType.Error, new Regex("reconciling the fallback UI threw"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[ErrorBoundary\] An exception occurred"));
            LogAssert.Expect(LogType.Exception, new Regex("thrown by the constructor"));

            // Act
            UseFrameFakeClockHost.Ms += 16;
            RecordingWhatItThrows(() => EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel), log.Add);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:A:thrown by the constructor caught:A:detach:T"));
        }

        private sealed record Items(string[] Names);

        private static VNode ARowFor(string item)
            => item == "R1" ? V.Custom<DetachThrower>(name: item, key: item) : V.Div(name: item, key: item);

        [Component]
        private static VNode RendersAListOf(Items props)
            => V.VirtualList(items: props.Names, keySelector: item => item, itemHeight: 50f, renderer: ARowFor, overscan: 0);

        [Component(IsErrorBoundary = true, DisplayName = "C")]
        private static VNode HoldsAListRendererOf(Items props)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-c"));
            return V.Component(RendersAListOf, props, key: "list");
        }

        [Component]
        private static VNode HandsTheListItsItems()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Component(HoldsAListRendererOf, new Items(shown ? new[] { "R1", "R2" } : new[] { "R2" }), key: "c");
        }

        // EditMode runs no layout, so the list is given the range its scroll would give it, rows of it tall.
        private void ShowTheListsRows(MountedTree mounted, float scrollY, int rows)
            => mounted.Root.Reconciler!.Context.VirtualListControllers[_mountPoint.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: scrollY, viewportHeight: 50f * rows);

        [Test]
        public void Given_ABoundaryBetweenTheUpdatedComponentAndTheComponentRenderingAList_When_TheUpdateDropsARowThatThrowsAsItDetaches_Then_TheBoundaryReportsIt()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(HandsTheListItsItems, key: "parent"));
            ShowTheListsRows(mounted, scrollY: 0f, rows: 2);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:C:detach:R1"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "C")]
        private static VNode HoldsAListRendererOfThreeRows()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-c"));
            return V.Component(RendersAListOf, new Items(new[] { "R1", "R2", "R3" }), key: "list");
        }

        [Test]
        public void Given_ABoundaryAboveTheComponentRenderingAList_When_AScrollTakesOutARowThatThrowsAsItDetaches_Then_TheBoundaryReportsIt()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(HoldsAListRendererOfThreeRows, key: "c"));
            ShowTheListsRows(mounted, scrollY: 0f, rows: 1);
            mounted.DrainForTest();
            log.Clear();

            // Act
            RecordingWhatItThrows(() => ShowTheListsRows(mounted, scrollY: 50f, rows: 1), log.Add);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:C:detach:R1"));
        }
    }
}
