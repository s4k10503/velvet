using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for a reconciler bug: an
    /// ancestor component and a keyed descendant component both subscribe to the same <see cref="Store{TState}"/>,
    /// so one store update marks both dirty. The ancestor flushes first and re-expands the keyed child via
    /// <c>RenderInlineForExpansion</c>. That nested render returned the child's OLD VNode tree to the pool
    /// mid-pass, while the ancestor's reconcile still held it as the patch baseline; a later sibling render in
    /// the same pass could rent and mutate those pooled nodes, emptying the baseline's children so
    /// <c>PatchNode</c> re-inserted the child's whole subtree instead of patching it — the subtree visibly
    /// duplicated (e.g. tapping a list item's "+" restacked its subtree). The reconciler walks top-down in one
    /// pass and patches the keyed child in place; these tests assert Velvet does too.
    /// <para>
    /// The fix defers the pooled-object return of re-expanded inline children to the top-level reconcile
    /// boundary (see <c>ReconcilerContext.DeferredInlineOldTreeReturns</c>), so the baseline stays intact for
    /// the whole pass while pooling is preserved (no added GC pressure).
    /// </para>
    /// <para>
    /// Each scenario below drives the store to one state through a shared private factory and then asserts
    /// exactly one invariant per test, so a failure names precisely which subtree stacked.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class AncestorDescendantStoreRerenderTests
    {
        private VisualElement _root;
        private AppStore _store;
        private MountedTree _mounted;

        private readonly record struct AppState(bool Busy, string Message);

        private sealed class AppStore : Store<AppState>
        {
            public AppStore() : base(new AppState(false, null)) { }
            public void Apply(Func<AppState, AppState> u) => SetState(u);
            protected override void ResetCore() => SetState(_ => new AppState(false, null));
        }

        private static AppStore s_store;

        private static readonly (string Label, bool Locked)[] s_menu =
        {
            ("a", false), ("b", true), ("c", false), ("d", true), ("e", false),
        };

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _store = new AppStore();
            s_store = _store;
            _mounted = V.Mount(_root, V.Component(App, key: "app"));
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _store?.Dispose();
            _mounted = null;
            _store = null;
            s_store = null;
        }

        private static FiberBatchScheduler Scheduler(MountedTree mounted)
            => mounted.Root.Reconciler.Context.BatchScheduler;

        private static int CountByName(VisualElement root, string name)
        {
            var n = root.name == name ? 1 : 0;
            for (var i = 0; i < root.childCount; i++) n += CountByName(root[i], name);
            return n;
        }

        #region scenario factories (shared Arrange; no inline duplication)

        // One store update dirties the ancestor (Busy) and the keyed child (whole state); the ancestor
        // flushes first and re-expands the child. Flushed by a frame drain.
        private void DriveBusyTrueViaFrameDrain()
        {
            _store.Apply(s => s with { Busy = true });
            Scheduler(_mounted).DrainImmediateForTest();
        }

        // A typical async loading/result flow: Busy true -> Busy false + result Message -> Message cleared.
        // The conditional siblings go element -> empty fragment and back. Leaves the store at the final state
        // (Busy=false, Message=null) so the toggle round-trip is exercised before any assert.
        private void DriveFullBusyMessageCycle()
        {
            _store.Apply(s => s with { Busy = true });
            Scheduler(_mounted).DrainImmediateForTest();
            _store.Apply(s => s with { Busy = false, Message = "+1200" });
            Scheduler(_mounted).DrainImmediateForTest();
            _store.Apply(s => s with { Message = null });
            Scheduler(_mounted).DrainImmediateForTest();
        }

        // The intermediate state of the cycle: Busy cleared and the result Message set. Asserting here proves
        // the element -> empty fragment (overlay) and empty fragment -> element (toast) transitions in one pass.
        private void DriveBusyClearedWithMessage()
        {
            _store.Apply(s => s with { Busy = true });
            Scheduler(_mounted).DrainImmediateForTest();
            _store.Apply(s => s with { Busy = false, Message = "+1200" });
            Scheduler(_mounted).DrainImmediateForTest();
        }

        // Mirror the click handler: the first toggle takes the Urgent lane inside a discrete event and is
        // flushed synchronously when the handler returns (FlushImmediate), not by a frame drain.
        private void DriveBusyTrueInDiscreteEvent()
        {
            var scheduler = Scheduler(_mounted);
            FiberWorkLoop.IsInDiscreteEvent = true;
            try
            {
                _store.Apply(s => s with { Busy = true });
                scheduler.FlushImmediate();
            }
            finally
            {
                FiberWorkLoop.IsInDiscreteEvent = false;
            }
        }

        // Repeat the whole click -> result cycle the way a user tapping a button repeatedly would:
        // each Busy=true on the Urgent lane inside its own discrete event, then the async completion and
        // toast-clear on the Normal lane.
        private void DriveRepeatedClickResultCycles()
        {
            var scheduler = Scheduler(_mounted);
            for (var i = 0; i < 4; i++)
            {
                FiberWorkLoop.IsInDiscreteEvent = true;
                try
                {
                    _store.Apply(s => s with { Busy = true });
                    scheduler.FlushImmediate();
                }
                finally
                {
                    FiberWorkLoop.IsInDiscreteEvent = false;
                }
                _store.Apply(s => s with { Busy = false, Message = "+1200" });
                scheduler.DrainImmediateForTest();
                _store.Apply(s => s with { Message = null });
                scheduler.DrainImmediateForTest();
            }
        }

        #endregion

        // The keyed descendant. Its subtree mirrors a realistic screen: a dynamic-className element, a
        // standalone empty-fragment conditional, and a keyed V.List whose items carry their own nested
        // empty-fragment conditionals. This shape is what the ancestor re-expands; its old VNode tree is the
        // patch baseline that the bug corrupted.
        [Component]
        private static VNode Screen(Action<string> nav)
        {
            var st = Hooks.UseStore(s_store, s => s); // whole-state selector: re-renders on every update
            var pct = st.Busy ? 40 : 70;
            return V.Div(name: "screen", children: new VNode[]
            {
                V.Label(name: "screen-title", text: "SCREEN"),
                V.Div(name: "bar", className: "h-[9px] w-[" + pct + "%]", children: Array.Empty<VNode>()),
                string.IsNullOrEmpty(st.Message)
                    ? (VNode)V.Fragment(Array.Empty<VNode>())
                    : V.Label(name: "badge", text: "!"),
                V.Div(name: "menu", children: V.List(s_menu, m => m.Label, m =>
                    V.Button(name: "item-" + m.Label, className: "menu-item", children: new VNode[]
                    {
                        V.Label(name: "item-label-" + m.Label, text: m.Label),
                        m.Locked
                            ? (VNode)V.Label(name: "lock-" + m.Label, text: "lock")
                            : V.Fragment(Array.Empty<VNode>()),
                    }))),
            });
        }

        // The ancestor. Subscribes to the SAME store as the screen and renders, after the keyed screen,
        // conditional siblings as `cond ? element : V.Fragment(Array.Empty)` — empty fragments when off, exactly
        // as a loading overlay / result toast would.
        [Component]
        private static VNode App()
        {
            var busy = Hooks.UseStore(s_store, s => s.Busy);
            var message = Hooks.UseStore(s_store, s => s.Message);
            var (screen, setScreen) = Hooks.UseState("home");
            void Nav(string s) => setScreen.Invoke(s);
            return V.Div(name: "app", children: new VNode[]
            {
                screen == "shop"
                    ? V.Component<Action<string>>(Screen, Nav, key: "shop")
                    : V.Component<Action<string>>(Screen, Nav, key: "home"),
                busy
                    ? (VNode)V.Div(name: "overlay", children: new VNode[] { V.Label(name: "overlay-label", text: "busy") })
                    : V.Fragment(Array.Empty<VNode>()),
                string.IsNullOrEmpty(message)
                    ? (VNode)V.Fragment(Array.Empty<VNode>())
                    : V.Div(name: "toast", children: new VNode[] { V.Label(name: "toast-label", text: message) }),
            });
        }

        [Test]
        public void Given_FreshMount_When_NothingHappens_Then_ExactlyOneScreenExists()
        {
            // Arrange — SetUp mounts the tree.
            // Act — none.
            // Assert
            Assert.AreEqual(1, CountByName(_root, "screen"), "one screen after mount");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_StoreUpdatesViaFrameDrain_Then_ScreenDoesNotStack()
        {
            // Arrange
            Assume.That(CountByName(_root, "screen"), Is.EqualTo(1), "Precondition: one screen after mount");
            // Act
            DriveBusyTrueViaFrameDrain();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "screen"), "the keyed screen must not stack");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_StoreUpdatesViaFrameDrain_Then_ListContainerDoesNotDuplicate()
        {
            // Arrange
            Assume.That(CountByName(_root, "menu"), Is.EqualTo(1), "Precondition: one menu after mount");
            // Act
            DriveBusyTrueViaFrameDrain();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "menu"), "the screen's V.List container must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_StoreUpdatesViaFrameDrain_Then_DynamicElementDoesNotDuplicate()
        {
            // Arrange
            Assume.That(CountByName(_root, "bar"), Is.EqualTo(1), "Precondition: one bar after mount");
            // Act
            DriveBusyTrueViaFrameDrain();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "bar"), "the screen's dynamic element must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_StoreUpdatesViaFrameDrain_Then_NestedEmptyFragmentListItemDoesNotDuplicate()
        {
            // Arrange
            Assume.That(CountByName(_root, "lock-b"), Is.EqualTo(1), "Precondition: one lock-b after mount");
            // Act
            DriveBusyTrueViaFrameDrain();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "lock-b"), "a nested empty-fragment list item must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_BusyTrueViaFrameDrain_Then_ExactlyOneOverlayShows()
        {
            // Arrange
            Assume.That(CountByName(_root, "overlay"), Is.EqualTo(0), "Precondition: no overlay after mount");
            // Act
            DriveBusyTrueViaFrameDrain();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "overlay"), "Busy=true shows exactly one overlay");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_BusyClearedWithMessage_Then_ScreenDoesNotStack()
        {
            // Arrange / Act
            DriveBusyClearedWithMessage();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "screen"), "the keyed screen must not stack");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_BusyClearedWithMessage_Then_ListContainerDoesNotDuplicate()
        {
            // Arrange / Act
            DriveBusyClearedWithMessage();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "menu"), "the screen's V.List container must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_BusyClearedWithMessage_Then_DynamicElementDoesNotDuplicate()
        {
            // Arrange / Act
            DriveBusyClearedWithMessage();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "bar"), "the screen's dynamic element must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_BusyClearedWithMessage_Then_NestedEmptyFragmentListItemDoesNotDuplicate()
        {
            // Arrange / Act
            DriveBusyClearedWithMessage();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "lock-b"), "a nested empty-fragment list item must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_BusyClearedWithMessage_Then_OverlayIsRemoved()
        {
            // Arrange / Act
            DriveBusyClearedWithMessage();
            // Assert
            Assert.AreEqual(0, CountByName(_root, "overlay"), "element -> empty fragment must remove the overlay");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_BusyClearedWithMessage_Then_ResultToastAppearsOnce()
        {
            // Arrange / Act
            DriveBusyClearedWithMessage();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "toast"), "the result toast appears exactly once");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_FullBusyMessageCycleCompletes_Then_ScreenDoesNotStack()
        {
            // Arrange / Act
            DriveFullBusyMessageCycle();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "screen"), "the keyed screen must not stack");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_FullBusyMessageCycleCompletes_Then_ListContainerDoesNotDuplicate()
        {
            // Arrange / Act
            DriveFullBusyMessageCycle();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "menu"), "the screen's V.List container must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_FullBusyMessageCycleCompletes_Then_DynamicElementDoesNotDuplicate()
        {
            // Arrange / Act
            DriveFullBusyMessageCycle();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "bar"), "the screen's dynamic element must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_FullBusyMessageCycleCompletes_Then_NestedEmptyFragmentListItemDoesNotDuplicate()
        {
            // Arrange / Act
            DriveFullBusyMessageCycle();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "lock-b"), "a nested empty-fragment list item must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_MessageCleared_Then_ToastIsRemoved()
        {
            // Arrange / Act
            DriveFullBusyMessageCycle();
            // Assert
            Assert.AreEqual(0, CountByName(_root, "toast"), "clearing Message must remove the toast");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_FirstToggleRunsInADiscreteEvent_Then_ScreenDoesNotStack()
        {
            // Arrange / Act
            DriveBusyTrueInDiscreteEvent();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "screen"), "the keyed screen must not stack");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_FirstToggleRunsInADiscreteEvent_Then_ListContainerDoesNotDuplicate()
        {
            // Arrange / Act
            DriveBusyTrueInDiscreteEvent();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "menu"), "the screen's V.List container must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_FirstToggleRunsInADiscreteEvent_Then_DynamicElementDoesNotDuplicate()
        {
            // Arrange / Act
            DriveBusyTrueInDiscreteEvent();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "bar"), "the screen's dynamic element must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_FirstToggleRunsInADiscreteEvent_Then_NestedEmptyFragmentListItemDoesNotDuplicate()
        {
            // Arrange / Act
            DriveBusyTrueInDiscreteEvent();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "lock-b"), "a nested empty-fragment list item must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_TheClickResultCycleRepeats_Then_ScreenDoesNotStack()
        {
            // Arrange / Act
            DriveRepeatedClickResultCycles();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "screen"), "the keyed screen must not stack");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_TheClickResultCycleRepeats_Then_ListContainerDoesNotDuplicate()
        {
            // Arrange / Act
            DriveRepeatedClickResultCycles();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "menu"), "the screen's V.List container must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_TheClickResultCycleRepeats_Then_DynamicElementDoesNotDuplicate()
        {
            // Arrange / Act
            DriveRepeatedClickResultCycles();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "bar"), "the screen's dynamic element must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_TheClickResultCycleRepeats_Then_NestedEmptyFragmentListItemDoesNotDuplicate()
        {
            // Arrange / Act
            DriveRepeatedClickResultCycles();
            // Assert
            Assert.AreEqual(1, CountByName(_root, "lock-b"), "a nested empty-fragment list item must not duplicate");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_TheClickResultCycleRepeats_Then_NoResidualOverlayStacks()
        {
            // Arrange / Act
            DriveRepeatedClickResultCycles();
            // Assert
            Assert.AreEqual(0, CountByName(_root, "overlay"), "no residual overlays may stack");
        }

        [Test]
        public void Given_AncestorReExpandsKeyedChild_When_TheClickResultCycleRepeats_Then_NoResidualToastStacks()
        {
            // Arrange / Act
            DriveRepeatedClickResultCycles();
            // Assert
            Assert.AreEqual(0, CountByName(_root, "toast"), "no residual toasts may stack");
        }
    }

    /// <summary>
    /// Specifies the ancestor↔descendant flush-coalescing contract: when a parent and one
    /// of its inline children are both dirtied within a single batch — in any enqueue order — each component
    /// renders exactly once in the single drain pass, and a higher-priority lane queued on the child is
    /// honored rather than dropped.
    /// <list type="bullet">
    /// <item>A child dirtied BEFORE its parent must not render twice. The parent's re-expansion of the
    /// child subsumes the child's own pending flush; the child renders once, not once for its own slot and
    /// again when the parent re-expands it.</item>
    /// <item>When the parent's re-expansion subsumes a still-dirty child, the child is removed from the
    /// batch scheduler's pending queue and its lane queue is settled, so a higher-priority lane queued on the
    /// child (e.g. Transition on the delayed tier) is not stranded and later dropped by FlushState's
    /// not-dirty early-return.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class FlushCoalescingParityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            FiberWorkLoop.IsInDiscreteEvent = false;
            _root = new VisualElement();
            ResetAll();
        }

        private static FiberBatchScheduler Scheduler(MountedTree mounted)
            => mounted.Root.Reconciler.Context.BatchScheduler;

        [Test]
        public void Given_ChildDirtiedBeforeParent_When_BatchDrained_Then_ChildRendersExactlyOnce()
        {
            // Arrange — child setState enqueued BEFORE the parent setState in one handler (repro).
            using var mounted = MountParentChild();
            Assume.That((s_renderCountParent, s_renderCountChild), Is.EqualTo((1, 1)),
                "Precondition: parent and child each rendered once on mount");
            s_setChild.Invoke("c-updated");
            s_setParent.Invoke("p-updated");

            // Act — single coalesced drain.
            Scheduler(mounted).DrainImmediateForTest();

            // Assert — exactly one re-render each (count == 2 = mount + one batch render). The bug renders
            // the child twice (its own isolated slot flush + the parent's re-expansion), giving 3.
            Assert.AreEqual((2, 2), (s_renderCountParent, s_renderCountChild),
                "Parent and child each render exactly once in the single coalesced pass, regardless of enqueue order");
        }

        [Test]
        public void Given_ChildDirtiedBeforeParent_When_BatchDrained_Then_PendingSetEmpty()
        {
            // Arrange
            using var mounted = MountParentChild();
            s_setChild.Invoke("c-updated");
            s_setParent.Invoke("p-updated");
            Assume.That(Scheduler(mounted).ImmediatePendingCount, Is.EqualTo(2),
                "Precondition: both fibers pending on the immediate tier");

            // Act
            Scheduler(mounted).DrainImmediateForTest();

            // Assert
            Assert.AreEqual(0, Scheduler(mounted).ImmediatePendingCount,
                "The whole batch is drained — no fiber lingers in the pending set after the coalesced pass");
        }

        [Test]
        public void Given_ChildWithTransitionLane_When_ParentNormalFlushSubsumesChild_Then_ChildRemovedFromDelayedTier()
        {
            // Arrange — child enrolled on the delayed (Transition) tier, parent on the immediate (Normal) tier.
            // The immediate drain flushes the parent, whose re-expansion renders the still-dirty child. The
            // child's Transition enrollment on the delayed tier and its lane queue must be settled by that
            // subsumption rather than stranded (repro).
            using var mounted = MountParentChild();
            s_childFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_parentFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            var scheduler = Scheduler(mounted);
            Assume.That((scheduler.ImmediatePendingCount, scheduler.DelayedPendingCount), Is.EqualTo((1, 1)),
                "Precondition: parent on the immediate tier, child on the delayed tier");

            // Act
            scheduler.DrainImmediateForTest();

            // Assert — the parent's re-expansion subsumed the child; it must not still be pending on the
            // delayed tier with a stranded lane that the next delayed drain would silently drop.
            Assert.AreEqual(0, scheduler.DelayedPendingCount,
                "The subsumed child is removed from the delayed tier (its lane is honored by the re-expansion, not dropped)");
            Assert.IsFalse(s_childFiber.IsDirty,
                "The subsumed child's dirty flag is cleared by the re-expansion");
            Assert.IsTrue(s_childFiber.LaneQueue == null || s_childFiber.LaneQueue.Count == 0,
                "The subsumed child's lane queue is settled, not left holding a stranded Transition lane");
        }

        [Test]
        public void Given_ChildWithTransitionLane_When_ParentSubsumesChild_Then_ChildRendersExactlyOnce()
        {
            // Arrange
            using var mounted = MountParentChild();
            s_childFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_parentFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);

            // Act — immediate drain (parent) then delayed drain (would re-flush a stranded child).
            Scheduler(mounted).DrainImmediateForTest();
            Scheduler(mounted).DrainDelayedForTest();

            // Assert — exactly one re-render of the child across both drains.
            Assert.AreEqual(2, s_renderCountChild,
                "The child renders exactly once (via the parent's re-expansion); the delayed drain finds nothing stranded");
        }

        private MountedTree MountParentChild()
        {
            var mounted = V.Mount(_root, V.Component(RenderParent, key: "parent"));
            s_parentFiber = mounted.Root.Child;
            s_childFiber = s_parentFiber?.Child;
            return mounted;
        }

        private static int s_renderCountParent;
        private static int s_renderCountChild;
        private static Action<string> s_setParent;
        private static Action<string> s_setChild;
        private static ComponentFiber s_parentFiber;
        private static ComponentFiber s_childFiber;

        private static void ResetAll()
        {
            s_renderCountParent = 0;
            s_renderCountChild = 0;
            s_setParent = null;
            s_setChild = null;
            s_parentFiber = null;
            s_childFiber = null;
        }

        // Compiler = false: these fixtures exercise the batch scheduler's lane coalescing / subsumption path,
        // which requires the parent to re-expand (re-render its children) on every flush. Auto-memo would let a
        // force-dirtied-but-unchanged parent memo-hit and skip re-expansion, so the subsumption under test would
        // not run. Opting out keeps these scheduler tests independent of the memoization axis.
        [Component(Compiler = false)]
        private static VNode RenderParent()
        {
            s_renderCountParent++;
            var (value, setValue) = Hooks.UseState("p");
            s_setParent = setValue;
            return V.Div(name: "parent", children: new VNode[]
            {
                V.Label(text: value),
                V.Component(RenderChild, key: "child"),
            });
        }

        [Component(Compiler = false)]
        private static VNode RenderChild()
        {
            s_renderCountChild++;
            var (value, setValue) = Hooks.UseState("c");
            s_setChild = setValue;
            return V.Label(name: "child", text: value);
        }
    }
}
