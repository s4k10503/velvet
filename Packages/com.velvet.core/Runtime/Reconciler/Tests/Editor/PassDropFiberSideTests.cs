using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the journal a pass keeps, rolled back after the pass ran, restores what the pass changed on the
    /// fiber side. Each case mounts its tree, takes the snapshot, runs one shape of update under
    /// <see cref="PassDropHarness"/> and reads what differs from the snapshot once the journal is rolled back. Each
    /// case covers the slot, tree and boundary families together, and checks that the journal restores what the pass
    /// changed; it does not show that a drop at <c>FinishPass</c> is sound.
    /// </summary>
    [TestFixture]
    internal sealed class PassDropFiberSideTests
    {
        private VisualElement _root;
        private static Action<int> s_setRows;
        private static Action<int> s_setOrder;
        private static Action<int> s_setWhich;
        private static Action<int> s_setKey;
        private static Action<int> s_setTick;
        private static VelvetTaskCompletionSource<int> s_hideSource;
        private static VelvetTaskCompletionSource<string> s_revealSource;
        private static bool s_throws;
        private static VisualElement s_portalTarget;
        private static int s_parkedRows;
        private static ComponentFiber s_parkedFiber;
        private static SetStore s_store;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setRows = null;
            s_setOrder = null;
            s_setWhich = null;
            s_setKey = null;
            s_setTick = null;
            s_hideSource = null;
            s_revealSource = new VelvetTaskCompletionSource<string>();
            s_throws = false;
            s_portalTarget = new VisualElement();
            s_parkedRows = 1;
            s_parkedFiber = null;
            s_store = null;
        }

        private static VNode Rows(string prefix, int count)
        {
            var rows = new VNode[count];
            for (var i = 0; i < count; i++) rows[i] = V.Label(name: prefix + i, key: prefix + i);
            return V.Fragment(children: rows);
        }

        [Component]
        private static VNode TailRender() => V.Label(name: "tail", key: "tail");

        #region A component's own update

        [Component]
        private static VNode GrowingRender()
        {
            var (rows, setRows) = Hooks.UseState(1);
            s_setRows = setRows;
            return Rows("grow", rows);
        }

        [Component]
        private static VNode GrowingHostRender()
            => V.Div(name: "box", children: new VNode[]
            {
                V.Component(GrowingRender, key: "grow"),
                V.Component(TailRender, key: "tail"),
            });

        [Test]
        public void Given_AComponentThatGrowsAheadOfAnother_When_ItsOwnUpdatePassIsDropped_Then_NoSlotDiffersFromBefore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(GrowingHostRender, key: "host"));
            var harness = PassDropHarness.Open(mounted);

            // Act
            s_setRows.Invoke(3);
            mounted.FlushStateForTest();
            var differences = harness.Drop();

            // Assert
            Assert.That(differences, Is.Empty);
        }

        #endregion

        #region A keyed reorder

        [Component]
        private static VNode FirstRender() => V.Label(name: "first");

        [Component]
        private static VNode SecondRender() => V.Label(name: "second");

        [Component]
        private static VNode ReorderHostRender()
        {
            var (order, setOrder) = Hooks.UseState(0);
            s_setOrder = setOrder;
            var first = V.Component(FirstRender, key: "first");
            var second = V.Component(SecondRender, key: "second");
            return V.Div(name: "box", children: order == 0 ? new VNode[] { first, second } : new VNode[] { second, first });
        }

        [Test]
        public void Given_TwoKeyedComponentsOnOneContainer_When_ThePassThatSwapsThemIsDropped_Then_NoSlotDiffersFromBefore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReorderHostRender, key: "host"));
            var harness = PassDropHarness.Open(mounted);

            // Act
            s_setOrder.Invoke(1);
            mounted.FlushStateForTest();
            var differences = harness.Drop();

            // Assert
            Assert.That(differences, Is.Empty);
        }

        #endregion

        #region An insert and a remove

        [Component]
        private static VNode OneRowRender() => V.Label(name: "one");

        [Component]
        private static VNode ThreeRowsRender() => Rows("three", 3);

        [Component]
        private static VNode ReplacingHostRender()
        {
            var (which, setWhich) = Hooks.UseState(0);
            s_setWhich = setWhich;
            return V.Div(name: "box", children: new VNode[]
            {
                which == 0 ? V.Component(OneRowRender, key: "one") : V.Component(ThreeRowsRender, key: "three"),
                V.Component(TailRender, key: "tail"),
            });
        }

        [Test]
        public void Given_AComponentAheadOfAnotherReplacedByOneOfMoreRows_When_ThatPassIsDropped_Then_NoSlotDiffersFromBefore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReplacingHostRender, key: "host"));
            var harness = PassDropHarness.Open(mounted);

            // Act
            s_setWhich.Invoke(1);
            mounted.FlushStateForTest();
            var differences = harness.Drop();

            // Assert
            Assert.That(differences, Is.Empty);
        }

        #endregion

        #region A Suspense hiding and revealing

        [Component(Compiler = false)]
        private static VNode WaitingReaderRender()
        {
            var (key, setKey) = Hooks.UseState(0);
            s_setKey = setKey;
            var value = Hooks.Use<int>(_ => key == 0
                ? VelvetTask.FromResult(0)
                : (s_hideSource = new VelvetTaskCompletionSource<int>()).Task, key);
            return V.Label(name: "reader", text: "value:" + value);
        }

        [Component]
        private static VNode HidingHostRender()
            => V.Suspense(V.Label(name: "loading", text: "loading"), new VNode[]
            {
                V.Div(name: "list", children: new VNode[] { V.Component(WaitingReaderRender, key: "reader") }),
            });

        [Test]
        public void Given_APrimaryWhoseReaderStartsWaiting_When_ThePassThatHidesItIsDropped_Then_NoFiberStateDiffersFromBefore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HidingHostRender, key: "host"));
            var harness = PassDropHarness.Open(mounted);

            // Act
            s_setKey.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();
            var differences = harness.Drop();

            // Assert
            Assert.That(differences, Is.Empty);
        }

        private static VelvetTask<string> ReadRevealed(CancellationToken _) => s_revealSource.Task;

        [Component]
        private static VNode RevealedChildRender()
        {
            var value = Hooks.Use<string>(ReadRevealed, "revealed");
            return V.Label(name: "content", text: value);
        }

        [Component]
        private static VNode RevealingHostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Component(RevealedChildRender, key: "child") });

        [Test]
        public void Given_AFallbackShownForAWaitingChild_When_ThePassThatRevealsTheChildIsDropped_Then_NoFiberStateDiffersFromBefore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RevealingHostRender, key: "host"));
            var harness = PassDropHarness.Open(mounted);

            // Act
            s_revealSource.TrySetResult("ready");
            mounted.FlushStateForTest();
            var differences = harness.Drop();

            // Assert
            Assert.That(differences, Is.Empty);
        }

        #endregion

        #region A Portal mount

        [Component]
        private static VNode PortalDeclarerRender()
            => V.Portal(s_portalTarget, children: new VNode[] { V.Label(name: "portaled", key: "portaled") });

        [Component]
        private static VNode PortalHostRender()
        {
            var (shown, setShown) = Hooks.UseState(0);
            s_setWhich = setShown;
            return V.Fragment(children: new VNode[]
            {
                shown == 1 ? V.Component(PortalDeclarerRender, key: "portal") : null,
                V.Component(TailRender, key: "tail"),
            });
        }

        [Test]
        public void Given_AComponentAheadOfAnother_When_ThePassThatMountsAPortalInItIsDropped_Then_NoFiberStateDiffersFromBefore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PortalHostRender, key: "host"));
            var harness = PassDropHarness.Open(mounted);

            // Act
            s_setWhich.Invoke(1);
            mounted.FlushStateForTest();
            var differences = harness.Drop();

            // Assert
            Assert.That(differences, Is.Empty);
        }

        #endregion

        #region An error boundary catching

        [Component]
        private static VNode ThrowingChildRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            if (s_throws) throw new InvalidOperationException("Test render error");
            return V.Label(name: "ok");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(name: "error"));
            return V.Component(ThrowingChildRender, key: "child");
        }

        [Test]
        public void Given_ABoundaryOverAChild_When_ThePassInWhichTheChildThrowsIsDropped_Then_NoFiberStateDiffersFromBefore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(BoundaryRender, key: "boundary"), CaughtErrors.Unlogged);
            var harness = PassDropHarness.Open(mounted);
            s_throws = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            var differences = harness.Drop();

            // Assert
            Assert.That(differences, Is.Empty);
        }

        #endregion

        #region A time-sliced park and resume

        [Component(Compiler = false)]
        private static VNode ParkedRowsRender()
        {
            s_parkedFiber = FiberAmbientStack.Current;
            return Rows("g", s_parkedRows);
        }

        [Component]
        private static VNode ParkingHostRender()
            => V.Div(name: "box", children: new VNode[]
            {
                V.Component(ParkedRowsRender, key: "g"),
                V.Component(TailRender, key: "tail"),
            });

        [Test]
        public void Given_AComponentWhoseGrowthParksOnTheTransitionLane_When_ThePassesThatPlaceItsRowsAreDropped_Then_NoFiberStateDiffersFromBefore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ParkingHostRender, key: "host"));
            var harness = PassDropHarness.Open(mounted);

            // Act
            s_parkedRows = 20;
            s_parkedFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_parkedFiber.FlushStateWithTinyBudgetForTest();
            s_parkedFiber.DrainTimeSlicedReconcileForTest();
            var differences = harness.Drop();

            // Assert
            Assert.That(differences, Is.Empty);
        }

        #endregion

        #region A presence exit

        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore() : base(new SetState("abc")) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState("abc"));
        }

        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["visible"] = "opacity-100",
            ["hidden"] = "opacity-0",
        };

        [Component]
        private static VNode PresenceListRender()
        {
            var keys = Hooks.UseStore(s_store, state => state.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "item-" + key, key: key.ToString(),
                    variants: s_fade, animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }, enterOnMount: false));
            }
            return V.Div(name: "presence-host", children: new VNode[]
            {
                V.AnimatePresence(key: "presence", children: children.ToArray()),
            });
        }

        [Test]
        public void Given_ThreeKeyedChildrenUnderAPresence_When_ThePassThatStartsTheMiddleOnesExitIsDropped_Then_NoFiberStateDiffersFromBefore()
        {
            // Arrange
            using var store = new SetStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(PresenceListRender, key: "list"));
            var harness = PassDropHarness.Open(mounted);

            // Act
            store.Set("ac");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var differences = harness.Drop();

            // Assert
            Assert.That(differences, Is.Empty);
        }

        #endregion
    }
}
