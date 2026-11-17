using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where a wave runs the insertion effects and layout cleanups of a component it updates or mounts, and the
    /// detach of a ref it replaces: as its writes apply, at the component's or the element's turn among the teardowns
    /// of the components it removes, where react-reconciler 19.1's <c>commitMutationEffectsOnFiber</c> runs them in its
    /// one walk of the tree. The cases red on the base expect what React does for their trees, read from its source
    /// rather than run. The rest hold what the base already did: an error a new element throws as it attaches, as a row
    /// beside a later component, inside an element of its own or relocated as a z-* element, is reported ahead of that
    /// component's layout cleanup's, as a deletion's teardown's is ahead of a new Portal child's, and one a replaced
    /// ref's detach throws reaches the boundary in a follow-up commit; the effect runs with the render's own closure; a
    /// component a walk's boundary removes runs only its teardown, its cleanups ahead of its element's ref detach; a
    /// render a suspend abandons promotes no deps; and a wave whose work the layout commit has to run keeps that
    /// commit's order, where it renders a component row after its host, parks or resumes a time-sliced render, mounts a
    /// Portal, renders what a settle owes, or mounts a component holding insertion effects under one a commit has
    /// taken.
    /// </summary>
    [TestFixture]
    internal sealed class TurnEndOrderTests
    {
        private VisualElement _root;

        private static VisualElement s_root;
        private static VisualElement s_portalTarget;
        private static readonly List<string> s_log = new();
        private static StateUpdater<int> s_setHost;
        private static StateUpdater<int> s_setRow;
        private static TransitionStarter s_start;
        private static Action<string> s_add;
        private static StateUpdater<int> s_setTick;
        private static ComponentFiber s_sibling;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_root = _root;
            s_portalTarget = new VisualElement();
            s_log.Clear();
            s_setHost = default;
            s_setRow = default;
            s_start = default;
            s_add = null;
            s_setTick = default;
            s_sibling = null;
            s_read = null;
            FiberStrictMode.Enabled = false;
            AsyncActionsInFlightTestAccess.ResetForTest();
        }

        [TearDown]
        public void TearDown()
        {
            FiberStrictMode.Enabled = false;
            AsyncActionsInFlightTestAccess.ResetForTest();
        }

        #region An update or a mount ahead of a deletion

        [Component]
        private static VNode Updated(bool shown)
        {
            EventLog.UseInsertionAndLayoutEffects("U", new object[] { shown });
            return V.Div();
        }

        [Component]
        private static VNode Deleted()
        {
            EventLog.UseInsertionAndLayoutEffects("D", Array.Empty<object>());
            return V.Div();
        }

        [Component]
        private static VNode UpdatedThenDeleted()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(Updated, shown, key: "u"),
                V.Div(key: "x", children: new VNode[] { shown ? V.Component(Deleted, key: "d") : null }));
        }

        [Test]
        public void Given_AComponentUpdatedAheadOfAnElementWhoseChildIsDeleted_When_OneDrainRendersBoth_Then_TheUpdatedComponentsInsertionEffectAndLayoutCleanupRunFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(UpdatedThenDeleted)));
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:U ins:U lay-x:U ins-x:D lay-x:D lay:U"));
        }

        [Component]
        private static VNode RefThenDeleted()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            var first = EventLog.UseRefCallback<VisualElement>("R1");
            var second = EventLog.UseRefCallback<VisualElement>("R2");
            return V.Fragment(
                V.Div(key: "r", refCallback: shown ? first : second),
                V.Div(key: "x", children: new VNode[] { shown ? V.Component(Deleted, key: "d") : null }));
        }

        [Test]
        public void Given_AnElementWhoseRefIsReplacedAheadOfAnElementWhoseChildIsDeleted_When_OneDrainRendersBoth_Then_TheReplacedRefDetachesFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(RefThenDeleted)));
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ref-x:R1 ins-x:D lay-x:D ref:R2"));
        }

        // Layout effects alone, so its turn has layout cleanups and no insertion effect.
        [Component]
        private static VNode LaysOut()
        {
            var (tick, _) = EventLog.UseState("A", 0);
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:A");
                return () => log.Add("lay-x:A");
            }, new object[] { tick });
            return V.Div();
        }

        // An insertion effect alone, beside the child it deletes.
        [Component]
        private static VNode InsertsAndDeletes()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            var log = EventLog.UseLog();
            Hooks.UseInsertionEffect(() =>
            {
                log.Add("ins:B");
                return () => log.Add("ins-x:B");
            }, new object[] { shown });
            return V.Div(children: new VNode[] { shown ? V.Component(Deleted, key: "d") : null });
        }

        [Component]
        private static VNode TwoRoots()
            => V.Fragment(V.Component(LaysOut, key: "a"), V.Component(InsertsAndDeletes, key: "b"));

        [Test]
        public void Given_TwoSiblingRootsTheSecondOfWhichDeletesAChild_When_TheSecondIsSetFirstAndOneDrainRendersBoth_Then_TheFirstsCleanupsTheDeletionAndTheSecondsCleanupsRunInThatOrder()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(TwoRoots)));
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            log.Setter<int>("A").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay-x:A ins-x:D lay-x:D ins-x:B ins:B lay:A"));
        }

        [Component]
        private static VNode InsertsOnMount()
        {
            var log = EventLog.UseLog();
            Hooks.UseInsertionEffect(() =>
            {
                log.Add("ins:M");
                return () => log.Add("ins-x:M");
            }, Array.Empty<object>());
            return V.Div();
        }

        // New with the component it holds, which is then not the one React places.
        [Component]
        private static VNode HoldsAnInsertingMount() => V.Component(InsertsOnMount, key: "m");

        // The first element holds no child until the update, so the container the mount lands in records no turn begin.
        [Component]
        private static VNode MountThenDeletion()
        {
            var (shown, _) = EventLog.UseState("Shown", false);
            return V.Fragment(
                V.Div(key: "m", children: shown
                    ? new VNode[] { V.Component(HoldsAnInsertingMount, key: "h") }
                    : Array.Empty<VNode>()),
                V.Div(key: "x", children: new VNode[] { shown ? null : V.Component(Deleted, key: "d") }));
        }

        [Test]
        public void Given_AComponentMountedInAnEmptyElementAheadOfAnElementWhoseChildIsDeleted_When_OneDrainRendersBoth_Then_TheMountsInsertionEffectRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(MountThenDeletion)));
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins:M ins-x:D lay-x:D"));
        }

        // A layout effect alone, so its mount has a turn with nothing for that turn to run.
        [Component]
        private static VNode LaysOutOnMount()
        {
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:M");
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div();
        }

        [Component]
        private static VNode UpdatedThenDeletedBesideAMount()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(Updated, shown, key: "u"),
                V.Div(key: "x", children: new VNode[] { shown ? V.Component(Deleted, key: "d") : null }),
                V.Div(key: "m", children: shown
                    ? Array.Empty<VNode>()
                    : new VNode[] { V.Component(LaysOutOnMount, key: "m") }));
        }

        [Test]
        public void Given_AComponentUpdatedAheadOfADeletionInTheRenderThatMountsALayoutEffectBesideThem_When_OneDrainRendersIt_Then_TheUpdatedComponentsCleanupsStillRunFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(UpdatedThenDeletedBesideAMount)));
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:U ins:U lay-x:U ins-x:D lay-x:D lay:U lay:M"));
        }

        // The element around the mount is new with it, so that element is the one React places.
        [Component]
        private static VNode MountInANewElementThenDeletion()
        {
            var (shown, _) = EventLog.UseState("Shown", false);
            return V.Fragment(
                V.Div(key: "m", children: new VNode[]
                {
                    shown ? V.Div(key: "new", children: new VNode[] { V.Component(InsertsOnMount, key: "m") }) : null,
                }),
                V.Div(key: "x", children: new VNode[] { shown ? null : V.Component(Deleted, key: "d") }));
        }

        [Test]
        public void Given_AComponentMountedInAnElementNewWithItAheadOfAnElementWhoseChildIsDeleted_When_OneDrainRendersBoth_Then_TheMountsInsertionEffectRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(MountInANewElementThenDeletion)));
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins:M ins-x:D lay-x:D"));
        }

        // Reads, as its insertion effect runs, whether the element it rendered is in the tree.
        [Component]
        private static VNode InsertsAndLooksForItsElement()
        {
            var log = EventLog.UseLog();
            Hooks.UseInsertionEffect((Func<Action>)(() =>
            {
                log.Add(s_root.Q<VisualElement>("placed") != null ? "ins:placed" : "ins:unplaced");
                return null;
            }), Array.Empty<object>());
            return V.Div(name: "placed");
        }

        [Component]
        private static VNode MountsUnderItself()
        {
            var (shown, _) = EventLog.UseState("Shown", false);
            return V.Div(children: shown
                ? new VNode[] { V.Component(InsertsAndLooksForItsElement, key: "m") }
                : Array.Empty<VNode>());
        }

        // GREEN_ON_BASE(characterization): the base runs a mount's insertion effects once the wave's writes have applied.
        // Making FiberWave.KeepBatchOrderForAPlacement return at once reddens it: the effect then runs at the component's
        // turn, ahead of the insert that places its element, which React makes first where the component itself is what
        // its commit places: new under one that commit has taken, in an element the commit does not create.
        [Test]
        public void Given_AComponentMountedUnderOneACommitHasTaken_When_ItsInsertionEffectRuns_Then_ItFindsItsElementPlaced()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(MountsUnderItself)));
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins:placed"));
        }

        #endregion

        #region Errors the turn ends throw

        // The cleanup of the mount's effect throws; the one the update leaves does not, so the boundary's fallback
        // takes the component down without a second error.
        [Component]
        private static VNode UpdatedThrowing(bool shown)
        {
            Hooks.UseLayoutEffect(() => () =>
            {
                if (shown) throw new InvalidOperationException("U");
            }, new object[] { shown });
            return V.Div();
        }

        [Component]
        private static VNode DeletedThrowing()
        {
            Hooks.UseLayoutEffect(() => () => throw new InvalidOperationException("D"), Array.Empty<object>());
            return V.Div();
        }

        [Component]
        private static VNode QuietFallback() => V.Div();

        [Component(IsErrorBoundary = true, DisplayName = "Guard")]
        private static VNode GuardOverAnUpdateAndADeletion()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            Hooks.UseFallback(_ => V.Component(QuietFallback));
            return V.Fragment(
                V.Component(UpdatedThrowing, shown, key: "u"),
                V.Div(key: "x", children: new VNode[] { shown ? V.Component(DeletedThrowing, key: "d") : null }));
        }

        [Test]
        public void Given_ABoundaryOverAnUpdatedComponentAheadOfADeletedOneWhoseLayoutCleanupsBothThrow_When_TheyCommit_Then_TheUpdatedComponentsErrorIsReportedFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(GuardOverAnUpdateAndADeletion)),
                log.ReportingErrors());
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:Guard:U caught:Guard:D"));
        }

        // Stands in for a V.Custom<T> whose element registers a panel-attach callback that throws. The message says
        // whether the element attached as a relocated z-* element, so a case about one fails where the arrangement stops
        // making one.
        internal sealed class ThrowsAsItAttaches : VisualElement
        {
            public ThrowsAsItAttaches()
                => RegisterCallback<AttachToPanelEvent>(_ => throw new InvalidOperationException(
                    "attach:" + name + (FiberZLayerCoordinator.IsRelocatedReal(this) ? ":z" : "")));
        }

        private static MountedTree MountOnAPanel(HeadlessEditorPanelHost host, EventLog log, Func<VNode> render)
        {
            var mountPoint = new VisualElement();
            host.Root.Add(mountPoint);
            var mounted = V.Mount(mountPoint, log.Provide(V.Component(render)), log.ReportingErrors());
            log.Clear();
            return mounted;
        }

        // The new element is a row of the container the component's rows are in, so its insert is recorded where that
        // container's walk ends, behind the component's turn.
        [Component(IsErrorBoundary = true, DisplayName = "Guard")]
        private static VNode GuardOverANewRowAheadOfAnUpdate()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            Hooks.UseFallback(_ => V.Component(QuietFallback));
            return V.Fragment(
                shown ? null : V.Custom<ThrowsAsItAttaches>(name: "A", key: "thrower"),
                V.Component(UpdatedThrowing, shown, key: "u"));
        }

        // GREEN_ON_BASE(characterization): the base reports an error a wave's write throws ahead of a layout cleanup's.
        // Filing a failed insert in ChildListWrites.FileFailure at the place of the insert's op, rather than at the place
        // its element's walk ended at, reddens it: the insert applies behind the component's turn.
        [Test]
        public void Given_ABoundaryOverANewRowThatThrowsAsItAttachesAheadOfAComponentWhoseLayoutCleanupThrows_When_TheyCommit_Then_TheAttachErrorIsReportedFirst()
        {
            // Arrange — a real panel, which is what runs an attach callback
            using var host = new HeadlessEditorPanelHost();
            var log = new EventLog();
            using var mounted = MountOnAPanel(host, log, GuardOverANewRowAheadOfAnUpdate);

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:Guard:attach:A caught:Guard:U"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "Guard")]
        private static VNode GuardOverAnUpdateAheadOfANewRow()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            Hooks.UseFallback(_ => V.Component(QuietFallback));
            return V.Fragment(
                V.Component(UpdatedThrowing, shown, key: "u"),
                shown ? null : V.Custom<ThrowsAsItAttaches>(name: "A", key: "thrower"));
        }

        [Test]
        public void Given_ABoundaryOverAComponentWhoseLayoutCleanupThrowsAheadOfANewRowThatThrowsAsItAttaches_When_TheyCommit_Then_TheCleanupsErrorIsReportedFirst()
        {
            // Arrange — a real panel, which is what runs an attach callback
            using var host = new HeadlessEditorPanelHost();
            var log = new EventLog();
            using var mounted = MountOnAPanel(host, log, GuardOverAnUpdateAheadOfANewRow);

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:Guard:U caught:Guard:attach:A"));
        }

        // The new element sits in an element of its own, so its insert applies as that element's walk ends, ahead of
        // the component's turn.
        [Component(IsErrorBoundary = true, DisplayName = "Guard")]
        private static VNode GuardOverANewElementAheadOfAnUpdate()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            Hooks.UseFallback(_ => V.Component(QuietFallback));
            return V.Fragment(
                V.Div(key: "a", children: new VNode[]
                {
                    shown ? null : V.Custom<ThrowsAsItAttaches>(name: "A", key: "thrower"),
                }),
                V.Component(UpdatedThrowing, shown, key: "u"));
        }

        // GREEN_ON_BASE(characterization): the base reports an error a wave's write throws ahead of a layout cleanup's.
        // Leaving ReconcilePass.HoldsCaptures unset in ChildListWrites.Apply reddens it: the cleanup's error is then
        // captured as the component's turn throws it, ahead of the attach error the wave routes once the pass has applied.
        [Test]
        public void Given_ABoundaryOverANewElementThatThrowsAsItAttachesAheadOfAComponentWhoseLayoutCleanupThrows_When_TheyCommit_Then_TheAttachErrorIsReportedFirst()
        {
            // Arrange — a real panel, which is what runs an attach callback
            using var host = new HeadlessEditorPanelHost();
            var log = new EventLog();
            using var mounted = MountOnAPanel(host, log, GuardOverANewElementAheadOfAnUpdate);

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:Guard:attach:A caught:Guard:U"));
        }

        // The new element is relocated (FiberZLayerCoordinator), so it attaches where the pass's drain resolves its
        // queued mount, after the pass has applied.
        [Component(IsErrorBoundary = true, DisplayName = "Guard")]
        private static VNode GuardOverANewStackedElementAheadOfAnUpdate()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            Hooks.UseFallback(_ => V.Component(QuietFallback));
            return V.Fragment(
                V.Div(key: "a", className: "relative", children: new VNode[]
                {
                    shown ? null : V.Custom<ThrowsAsItAttaches>(className: "absolute z-10", name: "Z", key: "thrower"),
                }),
                V.Component(UpdatedThrowing, shown, key: "u"));
        }

        // GREEN_ON_BASE(characterization): the base reports an error a wave's write throws ahead of a layout cleanup's.
        // Filing a relocated element's failed placement in ChildReconciler.PlaceRelocated at the place behind every op
        // applied, rather than at ZLayerMountNode.Place, reddens it: the placement then follows the component's turn.
        [Test]
        public void Given_ABoundaryOverANewStackedElementThatThrowsAsItAttachesAheadOfAComponentWhoseLayoutCleanupThrows_When_TheyCommit_Then_TheAttachErrorIsReportedFirst()
        {
            // Arrange — a real panel, which is what runs an attach callback
            using var host = new HeadlessEditorPanelHost();
            var log = new EventLog();
            using var mounted = MountOnAPanel(host, log, GuardOverANewStackedElementAheadOfAnUpdate);

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:Guard:attach:Z:z caught:Guard:U"));
        }

        // The Portal ends the pass at its mount, which follows the apply that runs the deletion's teardown.
        [Component(IsErrorBoundary = true, DisplayName = "Guard")]
        private static VNode GuardOverADeletionAheadOfANewPortal()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            Hooks.UseFallback(_ => V.Component(QuietFallback));
            return V.Fragment(
                V.Div(key: "x", children: new VNode[] { shown ? V.Component(DeletedThrowing, key: "d") : null }),
                shown
                    ? null
                    : V.Portal(s_portalTarget, new VNode[] { V.Custom<ThrowsAsItAttaches>(name: "P", key: "thrower") },
                        key: "portal"));
        }

        // GREEN_ON_BASE(characterization): the base reports what a deletion's teardown throws ahead of a Portal child's.
        // Deleting the count of the ops applied from ChildListWrites.Apply reddens it: the Portal's mount, which runs
        // after the apply, then takes the place ahead of every op.
        [Test]
        public void Given_ABoundaryOverAnElementWhoseChildsDeletionThrowsAheadOfANewPortalWhoseChildThrowsAsItAttaches_When_TheyCommit_Then_TheDeletionsErrorIsReportedFirst()
        {
            // Arrange — a real panel, which is what runs an attach callback, holding the Portal's target
            using var host = new HeadlessEditorPanelHost();
            host.Root.Add(s_portalTarget);
            var log = new EventLog();
            using var mounted = MountOnAPanel(host, log, GuardOverADeletionAheadOfANewPortal);

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:Guard:D caught:Guard:attach:P"));
        }

        private static readonly Func<VisualElement, Action> s_throwingRef =
            _ => () => throw new InvalidOperationException("R1");

        private static readonly Func<VisualElement, Action> s_quietRef = _ => null;

        [Component]
        private static VNode SwapsAThrowingRef(bool shown) => V.Div(refCallback: shown ? s_throwingRef : s_quietRef);

        [Component]
        private static VNode LoggedFallback()
        {
            EventLog.UseRender("Fb");
            return V.Div();
        }

        [Component(IsErrorBoundary = true, DisplayName = "Guard")]
        private static VNode GuardOverARefSwap()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            Hooks.UseFallback(_ => V.Component(LoggedFallback));
            return V.Component(SwapsAThrowingRef, shown, key: "s");
        }

        // GREEN_ON_BASE(characterization): the base runs a replaced ref's cleanup in the layout commit, at commit depth.
        // Dropping the EffectCommitDepth raise around DetachReplacedRef in ReconcilerContext.DetachReplacedRefAtItsTurn
        // reddens it.
        [Test]
        public void Given_ABoundaryOverAnElementWhoseReplacedRefsCleanupThrows_When_TheReplacementCommits_Then_TheBoundaryReportsItFromItsFallbacksCommit()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(GuardOverARefSwap)), log.ReportingErrors());
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Fb caught:Guard:R1"));
        }

        #endregion

        #region What a turn end runs with

        [Component]
        private static VNode InsertsItsValue()
        {
            var (value, _) = EventLog.UseState("Value", 0);
            var log = EventLog.UseLog();
            Hooks.UseInsertionEffect(() =>
            {
                log.Add("ins:" + value);
                return () => log.Add("ins-x:" + value);
            }, new object[] { value });
            return V.Div();
        }

        // GREEN_ON_BASE(characterization): the base runs the insertion effect with the factory the journal promoted.
        // Deleting the promotion FiberEffects.RunTurnEnd makes for a render the journal holds reddens it: the effect then
        // runs with the previous render's closure.
        [Test]
        public void Given_AComponentWhoseInsertionEffectReadsItsState_When_TheStateChangesAndADrainRendersIt_Then_TheEffectRunsWithTheNewValue()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(InsertsItsValue)));
            log.Clear();

            // Act
            log.Setter<int>("Value").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:0 ins:1"));
        }

        // The layout effect is set up twice where StrictMode reaches the mount, which the reading carries.
        [Component]
        private static VNode StrictInserts()
        {
            EventLog.UseInsertionAndLayoutEffects("S", Array.Empty<object>());
            return V.Div();
        }

        // New with the component it holds, so the mount runs that component's insertion effect at its turn.
        [Component]
        private static VNode HoldsStrictInserts() => V.Component(StrictInserts, key: "s");

        // React's doubleInvokeEffectsOnFiber runs a mount's layout and passive effects a second time, not its insertion
        // effects.
        [Test]
        public void Given_StrictModeOn_When_AComponentWithAnInsertionAndALayoutEffectMounts_Then_OnlyTheLayoutEffectIsSetUpTwice()
        {
            // Arrange
            var log = new EventLog();
            FiberStrictMode.Enabled = true;

            // Act
            using var mounted = V.Mount(_root, log.Provide(V.Component(HoldsStrictInserts)));

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins:S lay:S lay-x:S lay:S"));
        }

        #endregion

        #region A component row below its host

        [Component]
        private static VNode RowWithACleanup()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setRow = setTick;
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("lay:R");
                return () => s_log.Add("lay-x:R");
            }, new object[] { tick });
            return V.Label(text: "row");
        }

        [Component]
        private static VNode ListHostWithACleanup()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setHost = setTick;
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("lay:H");
                return () => s_log.Add("lay-x:H");
            }, new object[] { tick });
            return V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: tick == 0 ? Array.Empty<string>() : new[] { "row" },
                    keySelector: item => item,
                    itemHeight: 50f,
                    renderer: item => V.Component(RowWithACleanup, key: item),
                    overscan: 0,
                    key: "list"),
            });
        }

        // GREEN_ON_BASE(characterization): the base runs a row's layout cleanup before its host's, in the layout commit.
        // Deleting the FiberWave.KeepBatchOrder call in Reconciler.BeginOrJoinPass reddens it: the host's turn then ends
        // as its pass applies, ahead of the row's, which renders after it.
        [Test]
        public void Given_AVirtualListRowAndItsHostBothSet_When_OneDrainRendersBoth_Then_TheRowsLayoutCleanupRunsBeforeTheHosts()
        {
            // Arrange — the viewport height a geometry pass would have left, so the host's render renders a range
            using var mounted = V.Mount(_root, V.Component(ListHostWithACleanup, key: "host"));
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()], 50f);
            s_setHost.Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_log.Clear();

            // Act
            s_setRow.Invoke(1);
            s_setHost.Invoke(2);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(string.Join(" ", s_log), Is.EqualTo("lay-x:R lay-x:H lay:R lay:H"));
        }

        private static readonly Func<VisualElement, Action> s_evenTickRef = _ =>
        {
            s_log.Add("ref:O");
            return () => s_log.Add("ref-x:O");
        };

        private static readonly Func<VisualElement, Action> s_oddTickRef = _ =>
        {
            s_log.Add("ref:O");
            return () => s_log.Add("ref-x:O");
        };

        // The list sits in an element whose ref callback alternates between two delegates, so each render of the host
        // that moves its tick replaces that ref.
        [Component]
        private static VNode ListInAReffedElement()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setHost = setTick;
            Hooks.UseLayoutEffect(() =>
            {
                s_log.Add("lay:H");
                return () => s_log.Add("lay-x:H");
            }, new object[] { tick });
            return V.Div(
                refCallback: tick % 2 == 0 ? s_evenTickRef : s_oddTickRef,
                children: new VNode[]
                {
                    V.VirtualList(
                        items: tick == 0 ? Array.Empty<string>() : new[] { "row" },
                        keySelector: item => item,
                        itemHeight: 50f,
                        renderer: item => V.Component(RowWithACleanup, key: item),
                        overscan: 0,
                        key: "list"),
                });
        }

        // GREEN_ON_BASE(characterization): the base detaches the element's ref after the row inside it is cleaned up.
        // Deleting the FiberWave.KeepBatchOrder call in Reconciler.BeginOrJoinPass reddens it: the host's walk then
        // detaches the ref as its pass applies, ahead of the row's cleanup, which renders after it.
        [Test]
        public void Given_AVirtualListRowInAnElementWhoseRefItsHostReplaces_When_OneDrainRendersBoth_Then_TheRowsLayoutCleanupRunsBeforeTheRefDetaches()
        {
            // Arrange — the viewport height a geometry pass would have left, so the host's render renders a range
            using var mounted = V.Mount(_root, V.Component(ListInAReffedElement, key: "host"));
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()], 50f);
            s_setHost.Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_log.Clear();

            // Act
            s_setRow.Invoke(1);
            s_setHost.Invoke(2);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(string.Join(" ", s_log.FindAll(entry => entry.Contains("-x:"))), Is.EqualTo("lay-x:R ref-x:O lay-x:H"));
        }

        #endregion

        #region A replaced ref of a removed component

        [Component]
        private static VNode RefOwner(bool shown)
        {
            EventLog.UseInsertionAndLayoutEffects("O", Array.Empty<object>());
            var first = EventLog.UseRefCallback<VisualElement>("R1");
            var second = EventLog.UseRefCallback<VisualElement>("R2");
            return V.Div(refCallback: shown ? first : second);
        }

        [Component]
        private static VNode ThrowsWhenHidden(bool shown)
            => shown ? V.Div() : throw new InvalidOperationException("boom");

        // Catches in its parent's walk, after the component before the thrower has queued its new ref.
        [Component(IsErrorBoundary = true, DisplayName = "Inline")]
        private static VNode InlineGuard(bool shown)
        {
            Hooks.UseFallback(_ => V.Component(QuietFallback));
            return V.Fragment(V.Component(RefOwner, shown, key: "o"), V.Component(ThrowsWhenHidden, shown, key: "t"));
        }

        [Component]
        private static VNode HostOfAnInlineGuard()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Component(InlineGuard, shown, key: "g");
        }

        // GREEN_ON_BASE(characterization): the base detaches the ref where the removed component's teardown reaches it.
        // Deleting `if (owner.IsBeingDeleted) return;` from ReconcilerContext.DetachReplacedRefAtItsTurn reddens it: the
        // ref then detaches ahead of the component's own cleanups.
        [Test]
        public void Given_AComponentThatReplacesItsElementsRefInAWalkItsBoundaryCatches_When_TheFallbackRemovesIt_Then_ItsCleanupsRunAheadOfTheRefsDetach()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(HostOfAnInlineGuard)), log.ReportingErrors());
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:O lay-x:O ref-x:R1 caught:Inline:boom"));
        }

        [Component]
        private static VNode ChangedInAFailedWalk(bool shown)
        {
            EventLog.UseInsertionAndLayoutEffects("X", new object[] { shown });
            return V.Div();
        }

        [Component(IsErrorBoundary = true, DisplayName = "Inline")]
        private static VNode InlineGuardOverAChange(bool shown)
        {
            Hooks.UseFallback(_ => V.Component(QuietFallback));
            return V.Fragment(
                V.Component(ChangedInAFailedWalk, shown, key: "x"),
                V.Component(ThrowsWhenHidden, shown, key: "t"));
        }

        [Component]
        private static VNode HostOfAnInlineGuardOverAChange()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Component(InlineGuardOverAChange, shown, key: "g");
        }

        // GREEN_ON_BASE(characterization): the base runs none of the failed walk's queued work of a component it removes.
        // Deleting `|| fiber.IsBeingDeleted` from FiberEffects.RunTurnEnd reddens it: the component's turn then runs the
        // insertion effect its failed render queued, which its teardown cleans up again.
        [Test]
        public void Given_AComponentWhoseDepsChangeInAWalkItsBoundaryCatches_When_TheFallbackRemovesIt_Then_OnlyItsTeardownRuns()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(HostOfAnInlineGuardOverAChange)),
                log.ReportingErrors());
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:X lay-x:X caught:Inline:boom"));
        }

        #endregion

        #region A time-sliced render

        private sealed class Slicing
        {
            internal ComponentFiber Fiber;
            internal int Version;
            internal int QuietRows;
        }

        private sealed record SlicedDep(string Name, int Version);

        [Component]
        private static VNode ProbedSlicedRow(SlicedDep props)
        {
            EventLog.UseInsertionAndLayoutEffects(props.Name, new object[] { props.Version });
            return V.Fragment(Array.Empty<VNode>());
        }

        [Component]
        private static VNode QuietSlicedRow() => V.Fragment(Array.Empty<VNode>());

        // Built as PortalCommitOrderEventLogTests.SlicedRows is, and for its reasons.
        [Component(Compiler = false)]
        private static VNode SlicedRows(Slicing slicing)
        {
            slicing.Fiber = FiberAmbientStack.Current;
            var version = slicing.Version;
            var rows = new VNode[3];
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = V.Div(key: "row" + i, children: new VNode?[]
                {
                    i < slicing.QuietRows
                        ? V.Component(QuietSlicedRow, key: "row")
                        : V.Component(ProbedSlicedRow, new SlicedDep("R" + i, version), key: "row"),
                });
            }
            return V.Fragment(rows);
        }

        private static void SliceAndDrain(EventLog log, MountedTree mounted, Slicing slicing)
        {
            slicing.Version = 1;
            slicing.Fiber!.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            slicing.Fiber.FlushStateWithTinyBudgetForTest();
            log.Add(slicing.Fiber.HasPendingReconcileWorkForTest() ? "parked" : "never-parked");
            slicing.Fiber.DrainTimeSlicedReconcileForTest();
            mounted.DrainForTest();
        }

        // GREEN_ON_BASE(characterization): the base runs none of a parked render's cleanups while it is parked.
        // Deleting the FiberWave.KeepBatchOrder call in Reconciler.MarkOwnerParked reddens it: the first slice's row then
        // runs its cleanups as that slice's writes apply, ahead of the park.
        [Test]
        public void Given_ATimeSlicedTransitionOfRowsWithEffects_When_ItsFirstSliceParks_Then_NoCleanupRunsAheadOfThePark()
        {
            // Arrange
            var log = new EventLog();
            var slicing = new Slicing();
            using var mounted = V.Mount(_root, log.Provide(V.Component(SlicedRows, slicing)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            SliceAndDrain(log, mounted, slicing);

            // Assert — a pass that never parked logs another marker first
            Assert.That(log.ToString(), Does.StartWith("parked "));
        }

        // GREEN_ON_BASE(characterization): the base runs a time-sliced render's cleanups in tree order once it completes.
        // Deleting the FiberWave.KeepBatchOrder call in Reconciler.ContinueReconcileWork reddens it: the slice that
        // completes then runs its own row's cleanups as its writes apply, ahead of the rows earlier slices rendered.
        [Test]
        public void Given_ATimeSlicedTransitionWhoseFirstSlicedRowHasNoEffects_When_ItsSlicesComplete_Then_TheRowsCleanupsRunInTreeOrder()
        {
            // Arrange — the row the first slice renders has no effect, so nothing that slice renders has a turn to run
            var log = new EventLog();
            var slicing = new Slicing { QuietRows = 1 };
            using var mounted = V.Mount(_root, log.Provide(V.Component(SlicedRows, slicing)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            SliceAndDrain(log, mounted, slicing);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("parked ins-x:R1 ins:R1 lay-x:R1 ins-x:R2 ins:R2 lay-x:R2 lay:R1 lay:R2"));
        }

        #endregion

        #region A render a suspend abandons

        private static VelvetTaskCompletionSource<string> s_read;

        [Component]
        private static VNode InsertsAValue(int value)
        {
            var log = EventLog.UseLog();
            Hooks.UseInsertionEffect(() =>
            {
                log.Add("ins:" + value);
                return () => log.Add("ins-x:" + value);
            }, new object[] { value });
            return V.Div();
        }

        [Component(Compiler = false)]
        private static VNode SuspendsOnARead(bool reads)
        {
            Hooks.Use(() => reads ? s_read.Task : VelvetTask.FromResult("read"), reads);
            return V.Div();
        }

        // Its own render is the one a suspend nothing catches abandons, after the child before the reader has rendered.
        [Component]
        private static VNode RendersThenSuspends()
        {
            var (value, _) = EventLog.UseState("Value", 0);
            var (reads, _) = EventLog.UseState("Reads", false);
            return V.Fragment(
                V.Component(InsertsAValue, value, key: "v"),
                V.Component(SuspendsOnARead, reads, key: "s"));
        }

        // GREEN_ON_BASE(characterization): the base leaves the deps of a render a suspend abandons unpromoted.
        // Promoting the insertion effects' deps in FiberEffects.RunTurnEnd unconditionally, with or without insertion
        // effects pending and whether or not the pass journal holds the render, reddens it: the abandoned render's turn
        // then promotes them, and the render that retries finds its insertion effect's deps unchanged.
        [Test]
        public void Given_AComponentWhoseInsertionEffectDepsChangeInARenderASuspendAbandons_When_TheReadResolves_Then_TheRetryRunsTheEffect()
        {
            // Arrange
            s_read = new VelvetTaskCompletionSource<string>();
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(RendersThenSuspends)));
            log.Setter<int>("Value").Invoke(1);
            log.Setter<bool>("Reads").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            s_read.TrySetResult("done");
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:0 ins:1"));
        }

        #endregion

        #region Waves that keep the layout commit's order

        [Component]
        private static VNode OpensAPortal()
        {
            var (open, _) = EventLog.UseState("Open", false);
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:P");
                return () => log.Add("lay-x:P");
            }, new object[] { open });
            return V.Div(children: new VNode[]
            {
                open ? V.Portal(s_portalTarget, children: new VNode[] { V.Div() }, key: "portal") : null,
            });
        }

        [Component]
        private static VNode SwapsARef()
        {
            var (swapped, _) = EventLog.UseState("Swapped", false);
            var first = EventLog.UseRefCallback<VisualElement>("R1");
            var second = EventLog.UseRefCallback<VisualElement>("R2");
            return V.Div(refCallback: swapped ? second : first);
        }

        [Component]
        private static VNode PortalThenRefSwap()
            => V.Fragment(V.Component(OpensAPortal, key: "p"), V.Component(SwapsARef, key: "s"));

        // GREEN_ON_BASE(characterization): the base runs this layout cleanup and this ref detach in the layout commit.
        // Deleting the RunsTurnEnds check from the RefDetach arm of ChildListWrites.ApplyOp reddens it: the ref then
        // detaches as the second pass applies, ahead of the first component's cleanup.
        [Test]
        public void Given_AComponentFirstMountingAPortalAheadOfOneReplacingARef_When_OneDrainRendersBoth_Then_TheFirstsLayoutCleanupRunsBeforeTheRefDetaches()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(PortalThenRefSwap)));
            log.Clear();

            // Act
            log.Setter<bool>("Swapped").Invoke(true);
            log.Setter<bool>("Open").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay-x:P ref-x:R1 lay:P ref:R2"));
        }

        [Component]
        private static VNode LaysOutAfter()
        {
            var (tick, _) = EventLog.UseState("After", 0);
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:S");
                return () => log.Add("lay-x:S");
            }, new object[] { tick });
            return V.Div();
        }

        [Component]
        private static VNode PortalThenLayout()
            => V.Fragment(V.Component(OpensAPortal, key: "p"), V.Component(LaysOutAfter, key: "s"));

        // GREEN_ON_BASE(characterization): the base runs both layout cleanups in the layout commit, the first's first.
        // Deleting the RunsTurnEnds check from the FiberEnd arm of ChildListWrites.ApplyOp reddens it: the later
        // component's cleanup then runs as the second pass applies, ahead of the first's.
        [Test]
        public void Given_AComponentFirstMountingAPortalAheadOfOneWhoseLayoutEffectChanges_When_OneDrainRendersBoth_Then_TheFirstsLayoutCleanupRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(PortalThenLayout)));
            log.Clear();

            // Act
            log.Setter<int>("After").Invoke(1);
            log.Setter<bool>("Open").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay-x:P lay-x:S lay:P lay:S"));
        }

        [Component]
        private static VNode PortalChildInserts()
        {
            var log = EventLog.UseLog();
            Hooks.UseInsertionEffect(() =>
            {
                log.Add("ins:PC");
                return () => log.Add("ins-x:PC");
            }, Array.Empty<object>());
            return V.Div();
        }

        [Component]
        private static VNode InsertsBehindThePortal(bool open)
        {
            var log = EventLog.UseLog();
            Hooks.UseInsertionEffect(() =>
            {
                log.Add("ins:L");
                return () => log.Add("ins-x:L");
            }, new object[] { open });
            return V.Div();
        }

        // The Portal mounts in the render that updates the component behind it, so that render's own pass is the one the
        // Portal's mount ends.
        [Component]
        private static VNode PortalThenALaterChild()
        {
            var (open, _) = EventLog.UseState("Open", false);
            return V.Div(children: new VNode[]
            {
                open
                    ? V.Portal(s_portalTarget, children: new VNode[] { V.Component(PortalChildInserts, key: "pc") }, key: "portal")
                    : null,
                V.Component(InsertsBehindThePortal, open, key: "l"),
            });
        }

        // GREEN_ON_BASE(characterization): the base runs a Portal child's insertion effect at the Portal.
        // Leaving ReconcilePass.RunsTurnEnds set where ChildListWrites.Apply ends reddens it: the pass the Portal's mount
        // ends then runs the later component's turn as it applies, after the mount's own pass has run its turns.
        [Test]
        public void Given_AComponentWhoseRenderFirstMountsAPortalAheadOfAnUpdatedChild_When_ADrainRendersIt_Then_ThePortalsChildsInsertionEffectRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(PortalThenALaterChild)));
            log.Clear();

            // Act
            log.Setter<bool>("Open").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins:PC ins-x:L ins:L"));
        }

        // Unwoven, so a write to a component renders it rather than reusing its last output.
        [Component(Compiler = false)]
        private static VNode OwedStarter()
        {
            var (_, start) = Hooks.UseTransition();
            s_start = start;
            return V.Label(text: "starter");
        }

        // A layout effect with no dependencies, so each of its renders leaves a cleanup to run.
        [Component(Compiler = false)]
        private static VNode OwedHolder()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() => () => s_log.Add("holder-x")));
            var (state, addOptimistic) = Hooks.UseOptimistic<string, string>(
                "base",
                (current, action) => current + "+" + action);
            s_add = addOptimistic;
            return V.Label(text: state);
        }

        [Component(Compiler = false)]
        private static VNode OwedSibling()
        {
            s_sibling = FiberAmbientStack.Current;
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseLayoutEffect((Func<Action>)(() => () => s_log.Add("sibling-x")));
            return V.Label(text: "sibling:" + tick);
        }

        [Component(Compiler = false)]
        private static VNode OwedRoot()
            => V.Div(children: new VNode[]
            {
                V.Component(OwedStarter, key: "starter"),
                V.Component(OwedHolder, key: "holder"),
                V.Component(OwedSibling, key: "sibling"),
            });

        // GREEN_ON_BASE(characterization): the base commits a render a flush's settle owes with the flush, in tree order.
        // Deleting the FiberWave.KeepBatchOrder call FiberWorkLoop.FlushOwedSettleRenders makes reddens it: the flushed
        // component's turn then ends ahead of the holder's, which sits before it.
        [Test]
        public void Given_AFlushWhoseSettledTransitionOwesAComponentBeforeItARender_When_BothCommit_Then_TheEarlierComponentsLayoutCleanupRunsFirst()
        {
            // Arrange — the transition's write is on the sibling, so the flush that lands it settles the transition and
            // renders the holder, which sits before it
            using var mounted = V.Mount(_root, V.Component(OwedRoot, key: "root"));
            var sibling = s_sibling;
            s_start.Invoke(() =>
            {
                s_add.Invoke("sent");
                s_setTick.Invoke(1);
            });
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_log.Clear();

            // Act
            FiberWorkLoop.FlushState(sibling);

            // Assert
            Assert.That(string.Join(" ", s_log), Is.EqualTo("holder-x sibling-x"));
        }

        #endregion

        #region A reveal

        // Suspends on a read that never resolves while blocked, and reads at once otherwise.
        [Component]
        private static VNode BlockingReader(int blocked)
        {
            var value = Hooks.Use<int>(_ => blocked > 0
                ? new VelvetTaskCompletionSource<int>().Task
                : VelvetTask.FromResult(0), blocked);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode UpdatedAheadOfTheSuspense(int blocked)
        {
            EventLog.UseInsertionAndLayoutEffects("U", new object[] { blocked });
            return V.Div();
        }

        [Component]
        private static VNode HiddenWithNothingHeld()
        {
            EventLog.UseInsertionAndLayoutEffects("H", Array.Empty<object>());
            return V.Div();
        }

        [Component]
        private static VNode FallbackWithCleanups()
        {
            EventLog.UseInsertionAndLayoutEffects("F", Array.Empty<object>());
            return V.Div();
        }

        // One state updates the component and reveals the Suspense, so React commits both in one commit.
        [Component]
        private static VNode UpdatedThenARevealedSuspense()
        {
            var (blocked, _) = EventLog.UseState("Blocked", 0);
            return V.Div(children: new VNode[]
            {
                V.Component(UpdatedAheadOfTheSuspense, blocked, key: "u"),
                V.Suspense(fallback: V.Component(FallbackWithCleanups, key: "f"), children: new VNode[]
                {
                    V.Component(HiddenWithNothingHeld, key: "h"),
                    V.Component(BlockingReader, blocked, key: "r"),
                }),
            });
        }

        [Test]
        public void Given_AComponentUpdatedAheadOfASuspense_When_OneUpdateChangesItAndRevealsTheSuspenseRemovingItsFallback_Then_TheUpdatedComponentsCleanupsRunFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(UpdatedThenARevealedSuspense)),
                new MountOptions { OnCaughtError = (_, _) => { } });
            log.Setter<int>("Blocked").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            log.Setter<int>("Blocked").Invoke(0);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:U ins:U lay-x:U ins-x:F lay-x:F lay:U lay:H"));
        }

        #endregion
    }
}
