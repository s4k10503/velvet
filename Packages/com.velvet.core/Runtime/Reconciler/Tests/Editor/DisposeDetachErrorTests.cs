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
    /// once each element has left. An element whose removal threw is neither removed again nor pooled, so a callback in a
    /// clip-path wrapper or a pooled button runs once and is reported once, and a pooled button whose own callback threw
    /// stays out of the pool. A <c>z-*</c> element in a row whose removal threw is removed again only where that throw
    /// came ahead of its own detach, so its callback runs once either way, a pooled one rented again included, whether it
    /// attaches as a <c>z-*</c> element or is relocated where it stands, and a <c>z-*</c> button a healthy removal of its row
    /// took still goes back to the pool, carrying nothing the relocation registered. A <c>V.VirtualList</c>'s row is
    /// reported the same way where the disposal removes the list, as is an element beside a list of components; where a
    /// drain's update removes it, each of two rows is detached and reported; under a reconciler no root owns, the first of
    /// two rows' errors is thrown once both have detached. A host that disposes its root as it detaches is left without
    /// the root's <c>z-*</c> element.
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
        private static VNode ListRow() => V.Div();

        private static VNode AListOf(Func<string, VNode> renderer, params string[] items)
            => V.VirtualList(items: items, keySelector: item => item, itemHeight: 50f, renderer: renderer, overscan: 0);

        private static VNode AListOfThrowers(params string[] items)
            => AListOf(item => V.Custom<DetachThrower>(name: item, key: item), items);

        // The list is the only child of its parent's walk, so a removal of it takes the fast path, which cleans the list up
        // as the walk removes it.
        [Component(Compiler = false)]
        private static VNode RemovesAListOfThrowers()
        {
            var (shown, _) = EventLog.UseState("V", true);
            EventLog.UseRender("V");
            return V.Div(children: shown ? new[] { AListOfThrowers("R1", "R2") } : Array.Empty<VNode>());
        }

        // EditMode runs no layout, so the list is given the range its scroll would give it, rows of it tall.
        private void ShowTheListsRows(MountedTree mounted, int rows = 1)
        {
            mounted.Root.Reconciler!.Context.VirtualListControllers[_mountPoint.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f * rows);
            mounted.DrainForTest();
        }

        // As ShowTheListsRows, for a list a reconciler no root owns renders, sized for two rows.
        private static void ShowTwoRows(ReconcilerScope scope)
            => scope.Reconciler.Context.VirtualListControllers[scope.Root.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 100f);

        // The button is plain until Lift is set, when it is relocated where it stands, with no attach behind it.
        [Component(Compiler = false)]
        private static VNode LiftsARentedButton()
        {
            var (lifted, _) = EventLog.UseState("Lift", false);
            return V.Div(className: "relative", children: new VNode[]
            {
                V.Custom<DetachThrower>(name: "X", key: "x"),
                V.Button(className: lifted ? "absolute z-10" : null, key: "z", name: "P",
                    refCallback: s_registersAThrowingDetach),
            });
        }

        // Pools a z-* button: the healthy removal of its row takes it off the panel and the release returns it.
        private void PoolAZPlacedButton()
        {
            VNodePoolTestAccess.ClearButtonPoolForTest();
            V.Mount(_mountPoint, V.Div(className: "relative", key: "row", children: new VNode[]
            {
                V.Button(className: "absolute z-10", key: "z"),
            })).Dispose();
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

        // GREEN_ON_BASE(characterization): UI Toolkit leaves a child the detach of a removal never reached naming its panel.
        // FiberElementCleaner.ReleaseRetiredZLayerReals removes such a z-* element again for that; an engine that clears
        // the panel anyway reddens this.
        [Test]
        public void Given_AContainerWhoseFirstChildThrowsAsItDetaches_When_TheContainerIsRemovedFromItsPanel_Then_ItsSecondChildStillNamesThatPanel()
        {
            // Arrange
            var container = new VisualElement();
            container.Add(new DetachThrower());
            var second = new VisualElement();
            container.Add(second);
            _mountPoint.Add(container);

            // Act
            RecordingWhatItThrows(() => _mountPoint.Remove(container), _reports.Add);

            // Assert
            Assert.That(second.panel, Is.SameAs(_host.Panel));
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

        [Test]
        public void Given_AVirtualListWhoseRowThrowsAsItDetaches_When_TheHandleIsDisposed_Then_TheRootReportsTheError()
        {
            // Arrange
            var mounted = V.Mount(_mountPoint, V.Div(children: new[] { AListOfThrowers("R") }),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });
            ShowTheListsRows(mounted);

            // Act
            RecordingWhatItThrows(mounted.Dispose, _reports.Add);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("detach:R"));
        }

        // Each row's removal is its own, so the first one's error neither cuts the render short nor keeps the second from
        // leaving the panel.
        [Test]
        public void Given_ABoundaryAboveAComponentWhoseVirtualListRowsBothThrowAsTheyDetach_When_ADrainRemovesTheList_Then_TheBoundaryReportsEachRowsError()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_mountPoint, log.Provide(V.Component(FirstBoundary,
                new Holds(V.Component(RemovesAListOfThrowers, key: "v")), key: "b1")), log.ReportingErrors());
            ShowTheListsRows(mounted, rows: 2);
            log.Clear();

            // Act
            log.Setter<bool>("V").Invoke(false);
            RecordingWhatItThrows(mounted.GetSchedulerForTest().DrainImmediateForTest, log.Add);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:V caught:B1:detach:R1 caught:B1:detach:R2"));
        }

        // The list's row is a component, which mounts under an element of its own and unmounts its rows through the pass
        // the disposal runs, ahead of the removal that throws.
        [Test]
        public void Given_AVirtualListOfComponentsBesideAnElementThatThrowsAsItDetaches_When_TheHandleIsDisposed_Then_TheRootReportsTheError()
        {
            // Arrange
            var mounted = V.Mount(_mountPoint, V.Div(children: new VNode[]
                {
                    AListOf(item => V.Component(ListRow, key: item), "R"),
                    V.Custom<DetachThrower>(name: "X", key: "x"),
                }),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });
            ShowTheListsRows(mounted);

            // Act
            RecordingWhatItThrows(mounted.Dispose, _reports.Add);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("detach:X"));
        }

        // A's callback throws ahead of B's in the order UI Toolkit detaches the row's elements in.
        [Test]
        public void Given_ARowWhoseElementThrowsAsItDetachesAheadOfAZPlacedElementThatThrowsToo_When_TheHandleIsDisposed_Then_EachCallbackRunsOnceAndBothAreReported()
        {
            // Arrange
            var mounted = V.Mount(_mountPoint, V.Div(className: "relative", key: "row", children: new VNode[]
                {
                    V.Custom<DetachThrower>(name: "A", key: "a"),
                    V.Custom<DetachThrower>(className: "absolute z-10", name: "B", key: "b"),
                }),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });

            // Act
            RecordingWhatItThrows(mounted.Dispose, _reports.Add);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("detach:A / detach:B:z"));
        }

        // Both rows carry one name: which of the two fails first is not what this pins.
        [Test]
        public void Given_AVirtualListWhoseTwoRowsThrowAsTheyDetachUnderAReconcilerNoRootOwns_When_AReconcileRemovesTheList_Then_TheFirstErrorIsThrownOnceBothHaveDetached()
        {
            // Arrange — the entry carries how many detach callbacks had run when the error reached the case
            using var scope = new ReconcilerScope();
            _host.Root.Add(scope.Root);
            var before = new[] { AListOf(item => V.Custom<DetachThrower>(name: "R", key: item), "first", "second") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            ShowTwoRows(scope);

            // Act
            RecordingWhatItThrows(() => scope.Reconciler.Reconcile(scope.Root, before, Array.Empty<VNode>()),
                entry => _reports.Add(entry + "@" + s_detached));

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("threw:detach:R@2"));
        }

        // GREEN_ON_BASE(characterization): the base pools a z-* button the healthy removal of its row took with it.
        // It reddens where the z-* release skips an element whose detach went out without a throw anywhere above it.
        [Test]
        public void Given_AZPlacedButtonInARow_When_TheHandleIsDisposed_Then_TheButtonGoesBackToThePool()
        {
            // Arrange
            VNodePoolTestAccess.ClearButtonPoolForTest();
            var mounted = V.Mount(_mountPoint, V.Div(className: "relative", key: "row", children: new VNode[]
            {
                V.Button(className: "absolute z-10", key: "z"),
            }));

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(VNodePoolTestAccess.ButtonPoolCountForTest, Is.EqualTo(1));
        }

        // The button the second tree rents is the one the first tree's row took off the panel and pooled, which has
        // attached again since.
        [Test]
        public void Given_APooledZPlacedButtonRentedAgainBehindAnElementThatThrowsAsItDetaches_When_TheSecondHandleIsDisposed_Then_TheButtonsCallbackRunsAndBothAreReported()
        {
            // Arrange
            VNodePoolTestAccess.ClearButtonPoolForTest();
            V.Mount(_mountPoint, V.Div(className: "relative", key: "row", children: new VNode[]
            {
                V.Button(className: "absolute z-10", key: "z"),
            })).Dispose();
            var mounted = V.Mount(_mountPoint, V.Div(className: "relative", key: "row", children: new VNode[]
                {
                    V.Custom<DetachThrower>(name: "X", key: "x"),
                    V.Button(className: "absolute z-10", key: "z", name: "P", refCallback: s_registersAThrowingDetach),
                }),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });

            // Act
            RecordingWhatItThrows(mounted.Dispose, _reports.Add);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("detach:X / detach:P"));
        }

        // GREEN_ON_BASE(characterization): the base registers nothing on a z-* button that a pool hands to a later tree.
        // Leaving out either UnregisterCallback in FiberZLayerCoordinator.Release reddens this.
        [Test]
        public void Given_AZPlacedButtonAHealthyRemovalPooled_When_ATreeRentsItBesideAFreshButton_Then_ItCarriesNoMoreTrickleDownCallbacksThanTheFreshOne()
        {
            // Arrange
            PoolAZPlacedButton();

            // Act
            using var mounted = V.Mount(_mountPoint, V.Div(key: "row", children: new VNode[]
            {
                V.Button(key: "rented", name: "rented"),
                V.Button(key: "fresh", name: "fresh"),
            }));

            // Assert
            Assert.That(CallbackRegistryProbe.TrickleDownCallbackCount(_mountPoint.Q<Button>("rented")),
                Is.EqualTo(CallbackRegistryProbe.TrickleDownCallbackCount(_mountPoint.Q<Button>("fresh"))));
        }

        // The rented button attaches as a plain one and is relocated where it stands, so no attach clears what an earlier
        // tenancy noted; FiberZLayerCoordinator.Release drops that note with the role.
        [Test]
        public void Given_APooledZPlacedButtonRentedPlainAndRelocatedBehindAnElementThatThrowsAsItDetaches_When_TheHandleIsDisposed_Then_TheButtonsCallbackRunsAndBothAreReported()
        {
            // Arrange
            PoolAZPlacedButton();
            var log = new EventLog();
            var mounted = V.Mount(_mountPoint, log.Provide(V.Component(LiftsARentedButton, key: "l")),
                new MountOptions { OnUncaughtError = (error, _) => _reports.Add(error.Message) });
            log.Setter<bool>("Lift").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            RecordingWhatItThrows(mounted.Dispose, _reports.Add);

            // Assert
            Assert.That(string.Join(" / ", _reports), Is.EqualTo("detach:X / detach:P"));
        }

        // GREEN_ON_BASE(characterization): the base removes the z-* element from a host its own detach disposes the root in.
        // Nothing throws here, so the element's removal is an ordinary one, made while UI Toolkit is still detaching the
        // host.
        [Test]
        public void Given_AHostThatDisposesItsRootAsItDetaches_When_TheHostIsRemovedWithAZPlacedElementInIt_Then_TheElementLeavesTheHost()
        {
            // Arrange
            var host = new VisualElement();
            _mountPoint.Add(host);
            var mounted = V.Mount(host, V.Div(className: "absolute z-10", name: "Z", key: "z"));
            host.RegisterCallback<DetachFromPanelEvent>(_ => mounted.Dispose());

            // Act
            _mountPoint.Remove(host);

            // Assert
            Assert.That(host.childCount, Is.EqualTo(0));
        }
    }
}
