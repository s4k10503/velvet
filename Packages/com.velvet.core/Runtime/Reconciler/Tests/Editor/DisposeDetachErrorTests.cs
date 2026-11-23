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
    /// once each element has left. An element whose removal threw is neither removed again nor pooled, so its callback
    /// runs once and is reported once where it sits in a clip-path wrapper, in a pooled button, or in a row whose removal
    /// took it, and a pooled button whose own callback threw stays out of the pool.
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
            VNodePoolTestAccess.ClearButtonPoolForTest();
        }

        // Stands in for a V.Custom<T> whose element registers a panel-detach callback, which throws on every call. Its
        // message says whether the element attached as, or inside, a relocated z-* element, and inside a clip-path
        // wrapper, so a case about either fails where the arrangement stops making one, and numbers every call after the
        // first, so a callback run twice reads as two messages.
        internal sealed class DetachThrower : VisualElement
        {
            private string _arranged = "";
            private int _calls;

            public DetachThrower()
            {
                RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    var parent = hierarchy.parent;
                    _arranged = (FiberZLayerCoordinator.IsRelocatedReal(this) || FiberZLayerCoordinator.IsRelocatedReal(parent)
                            ? ":z" : "")
                        + (parent != null && parent.ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass)
                            ? ":wrapped" : "");
                });
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    s_detached++;
                    _calls++;
                    throw new InvalidOperationException("detach:" + name + _arranged + (_calls > 1 ? "#" + _calls : ""));
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
                shown ? V.Custom<DetachThrower>(className: "absolute z-10", name: "Z", key: "thrower") : null,
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
                ? new VNode[] { V.Custom<DetachThrower>(className: "absolute z-10", name: "Z", key: "thrower") }
                : Array.Empty<VNode>());
        }

        [Component(Compiler = false)]
        private static VNode RemovesAWrappedThrower()
        {
            var (shown, _) = EventLog.UseState("W", true);
            EventLog.UseRender("W");
            return V.Div(children: new VNode[]
            {
                shown ? V.Custom<DetachThrower>(className: "clip-path-[circle(50%)]", name: "W", key: "thrower") : null,
            });
        }

        // Registers a detach callback that throws on every call, as an application's ref callback can.
        private static readonly Func<VisualElement, Action> s_registersAThrowingDetach = element =>
        {
            element.RegisterCallback<DetachFromPanelEvent>(_ => throw new InvalidOperationException("detach:" + element.name));
            return () => { };
        };

        [Component(Compiler = false)]
        private static VNode RemovesTwoButtons()
        {
            var (shown, _) = EventLog.UseState("P", true);
            EventLog.UseRender("P");
            return V.Div(children: new VNode[]
            {
                shown ? V.Button(key: "thrower", name: "P", refCallback: s_registersAThrowingDetach) : null,
                shown ? V.Button(key: "control") : null,
            });
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
                    V.Custom<DetachThrower>(className: "absolute z-10", name: "Y", key: "y"),
                    V.Custom<DetachThrower>(className: "absolute z-10", name: "Z", key: "z")),
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
                V.Custom<DetachThrower>(className: "absolute z-10", name: "Y", key: "first"),
                V.Custom<DetachThrower>(className: "absolute z-10", name: "Y", key: "second"),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);

            // Act
            RecordingWhatItThrows(() => scope.Reconciler.Reconcile(scope.Root, before, Array.Empty<VNode>()),
                entry => _reports.Add(entry + "@" + s_detached));

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("threw:detach:Y:z@2"));
        }

        // GREEN_ON_BASE(characterization): UI Toolkit leaves an element whose detach callback threw naming its panel.
        // FiberElementCleaner.StoppedPartway reads that, and an engine that clears the panel anyway reddens this.
        [Test]
        public void Given_AnElementWhoseDetachCallbackThrows_When_ItIsRemovedFromItsPanel_Then_ItStillNamesThatPanel()
        {
            // Arrange
            var element = new DetachThrower();
            _mountPoint.Add(element);

            // Act
            RecordingWhatItThrows(() => _mountPoint.Remove(element), _reports.Add);

            // Assert
            Assert.That(element.panel, Is.SameAs(_host.Panel));
        }

        [Test]
        public void Given_AClipPathWrappedElementThatThrowsAsItDetaches_When_TheHandleIsDisposed_Then_ItsCallbackRunsOnceAndTheRootReportsIt()
        {
            // Arrange
            var mounted = V.Mount(_mountPoint, V.Custom<DetachThrower>(className: "clip-path-[circle(50%)]", name: "X", key: "x"),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });

            // Act
            RecordingWhatItThrows(mounted.Dispose, _reports.Add);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("detach:X:wrapped"));
        }

        [Test]
        public void Given_APooledButtonHoldingAnElementThatThrowsAsItDetaches_When_TheHandleIsDisposed_Then_ItsCallbackRunsOnceAndTheRootReportsIt()
        {
            // Arrange
            var mounted = V.Mount(_mountPoint,
                V.Button(key: "button", children: new VNode[] { V.Custom<DetachThrower>(name: "X", key: "x") }),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });

            // Act
            RecordingWhatItThrows(mounted.Dispose, _reports.Add);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("detach:X"));
        }

        [Test]
        public void Given_AClipPathWrappedZPlacedElementInTheMountTargetThatThrowsAsItDetaches_When_TheHandleIsDisposed_Then_ItsCallbackRunsOnceAndTheRootReportsIt()
        {
            // Arrange
            var mounted = V.Mount(_mountPoint,
                V.Custom<DetachThrower>(className: "absolute z-10 clip-path-[circle(50%)]", name: "X", key: "x"),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });

            // Act
            RecordingWhatItThrows(mounted.Dispose, _reports.Add);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("detach:X:z:wrapped"));
        }

        [Test]
        public void Given_AZPlacedElementThatThrowsAsItDetachesInsideARow_When_TheHandleIsDisposed_Then_ItsCallbackRunsOnceAndTheRootReportsIt()
        {
            // Arrange
            var mounted = V.Mount(_mountPoint, V.Div(className: "relative", key: "row", children: new VNode[]
                {
                    V.Custom<DetachThrower>(className: "absolute z-10", name: "Z", key: "z"),
                }),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });

            // Act
            RecordingWhatItThrows(mounted.Dispose, _reports.Add);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("detach:Z:z"));
        }

        [Test]
        public void Given_ABoundaryAboveAComponentWhoseClipPathWrappedElementThrowsAsItDetaches_When_ADrainRemovesTheElement_Then_ItsCallbackRunsOnceAndTheBoundaryReportsIt()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_mountPoint, log.Provide(V.Component(FirstBoundary,
                new Holds(V.Component(RemovesAWrappedThrower, key: "w")), key: "b1")), log.ReportingErrors());
            log.Clear();

            // Act
            log.Setter<bool>("W").Invoke(false);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:W caught:B1:detach:W:wrapped"));
        }

        // The button removed alongside is the control: it goes back to the pool.
        [Test]
        public void Given_TwoPooledButtonsOneOfWhichThrowsAsItDetaches_When_ADrainRemovesBoth_Then_OnlyTheOtherGoesBackToThePool()
        {
            // Arrange
            VNodePoolTestAccess.ClearButtonPoolForTest();
            var log = new EventLog();
            using var mounted = V.Mount(_mountPoint, log.Provide(V.Component(FirstBoundary,
                new Holds(V.Component(RemovesTwoButtons, key: "p")), key: "b1")), log.ReportingErrors());

            // Act
            log.Setter<bool>("P").Invoke(false);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(VNodePoolTestAccess.ButtonPoolCountForTest, Is.EqualTo(1));
        }
    }
}
