using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseTransition"/> in a function component.
    /// <list type="bullet">
    /// <item>The hook returns a <see cref="TransitionStarter"/> and an <c>isPending</c> flag; <c>isPending</c> is
    /// false on the first render.</item>
    /// <item>State updates run inside <c>startTransition</c> are scheduled on the Transition lane and commit on the
    /// next flush, not synchronously during the call.</item>
    /// <item>The completion render after a transition flush always observes <c>isPending == false</c>.</item>
    /// <item>Setting a state to an equal value inside a transition schedules no re-render.</item>
    /// <item>A Normal-priority update may interrupt a pending transition, but <c>isPending</c> stays true while the
    /// transition lane remains queued and returns to false only after a subsequent flush drains that lane.</item>
    /// <item>An async <c>startTransition</c> keeps <c>isPending</c> true across awaits until the task completes,
    /// and its post-await updates still take the transition lane.</item>
    /// <item>A nested <c>startTransition</c> joins the outer transition: it applies its updates without starting a
    /// new transition and without throwing.</item>
    /// <item>Each <c>UseTransition</c> slot tracks its own pending flag independently of other slots in the same
    /// component, including a slot started while another slot's async transition is still awaiting.</item>
    /// <item>A discrete update on the same component keeps its urgent priority while an async transition is in
    /// flight there, unless the handler wrapped it in a <c>startTransition</c> of its own.</item>
    /// <item>Calling the hook outside a render throws an <see cref="InvalidOperationException"/>.</item>
    /// <item>A remounted fiber's slots are owned by nobody, even when the unmounted owner's task has not
    /// settled.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Per-component captures (render count, last <c>isPending</c>, the starter, the owning fiber) are exposed via
    /// static fields reset together in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseTransitionTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetTransition();
        }

        #region StartTransition scheduling

        [Test]
        public void Given_MountedComponent_When_StartTransitionCalled_Then_NoRenderBeforeFlush()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            Assume.That(s_transitionRenderCount, Is.EqualTo(1), "Precondition: only the mount render has happened");

            // Act
            s_transitionStart.Invoke(() => s_transitionSetValue.Invoke(1));

            // Assert
            Assert.AreEqual(1, s_transitionRenderCount, "A transition update does not render synchronously");
        }

        [Test]
        public void Given_StartedTransition_When_Flushed_Then_CommitRendersExactlyOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            s_transitionStart.Invoke(() => s_transitionSetValue.Invoke(1));

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(2, s_transitionRenderCount, "The transition update commits in a single render on flush");
        }

        [Test]
        public void Given_StartedTransition_When_Flushed_Then_CompletionRenderHasIsPendingFalse()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            s_transitionStart.Invoke(() => s_transitionSetValue.Invoke(1));

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.IsFalse(s_transitionLastIsPending, "The completion render observes isPending = false");
        }

        [Test]
        public void Given_MountedComponent_When_TransitionSetsEqualValue_Then_NoRerender()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));

            // Act
            s_transitionStart.Invoke(() => s_transitionSetValue.Invoke(0));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(1, s_transitionRenderCount, "Setting an equal value inside a transition schedules no re-render");
        }

        #endregion

        #region isPending lifecycle

        [Test]
        public void Given_TransitionComponent_When_FirstMounted_Then_IsPendingIsFalse()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));

            // Assert
            Assert.IsFalse(s_transitionLastIsPending, "isPending is false on the first render");
        }

        [Test]
        public void Given_PendingTransition_When_NormalUpdateInterrupts_Then_IsPendingStaysTrueUntilTransitionFlushed()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            s_transitionStart.Invoke(() => s_transitionSetValue.Invoke(1));
            s_transitionSetValue.Invoke(2); // Normal-priority update interrupts the transition

            // Act — the first flush drains the Normal lane; the transition lane remains queued
            mounted.FlushStateForTest();

            // Assert
            Assert.IsTrue(s_transitionLastIsPending, "isPending stays true while the transition lane remains queued after a Normal interruption");
        }

        [Test]
        public void Given_NormalInterruptedTransition_When_TransitionLaneFlushed_Then_IsPendingClears()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            s_transitionStart.Invoke(() => s_transitionSetValue.Invoke(1));
            s_transitionSetValue.Invoke(2); // Normal-priority update interrupts the transition
            mounted.FlushStateForTest(); // drains the Normal lane; transition lane remains
            Assume.That(s_transitionLastIsPending, Is.True, "Precondition: the transition lane is still queued");

            // Act — the second flush drains the transition lane
            mounted.FlushStateForTest();

            // Assert
            Assert.IsFalse(s_transitionLastIsPending, "isPending returns to false once the transition lane flushes");
        }

        #endregion

        #region Calling UseTransition outside Render

        [Test]
        public void Given_OutsideRender_When_UseTransitionCalled_Then_ThrowsInvalidOperationException()
        {
            // Act + Assert
            Assert.Throws<InvalidOperationException>(() => Hooks.UseTransition());
        }

        #endregion

        #region Nested startTransition

        [Test]
        public void Given_NestedStartTransition_When_InnerCalled_Then_DoesNotThrow()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));

            // Act + Assert — a nested startTransition joins the outer transition without throwing
            Assert.DoesNotThrow(() =>
                s_transitionStarter.Invoke(() =>
                    s_transitionStarter.Invoke(() => s_transitionSetValue.Invoke(5))));
        }

        [Test]
        public void Given_NestedStartTransition_When_Flushed_Then_InnerUpdateCommits()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            s_transitionStarter.Invoke(() =>
                s_transitionStarter.Invoke(() => s_transitionSetValue.Invoke(5)));

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(2, s_transitionRenderCount, "The nested transition's update commits on flush");
        }

        #endregion

        #region Async startTransition

        [Test]
        public void Given_AsyncStartTransition_When_Awaiting_Then_IsPendingStaysTrue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                s_transitionSetValue.Invoke(1);
                await gate.Task;
                s_transitionSetValue.Invoke(2);
            };

            // Act — the async action suspends at the await; the transition is still in flight
            s_transitionStarter.Invoke(asyncUpdates);

            // Assert — read the component fiber that owns the transition, not the wrapper root fiber
            Assert.IsTrue(s_transitionFiber.IsTransitionPending, "isPending stays true while the async transition is awaiting");
        }

        [Test]
        public void Given_AsyncStartTransitionAwaitingBeforeAnyUpdate_When_ACleanFiberFlushFires_Then_IsPendingStaysTrue()
        {
            // Arrange — the action awaits BEFORE its first setState, so the fiber has no pending lane at
            // all while the transition is in flight.
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                await gate.Task;
                s_transitionSetValue.Invoke(1);
            };
            s_transitionStarter.Invoke(asyncUpdates);
            Assume.That(s_transitionFiber.IsTransitionPending, Is.True, "Precondition: the async transition is awaiting");

            // Act — a drain callback armed earlier can legitimately fire on this now-clean fiber (e.g.
            // the delayed-tier callback left over after a starvation promotion drained every lane); the
            // not-dirty flush must not read the empty lane queue as this transition having settled.
            mounted.FlushStateForTest();

            // Assert
            Assert.IsTrue(s_transitionFiber.IsTransitionPending,
                "A flush on a clean fiber must not wipe an awaiting async transition's pending flag");
        }

        [Test]
        public void Given_AnAsyncTransition_When_ItsContinuationSetsStateAfterTheAwait_Then_ThatUpdateTakesTheTransitionLane()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                await gate.Task;
                s_transitionSetValue.Invoke(1);
            };
            s_transitionStarter.Invoke(asyncUpdates);
            Assume.That(s_transitionFiber.IsTransitionPending, Is.True, "Precondition: the async transition is awaiting");

            // Act — the awaited task completes, so the continuation runs with nothing of the starter call left
            // on the stack
            gate.TrySetResult();

            // Assert
            Assert.That(s_transitionFiber.LaneQueue.Contains(FiberUpdatePriority.Transition), Is.True,
                "An async action's post-await update is still scheduled on the transition lane");
        }

        [Test]
        public void Given_AsyncStartTransition_When_TaskCompletesAndFlushes_Then_IsPendingClears()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                s_transitionSetValue.Invoke(1);
                await gate.Task;
                s_transitionSetValue.Invoke(2);
            };
            s_transitionStarter.Invoke(asyncUpdates);
            Assume.That(s_transitionFiber.IsTransitionPending, Is.True, "Precondition: the async transition is awaiting");

            // Act — complete the awaited task so the continuation runs and the lane flushes
            gate.TrySetResult();
            mounted.FlushStateForTest();

            // Assert
            Assert.IsFalse(s_transitionLastIsPending, "isPending returns to false once the async transition completes and flushes");
        }

        #endregion

        #region Independent per-slot pending

        [Test]
        public void Given_TwoTransitions_When_OnlyOneStarts_Then_OtherSlotStaysNotPending()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoTransitionRender, key: "two"));
            s_twoStartA.Invoke(() => s_twoSetValue.Invoke(1));
            s_twoSetValue.Invoke(2); // Normal interrupt so the transition lane survives the first flush
            mounted.FlushStateForTest(); // drains Normal; transition lane remains
            Assume.That(s_twoLastIsPendingA, Is.True, "Precondition: the started slot reports pending");

            // Assert
            Assert.IsFalse(s_twoLastIsPendingB, "An unstarted slot stays not pending — each slot tracks pending independently");
        }

        [Test]
        public void Given_TwoTransitions_When_BothStart_Then_BothReportPending()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoTransitionRender, key: "two"));
            s_twoStartA.Invoke(() => s_twoSetValue.Invoke(1));
            s_twoStartB.Invoke(() => s_twoSetValueB.Invoke(1));
            s_twoSetValue.Invoke(2); // Normal interrupt so both slots' pending survive the first flush

            // Act
            mounted.FlushStateForTest(); // drains Normal; transition lane remains

            // Assert
            Assert.That((s_twoLastIsPendingA, s_twoLastIsPendingB), Is.EqualTo((true, true)),
                "Both started slots report pending concurrently");
        }

        [Test]
        public void Given_TwoStartedTransitions_When_TransitionLaneFlushed_Then_BothSlotsClear()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoTransitionRender, key: "two"));
            s_twoStartA.Invoke(() => s_twoSetValue.Invoke(1));
            s_twoStartB.Invoke(() => s_twoSetValueB.Invoke(1));
            s_twoSetValue.Invoke(2); // Normal interrupt
            mounted.FlushStateForTest(); // drains Normal; transition lane remains
            Assume.That((s_twoLastIsPendingA, s_twoLastIsPendingB), Is.EqualTo((true, true)),
                "Precondition: both slots are pending while the transition lane is queued");

            // Act
            mounted.FlushStateForTest(); // drains the transition lane

            // Assert
            Assert.That((s_twoLastIsPendingA, s_twoLastIsPendingB), Is.EqualTo((false, false)),
                "Both slots clear after the transition flush completes");
        }

        [Test]
        public void Given_OneSlotsAsyncTransitionAwaiting_When_ASecondSlotStartsItsOwn_Then_TheSecondSlotReportsPending()
        {
            // Arrange — slot A's async action parks on its await, so A's transition is still in flight
            using var mounted = V.Mount(_root, V.Component(TwoTransitionRender, key: "two"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                await gate.Task;
                s_twoSetValue.Invoke(1);
            };
            s_twoStarterA.Invoke(asyncUpdates);
            Assume.That(s_twoFiber.IsTransitionPending, Is.True, "Precondition: slot A's async transition is awaiting");

            // Act — an unrelated second slot starts its own transition inside that window
            s_twoStartB.Invoke(() => s_twoSetValueB.Invoke(1));

            // Assert
            Assert.That(s_twoFiber.TransitionSlots[1].IsPending, Is.True,
                "A slot started while another slot's async transition is in flight reports its own pending");
        }

        #endregion

        #region Discrete updates during an in-flight async transition

        [Test]
        public void Given_AnAwaitingAsyncTransition_When_AClickSetsStateOnTheSameComponent_Then_ItCommitsInsideTheClick()
        {
            // Arrange — the async action parks on its await without having scheduled anything
            using var mounted = V.Mount(_root, V.Component(ClickableTransitionRender, key: "clickable"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () => await gate.Task;
            s_clickableStarter.Invoke(asyncUpdates);
            Assume.That(s_clickableFiber.IsTransitionPending, Is.True, "Precondition: the async transition is awaiting");

            // Act — a discrete click sets state on the same component
            _root.Q<Button>("bump").SimulateClick();

            // Assert — the discrete event's synchronous flush drains the immediate tier only, so a label
            // showing the new value means the click's update was not demoted to the transition lane
            Assert.That(_root.Q<Label>("out").text, Is.EqualTo("1"),
                "A discrete update is not demoted while an async transition is in flight on the same fiber");
        }

        [Test]
        public void Given_AnAwaitingAsyncTransition_When_AClickReusesTheSameStarter_Then_ItsUpdateStaysOnTheTransitionLane()
        {
            // Arrange — the slot's async action parks on its await, so a further call on it joins that owner
            using var mounted = V.Mount(_root, V.Component(ClickableTransitionRender, key: "clickable"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () => await gate.Task;
            s_clickableStarter.Invoke(asyncUpdates);
            Assume.That(s_clickableFiber.IsTransitionPending, Is.True, "Precondition: the async transition is awaiting");

            // Act — a discrete click whose handler wraps its update in that same starter
            _root.Q<Button>("bump-deferred").SimulateClick();

            // Assert — an explicitly wrapped update keeps transition priority even when the discrete carve-out
            // would otherwise apply, so it is queued rather than committed by the click's synchronous flush
            Assert.That(
                (_root.Q<Label>("out").text, s_clickableFiber.LaneQueue.Contains(FiberUpdatePriority.Transition)),
                Is.EqualTo(("0", true)),
                "A joined startTransition call keeps its updates on the transition lane inside a discrete event");
        }

        [Test]
        public void Given_AnAwaitingAsyncTransition_When_AClickCompletesItsAwaitedTask_Then_TheResumedUpdateTakesUrgentPriority()
        {
            // Arrange — the action's only update comes after its await
            using var mounted = V.Mount(_root, V.Component(ClickableTransitionRender, key: "clickable"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            s_clickableGate = gate;
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                await gate.Task;
                s_clickableSetValue.Invoke(v => v + 1);
            };
            s_clickableStarter.Invoke(asyncUpdates);
            Assume.That(s_clickableFiber.IsTransitionPending, Is.True, "Precondition: the async transition is awaiting");

            // Act — a discrete click completes the awaited task, which resumes the action inside the handler
            _root.Q<Button>("release").SimulateClick();

            // Assert — the accepted cost of the discrete carve-out: this fiber looks exactly as it does for an
            // unrelated write from the same handler, so the resumed update commits at the handler's priority
            Assert.That(_root.Q<Label>("out").text, Is.EqualTo("1"),
                "An action resumed inside a discrete handler has its updates classified as that handler's");
        }

        #endregion

        #region Two-transition component

        private static Action<int> s_twoSetValue;
        private static Action<int> s_twoSetValueB;
        private static Action<Action> s_twoStartA;
        private static Action<Action> s_twoStartB;
        private static TransitionStarter s_twoStarterA;
        private static bool s_twoLastIsPendingA;
        private static bool s_twoLastIsPendingB;
        private static ComponentFiber s_twoFiber;

        [Component]
        private static VNode TwoTransitionRender()
        {
            s_twoFiber = FiberAmbientStack.Current;
            var (_, setValueA) = Hooks.UseState(0);
            var (_, setValueB) = Hooks.UseState(0);
            s_twoSetValue = setValueA;
            s_twoSetValueB = setValueB;
            var (isPendingA, startA) = Hooks.UseTransition();
            var (isPendingB, startB) = Hooks.UseTransition();
            s_twoStartA = startA;
            s_twoStartB = startB;
            s_twoStarterA = startA;
            s_twoLastIsPendingA = isPendingA;
            s_twoLastIsPendingB = isPendingB;
            return V.Label();
        }

        #endregion

        #region Ownership across unmount

        [Test]
        public void Given_AFiberUnmountedWhileItsAsyncTransitionAwaits_When_ItIsMountedAgain_Then_TheSlotStartsUnownedAndTheNextTransitionReportsPending()
        {
            // Arrange — the Unmount then Mount pair reuses one fiber and its hook slots, so a slot can enter
            // the new mount still owned by an action parked on an await the unmount could not settle
            var fiber = FiberRenderer.CreateRoot(TransitionRender);
            FiberRenderer.Mount(fiber, _root);
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () => await gate.Task;
            s_transitionStarter.Invoke(asyncUpdates);
            Assume.That(fiber.IsTransitionPending, Is.True, "Precondition: the async transition is awaiting");
            var slotBeforeUnmount = fiber.TransitionSlots[0];
            FiberRenderer.Unmount(fiber);
            FiberRenderer.Mount(fiber, _root);
            var pendingAfterRemount = fiber.IsTransitionPending;

            // Act — the remounted fiber starts a fresh transition on that same slot
            s_transitionStarter.Invoke(() => s_transitionSetValue.Invoke(1));

            // Assert — three terms, because pending after the new call is true either way while the old
            // owner's flag survives: the slot must be the same object (a fresh one proves nothing about
            // ownership), the remount must start it clear, and the new call must light it again
            Assert.That(
                (ReferenceEquals(fiber.TransitionSlots[0], slotBeforeUnmount),
                 pendingAfterRemount,
                 fiber.IsTransitionPending),
                Is.EqualTo((true, false, true)),
                "A remounted slot is owned by nobody, so the next startTransition opens its own pending scope");
        }

        #endregion

        #region Clickable transition component (UseTransition + a discrete click)

        private static TransitionStarter s_clickableStarter;
        private static ComponentFiber s_clickableFiber;
        private static StateUpdater<int> s_clickableSetValue;
        private static Cysharp.Threading.Tasks.UniTaskCompletionSource s_clickableGate;

        [Component]
        private static VNode ClickableTransitionRender()
        {
            s_clickableFiber = FiberAmbientStack.Current;
            var (value, setValue) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_clickableStarter = start;
            s_clickableSetValue = setValue;
            return V.Div(children: new VNode[]
            {
                V.Button(name: "bump", onClick: () => setValue.Invoke(v => v + 1)),
                V.Button(name: "bump-deferred", onClick: () => start.Invoke(() => setValue.Invoke(v => v + 1))),
                V.Button(name: "release", onClick: () => s_clickableGate?.TrySetResult()),
                V.Label(name: "out", text: value.ToString()),
            });
        }

        #endregion

        #region Transition component (UseState + UseTransition)

        private static int s_transitionRenderCount;
        private static bool s_transitionLastIsPending;
        private static Action<int> s_transitionSetValue;
        private static Action<Action> s_transitionStart;
        private static TransitionStarter s_transitionStarter;
        private static ComponentFiber s_transitionFiber;

        private static void ResetTransition()
        {
            s_transitionRenderCount = 0;
            s_transitionLastIsPending = false;
            s_transitionSetValue = null;
            s_transitionStart = null;
            s_transitionStarter = default;
            s_transitionFiber = null;
            s_twoSetValue = null;
            s_twoSetValueB = null;
            s_twoStartA = null;
            s_twoStartB = null;
            s_twoStarterA = default;
            s_twoLastIsPendingA = false;
            s_twoLastIsPendingB = false;
            s_twoFiber = null;
            s_clickableStarter = default;
            s_clickableFiber = null;
            s_clickableSetValue = default;
            s_clickableGate = null;
        }

        [Component]
        private static VNode TransitionRender()
        {
            s_transitionRenderCount++;
            s_transitionFiber = FiberAmbientStack.Current;
            var (_, setValue) = Hooks.UseState(0);
            s_transitionSetValue = setValue;
            var (isPending, start) = Hooks.UseTransition();
            s_transitionStart = start;
            s_transitionStarter = start;
            s_transitionLastIsPending = isPending;
            return V.Label();
        }

        #endregion
    }
}
