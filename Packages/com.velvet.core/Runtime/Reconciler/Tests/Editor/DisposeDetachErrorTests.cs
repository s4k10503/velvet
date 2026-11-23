using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where an error goes that an element's panel-detach callback throws while <see cref="MountedTree.Dispose"/>
    /// removes the tree's rows: to <see cref="MountOptions.OnUncaughtError"/> once the unmount is done, with
    /// <c>Dispose</c> returning, as React's <c>root.unmount()</c> captures what a removal throws at the root and reports
    /// it after the unmount commits. Two elements' errors are each reported, as are an element's and an effect
    /// cleanup's, and so is one thrown where the disposal waited for another root's commit to end. A boundary the
    /// disposal tears down catches none of them. A <c>z-*</c> element, which leaves its parent once the pass's writes have
    /// applied, is reported the same way where the disposal removes it, each of two, and where a drain's update removes
    /// it, whichever walk cleans it up, the boundary above the component whose render removed it catches it, though a
    /// later render of the same drain records behind it. Under a reconciler no root owns, the first such error is thrown
    /// once each element has left.
    /// </summary>
    /// <remarks>
    /// The tree mounts on a real panel, which is what runs a detach callback.
    /// </remarks>
    [TestFixture]
    internal sealed class DisposeDetachErrorTests
    {
        private HeadlessEditorPanelHost _host;
        private VisualElement _mountPoint;
        private readonly List<string> _reports = new();
        private static int s_detached;
        private static MountedTree s_disposedByAnotherRoot;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _mountPoint = new VisualElement();
            _host.Root.Add(_mountPoint);
            _reports.Clear();
            s_detached = 0;
            s_disposedByAnotherRoot = null;
        }

        [TearDown]
        public void TearDown()
        {
            s_disposedByAnotherRoot = null;
            _host?.Dispose();
            _host = null;
        }

        // Stands in for a V.Custom<T> whose element registers a panel-detach callback that throws.
        internal sealed class DetachThrower : VisualElement
        {
            public DetachThrower()
            {
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    s_detached++;
                    throw new InvalidOperationException("detach:" + name);
                });
            }
        }

        // As DetachThrower, saying in its message whether it attached as a relocated z-* element, so a case about one
        // fails where the arrangement stops making one, and throwing from its first detach alone, so a removal after the
        // case's own does not throw.
        internal sealed class ZPlacedDetachThrower : VisualElement
        {
            private bool _relocated;
            private bool _thrown;

            public ZPlacedDetachThrower()
            {
                RegisterCallback<AttachToPanelEvent>(_ => _relocated = FiberZLayerCoordinator.IsRelocatedReal(this));
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    if (_thrown) return;
                    _thrown = true;
                    s_detached++;
                    throw new InvalidOperationException("detach:" + name + (_relocated ? ":z" : ""));
                });
            }
        }

        [Component(Compiler = false)]
        private static VNode LogsItsCleanup()
        {
            var log = EventLog.UseLog();
            Hooks.UseEffect((Func<Action>)(() => () => log.Add("eff-x:C")), Array.Empty<object>());
            return V.Div();
        }

        [Component(Compiler = false)]
        private static VNode ThrowsFromItsCleanup()
        {
            Hooks.UseEffect((Func<Action>)(() => () => throw new InvalidOperationException("cleanup:C")),
                Array.Empty<object>());
            return V.Div();
        }

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "B")]
        private static VNode CatchesAroundAThrower()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Custom<DetachThrower>(name: "X", key: "thrower");
        }

        [Component(Compiler = false)]
        private static VNode RemovesAZPlacedThrower()
        {
            var (shown, _) = EventLog.UseState("R", true);
            EventLog.UseRender("R");
            return V.Div(className: "relative", children: new VNode[]
            {
                shown ? V.Custom<ZPlacedDetachThrower>(className: "absolute z-10", name: "Z", key: "thrower") : null,
            });
        }

        // Removes the element through the fast path, which cleans it up as the walk removes it; RemovesAZPlacedThrower's
        // null child takes the general walk instead, which leaves that to the pass's apply.
        [Component(Compiler = false)]
        private static VNode RemovesAZPlacedThrowerFromItsList()
        {
            var (shown, _) = EventLog.UseState("L", true);
            EventLog.UseRender("L");
            return V.Div(className: "relative", children: shown
                ? new VNode[] { V.Custom<ZPlacedDetachThrower>(className: "absolute z-10", name: "Z", key: "thrower") }
                : Array.Empty<VNode>());
        }

        [Component(Compiler = false)]
        private static VNode RendersAgain()
        {
            var (tick, _) = EventLog.UseState("T", 0);
            EventLog.UseRender("T", tick);
            return V.Div();
        }

        private sealed record Holds(VNode Child);

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "B1")]
        private static VNode FirstBoundary(Holds props)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-1"));
            return props.Child;
        }

        [Component(Compiler = false, IsErrorBoundary = true, DisplayName = "B2")]
        private static VNode SecondBoundary(Holds props)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-2"));
            return props.Child;
        }

        [Component(Compiler = false)]
        private static VNode DisposesAnotherRoot()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_disposedByAnotherRoot.Dispose();
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div();
        }

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

        [Test]
        public void Given_ATreeWhoseElementThrowsAsItDetachesBesideAComponentWhoseEffectCleanupLogs_When_TheHandleIsDisposed_Then_TheCleanupRunsAndTheRootReportsTheErrorOnceAfterIt()
        {
            // Arrange
            var log = new EventLog();
            var mounted = V.Mount(_mountPoint, log.Provide(V.Fragment(
                V.Custom<DetachThrower>(name: "X", key: "thrower"),
                V.Component(LogsItsCleanup, key: "logs"))), log.ReportingErrors());
            mounted.FlushEffectsForTest();

            // Act
            RecordingWhatItThrows(mounted.Dispose, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:C uncaught:detach:X"));
        }

        // Equivalent rather than equal: which of the two rows detaches first is not what this pins.
        [Test]
        public void Given_TwoElementsThatThrowAsTheyDetach_When_TheHandleIsDisposed_Then_EachErrorIsReportedOnceBothHaveDetached()
        {
            // Arrange — each report carries how many detach callbacks had run when the handler was called
            var mounted = V.Mount(_mountPoint, V.Fragment(
                    V.Custom<DetachThrower>(name: "X", key: "x"),
                    V.Custom<DetachThrower>(name: "Y", key: "y")),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message + "@" + s_detached) });

            // Act
            RecordingWhatItThrows(mounted.Dispose, _reports.Add);

            // Assert
            Assert.That(_reports, Is.EquivalentTo(new[] { "detach:X@2", "detach:Y@2" }));
        }

        // Equivalent rather than equal: the order of the two reports is not what this pins.
        [Test]
        public void Given_AnElementThatThrowsAsItDetachesBesideAComponentWhoseEffectCleanupThrows_When_TheHandleIsDisposed_Then_BothErrorsAreReported()
        {
            // Arrange
            var mounted = V.Mount(_mountPoint, V.Fragment(
                    V.Custom<DetachThrower>(name: "X", key: "thrower"),
                    V.Component(ThrowsFromItsCleanup, key: "cleanup")),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });
            mounted.FlushEffectsForTest();

            // Act
            RecordingWhatItThrows(mounted.Dispose, _reports.Add);

            // Assert
            Assert.That(_reports, Is.EquivalentTo(new[] { "cleanup:C", "detach:X" }));
        }

        [Test]
        public void Given_ABoundaryAboveAnElementThatThrowsAsItDetaches_When_TheHandleIsDisposed_Then_TheRootReportsTheErrorAndTheBoundaryCatchesNothing()
        {
            // Arrange
            var log = new EventLog();
            var mounted = V.Mount(_mountPoint, log.Provide(V.Component(CatchesAroundAThrower, key: "boundary")),
                log.ReportingErrors());

            // Act
            RecordingWhatItThrows(mounted.Dispose, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("uncaught:detach:X"));
        }

        [Test]
        public void Given_ATreeWhoseElementThrowsAsItDetaches_When_ALayoutEffectOfAnotherRootDisposesItsHandle_Then_TheRootReportsTheError()
        {
            // Arrange
            s_disposedByAnotherRoot = V.Mount(_mountPoint, V.Custom<DetachThrower>(name: "X", key: "thrower"),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });

            // Act
            using var other = V.Mount(new VisualElement(), V.Component(DisposesAnotherRoot));

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("detach:X"));
        }

        // Equivalent rather than equal: which of the two elements leaves first is not what this pins.
        [Test]
        public void Given_TwoZPlacedElementsInTheMountTargetThatThrowAsTheyDetach_When_TheHandleIsDisposed_Then_EachErrorIsReported()
        {
            // Arrange
            var mounted = V.Mount(_mountPoint, V.Fragment(
                    V.Custom<ZPlacedDetachThrower>(className: "absolute z-10", name: "Y", key: "y"),
                    V.Custom<ZPlacedDetachThrower>(className: "absolute z-10", name: "Z", key: "z")),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });

            // Act
            RecordingWhatItThrows(mounted.Dispose, _reports.Add);

            // Assert
            Assert.That(_reports, Is.EquivalentTo(new[] { "detach:Y:z", "detach:Z:z" }));
        }

        [Test]
        public void Given_ABoundaryAboveAComponentWhoseZPlacedElementThrowsAsItDetaches_When_ADrainRemovesTheElement_Then_TheBoundaryReportsTheError()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_mountPoint, log.Provide(V.Component(FirstBoundary,
                new Holds(V.Component(RemovesAZPlacedThrower, key: "r")), key: "b1")), log.ReportingErrors());
            log.Clear();

            // Act
            log.Setter<bool>("R").Invoke(false);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:R caught:B1:detach:Z:z"));
        }

        [Test]
        public void Given_ABoundaryAboveAComponentWhoseZPlacedElementThrowsAsItDetaches_When_ADrainRemovesTheElementFromAListOfHostElements_Then_TheBoundaryReportsTheError()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_mountPoint, log.Provide(V.Component(FirstBoundary,
                new Holds(V.Component(RemovesAZPlacedThrowerFromItsList, key: "l")), key: "b1")), log.ReportingErrors());
            log.Clear();

            // Act
            log.Setter<bool>("L").Invoke(false);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:L caught:B1:detach:Z:z"));
        }

        // The drain renders the two in tree order, so the run of the component that removes the element is not the
        // last one the pass records.
        [Test]
        public void Given_TwoComponentsUnderBoundariesOfTheirOwnThatOneDrainRenders_When_TheFirstRemovesAZPlacedElementThatThrowsAsItDetaches_Then_TheFirstsBoundaryReportsIt()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_mountPoint, log.Provide(V.Fragment(
                V.Component(FirstBoundary, new Holds(V.Component(RemovesAZPlacedThrower, key: "r")), key: "b1"),
                V.Component(SecondBoundary, new Holds(V.Component(RendersAgain, key: "t")), key: "b2"))),
                log.ReportingErrors());
            log.Clear();

            // Act
            log.Setter<bool>("R").Invoke(false);
            log.Setter<int>("T").Invoke(1);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:R render:T=1 caught:B1:detach:Z:z"));
        }

        // Both elements carry one name: which of the two fails first is not what this pins.
        [Test]
        public void Given_TwoZPlacedElementsThatThrowAsTheyDetachUnderAReconcilerNoRootOwns_When_AReconcileRemovesBoth_Then_TheFirstErrorIsThrownOnceBothHaveDetached()
        {
            // Arrange — the entry carries how many detach callbacks had run when the error reached the case
            using var scope = new ReconcilerScope();
            _host.Root.Add(scope.Root);
            var before = new VNode[]
            {
                V.Custom<ZPlacedDetachThrower>(className: "absolute z-10", name: "Y", key: "first"),
                V.Custom<ZPlacedDetachThrower>(className: "absolute z-10", name: "Y", key: "second"),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);

            // Act
            RecordingWhatItThrows(() => scope.Reconciler.Reconcile(scope.Root, before, Array.Empty<VNode>()),
                entry => _reports.Add(entry + "@" + s_detached));

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("threw:detach:Y:z@2"));
        }
    }
}
