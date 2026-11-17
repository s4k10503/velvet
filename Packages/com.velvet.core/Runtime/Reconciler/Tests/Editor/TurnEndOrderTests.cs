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
    /// detach of a ref it replaces: as its writes apply, at the component's or the element's turn among the teardowns of
    /// the components it removes, where react-reconciler 19.1's <c>commitMutationEffectsOnFiber</c> runs them in its one
    /// walk of the tree. The cases red on the base expect that function's order for their trees, read from its source
    /// rather than run. The rest hold what the layout commit already did: an error those
    /// cleanups throw reaches the boundary in a follow-up commit, a mounted component's insertion effect runs once more
    /// under StrictMode and an updated one's does not, the effect runs with the render's own closure, a component row's
    /// turn ends before its host's, a component a walk's boundary removes runs only its teardown, a render a suspend
    /// abandons promotes no deps, a component mounted under one a commit has taken finds its element placed, and a wave
    /// whose work the layout commit has to run keeps that commit's order.
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
        private static VelvetTaskCompletionSource<int> s_source;
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
            s_source = null;
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
        // turn, ahead of the insert that places its element, which React makes first for a component new under one its
        // commit has taken.
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

        [Component]
        private static VNode StrictInserts()
        {
            var (value, _) = EventLog.UseState("Strict", 0);
            var log = EventLog.UseLog();
            Hooks.UseInsertionEffect(() =>
            {
                log.Add("ins:S");
                return () => log.Add("ins-x:S");
            }, new object[] { value });
            return V.Div();
        }

        // New with the component it holds, so the mount runs that component's insertion effect at its turn.
        [Component]
        private static VNode HoldsStrictInserts() => V.Component(StrictInserts, key: "s");

        // GREEN_ON_BASE(characterization): the base runs a mount's insertion effect a second time under StrictMode.
        // Passing false for isMount in FiberEffects.RunTurnEnd reddens it.
        [Test]
        public void Given_StrictModeOn_When_AComponentWithAnInsertionEffectMounts_Then_TheEffectIsSetUpCleanedUpAndSetUpAgain()
        {
            // Arrange
            var log = new EventLog();
            FiberStrictMode.Enabled = true;

            // Act
            using var mounted = V.Mount(_root, log.Provide(V.Component(HoldsStrictInserts)));

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins:S ins-x:S ins:S"));
        }

        // GREEN_ON_BASE(characterization): the base runs an update's insertion effect once under StrictMode.
        // Passing true for isMount in FiberEffects.RunTurnEnd reddens it.
        [Test]
        public void Given_StrictModeOn_When_AComponentWithAnInsertionEffectUpdates_Then_TheEffectIsCleanedUpAndSetUpOnce()
        {
            // Arrange
            var log = new EventLog();
            FiberStrictMode.Enabled = true;
            using var mounted = V.Mount(_root, log.Provide(V.Component(HoldsStrictInserts)));
            log.Clear();

            // Act
            log.Setter<int>("Strict").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:S ins:S"));
        }

        #endregion

        #region A root below a root

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

        // GREEN_ON_BASE(characterization): the base runs a drain's cleanups in post-order, a row before its host.
        // Recording a root's turn end where its render returns, in FiberRenderer.EndOwnRowsTurn, reddens it: the host's
        // turn then ends ahead of the row's, which renders after it.
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
        // Returning true at the end of FiberPassJournal.Holds reddens it: the abandoned render's turn then promotes them,
        // and the render that retries finds its insertion effect's deps unchanged.
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

        // GREEN_ON_BASE(characterization): the base runs a drain's cleanups and ref detaches in tree order.
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

        // GREEN_ON_BASE(characterization): the base runs every cleanup of a drain in the layout commit, in tree order.
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

        [Component]
        private static VNode BlockingReader(int blocked)
        {
            var value = Hooks.Use<int>(_ => blocked > 0
                ? (s_source = new VelvetTaskCompletionSource<int>()).Task
                : VelvetTask.FromResult(0), blocked);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode InsertsWhileHidden(int tick)
        {
            var log = EventLog.UseLog();
            Hooks.UseInsertionEffect(() =>
            {
                log.Add("ins:H");
                return () => log.Add("ins-x:H");
            }, new object[] { tick });
            return V.Div();
        }

        // Handed a new object at every render of its parent, so each of those renders it and changes its effect's deps.
        [Component]
        private static VNode InsertsAfterTheSuspense(object renders)
        {
            var log = EventLog.UseLog();
            Hooks.UseInsertionEffect(() =>
            {
                log.Add("ins:L");
                return () => log.Add("ins-x:L");
            }, new object[] { renders });
            return V.Div();
        }

        [Component]
        private static VNode SuspenseThenALaterComponent()
        {
            var (blocked, _) = EventLog.UseState("Blocked", 0);
            var (tick, _) = EventLog.UseState("Tick", 0);
            return V.Div(children: new VNode[]
            {
                V.Suspense(fallback: V.Label(text: "loading"), children: new VNode[]
                {
                    V.Component(InsertsWhileHidden, tick, key: "h"),
                    V.Component(BlockingReader, blocked, key: "r"),
                }),
                V.Component(InsertsAfterTheSuspense, new object(), key: "l"),
            });
        }

        // GREEN_ON_BASE(characterization): the base runs a revealed component's held insertion effect in tree order.
        // Deleting the FiberWave.KeepBatchOrder call FiberEffects.ShowLayoutEffects makes reddens it: the later
        // component's insertion effect then runs as the reveal's writes apply, ahead of the revealed one's.
        [Test]
        public void Given_AComponentRenderedWhileASuspenseHidesIt_When_TheSuspenseRevealsItBesideALaterComponentThatRendersAgain_Then_TheRevealedComponentsInsertionEffectRunsFirst()
        {
            // Arrange — the hidden component renders again behind the fallback, which holds its insertion effect
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(SuspenseThenALaterComponent)),
                new MountOptions { OnCaughtError = (_, _) => { } });
            log.Setter<int>("Blocked").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Setter<int>("Tick").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            s_source.TrySetResult(5);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:H ins:H ins-x:L ins:L"));
        }

        // Handed a new object at every render of its parent, as InsertsAfterTheSuspense is, ahead of the Suspense.
        [Component]
        private static VNode UpdatedAheadOfTheSuspense(object renders)
        {
            EventLog.UseInsertionAndLayoutEffects("U", new object[] { renders });
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

        [Component]
        private static VNode UpdatedThenARevealedSuspense()
        {
            var (blocked, _) = EventLog.UseState("Blocked", 0);
            return V.Div(children: new VNode[]
            {
                V.Component(UpdatedAheadOfTheSuspense, new object(), key: "u"),
                V.Suspense(fallback: V.Component(FallbackWithCleanups, key: "f"), children: new VNode[]
                {
                    V.Component(HiddenWithNothingHeld, key: "h"),
                    V.Component(BlockingReader, blocked, key: "r"),
                }),
            });
        }

        [Test]
        public void Given_AComponentUpdatedAheadOfASuspenseThatHoldsNoInsertionEffect_When_TheSuspenseRevealsAndRemovesItsFallback_Then_TheUpdatedComponentsCleanupsRunFirst()
        {
            // Arrange — the hidden component does not render behind the fallback, so it holds no insertion effect
            var log = new EventLog();
            using var mounted = V.Mount(_root, log.Provide(V.Component(UpdatedThenARevealedSuspense)),
                new MountOptions { OnCaughtError = (_, _) => { } });
            log.Setter<int>("Blocked").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            s_source.TrySetResult(5);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:U ins:U lay-x:U ins-x:F lay-x:F lay:U lay:H"));
        }

        #endregion
    }
}
