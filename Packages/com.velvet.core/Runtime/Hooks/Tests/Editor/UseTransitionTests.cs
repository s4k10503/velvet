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
    /// <item>The render that commits a transition observes <c>isPending == false</c> where that commit is the
    /// last thing the transition was waiting on. It observes it lit while something else still is: an
    /// <c>async</c> action in flight, whose pre-<c>await</c> write commits with the flag up, or a second
    /// fiber the same callback enrolled that this commit does not reach.</item>
    /// <item>Setting a state to an equal value inside a transition schedules no re-render.</item>
    /// <item>A Normal-priority update may interrupt a pending transition: <c>isPending</c> stays true through the
    /// flush that drains the Normal lane and returns to false at the flush that commits the transition's own
    /// work.</item>
    /// <item>Either overload's exit clears a flag no render is behind, so it asks the declaring component for
    /// the render that observes the clear whenever that component's last render read the flag lit — a
    /// synchronous callback that drove a flush of its own reaches this — and asks for nothing where that
    /// last render read it false. That render is never itself deferred by a transition scope it is reached
    /// inside.</item>
    /// <item>An async <c>startTransition</c> keeps <c>isPending</c> true across awaits until the task completes,
    /// and its completion asks the declaring component for the render that observes the cleared flag — the
    /// work it queued having already committed is not what finishes it. An action that suspended asks for it
    /// unconditionally, since a task continuation renders nobody. The updates it makes after an
    /// <c>await</c> that suspended it fall outside the scope its callback opened: they take the Normal lane,
    /// or the priority of whatever scope they do land in — a discrete handler's, or a further
    /// <c>startTransition</c> call's — and where the resume and the completion land in one handler, the two
    /// requests coalesce into a single render. An <c>await</c> of an already-completed task suspends
    /// nothing, so what follows such an await is still inside the scope and is still a transition. An update
    /// from elsewhere that lands while the action awaits keeps its own priority.</item>
    /// <item>A nested <c>startTransition</c> joins the outer transition: it applies its updates without starting a
    /// new transition and without throwing; a callback leaving by an exception still closes its scope.</item>
    /// <item>A transition's callback covers the updates it schedules on other fibers too, so a setter a component
    /// received as a prop is deferred by the transition that wraps the call. <c>isPending</c> then stays lit
    /// until each of those other fibers has discharged that work — committed it, unmounted, or had the
    /// scheduler drop it — and the declaring component re-renders on the commit that finishes them,
    /// including where nothing else would have re-rendered it, as for a write to a store another component
    /// reads. Two such fibers wait on each other: the first to commit settles nothing. For an async
    /// callback that commit is not the end of the transition, and the render falls to whichever of the two
    /// lands last.</item>
    /// <item>A slot the unmount of its own component released mid-callback records no further work.</item>
    /// <item>Each <c>UseTransition</c> slot tracks its own pending flag independently of other slots in the same
    /// component, including a slot started while another slot's async transition is still awaiting.</item>
    /// <item>A transition whose callback queues nothing settles without waiting on anything else on the
    /// component — a synchronous one when its callback returns, an async one on its own completion — even
    /// while another slot's transition work is still queued on the same fiber and even while another slot's
    /// in-flight action writes to it.</item>
    /// <item>A transition committed by a subsuming parent render clears its pending flag even when a
    /// <c>UseDeferredValue</c> in the same component holds the transition lane queued.</item>
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

        // GREEN_ON_BASE(characterization): the unwind closed the per-fiber scope the base kept too.
        // It is pinned here because the scope this branch replaces it with is process-wide, where a leak
        // would put later updates anywhere in the process on the transition lane rather than one
        // component's.
        [Test]
        public void Given_ATransitionCallbackThatThrew_When_ALaterSetterRuns_Then_ThatUpdateTakesTheNormalLane()
        {
            // Arrange — the callback leaves by an exception, so the scope closes on the unwind or not at all
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            try
            {
                s_transitionStart.Invoke(() => throw new InvalidOperationException("transition callback"));
            }
            catch (InvalidOperationException)
            {
            }

            // Act
            s_transitionSetValue.Invoke(1);

            // Assert
            Assert.That(
                (s_transitionFiber.LaneQueue.Contains(FiberUpdatePriority.Normal),
                 s_transitionFiber.LaneQueue.Contains(FiberUpdatePriority.Transition)),
                Is.EqualTo((true, false)),
                "A transition callback that throws leaves no scope open behind it");
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
        public void Given_AnAsyncTransition_When_ItsContinuationSetsStateAfterTheAwait_Then_ThatUpdateIsNotOnTheTransitionLane()
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
            var awaitingBeforeTheAct = s_transitionFiber.IsTransitionPending;

            // Act — the awaited task completes, so the continuation runs after the starter call's scope closed,
            // and only the immediate tier is drained after it
            gate.TrySetResult();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the committed value is what separates the two answers: a Normal-lane term reads true for
            // the render the completion asks for whether or not the write landed at all. The leading term is
            // folded in because a case where no transition was awaiting reads the same as one where the scope
            // correctly closed. Which immediate-tier lane the write took is not read here: the completion's own
            // request puts Normal on this fiber either way
            Assert.That(
                (awaitingBeforeTheAct,
                 s_transitionFiber.LaneQueue.Contains(FiberUpdatePriority.Transition),
                 s_transitionLastValue),
                Is.EqualTo((true, false, 1)),
                "An update made after an await that suspended is outside the scope the callback opened, so it takes no transition lane");
        }

        // The scope closes at the callback's first suspension, not at its first `await`. The two cases below
        // hold that difference, which shows only where the awaited task had already completed; the case
        // above is the same shape with a task that had not.
        // GREEN_ON_BASE(characterization): the base put this write on the transition lane as well.
        // Twice over, in fact: a per-fiber call depth held for the callback's synchronous run, and an
        // in-flight fallback behind it. Pinned because the boundary is easy to state wrongly — four shipped
        // sentences did.
        [Test]
        public void Given_AnAsyncTransitionAction_When_ItWritesAfterAwaitingAnAlreadyCompletedTask_Then_ThatWriteTakesTheTransitionLane()
        {
            // Arrange — the action's only write comes after an await of a task that has already completed
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                await Cysharp.Threading.Tasks.UniTask.CompletedTask;
                s_transitionSetValue.Invoke(1);
            };

            // Act — the whole action runs inside this call, since nothing in it suspends
            s_transitionStarter.Invoke(asyncUpdates);

            // Assert — both lanes, because an absent transition lane is also what a write that never landed
            // looks like
            Assert.That(
                (s_transitionFiber.LaneQueue.Contains(FiberUpdatePriority.Transition),
                 s_transitionFiber.LaneQueue.Contains(FiberUpdatePriority.Normal)),
                Is.EqualTo((true, false)),
                "A write after an await that never suspended is still inside the scope the starter opened");
        }

        // GREEN_ON_BASE(characterization): the flag was lit on the base as well.
        // Its write took the transition lane there by the route the case above names, so the same enrolment
        // held the flag up. What this pins is the other half of that state — that an action's own completion
        // path cannot settle a slot its own write enrolled.
        [Test]
        public void Given_AnAsyncTransitionActionThatNeverSuspended_When_ItHasReturned_Then_IsPendingIsStillLit()
        {
            // Arrange — same shape as above: the write lands under the open scope and enrols this component
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                await Cysharp.Threading.Tasks.UniTask.CompletedTask;
                s_transitionSetValue.Invoke(1);
            };

            // Act — the action's completion path has already run by the time this returns
            s_transitionStarter.Invoke(asyncUpdates);

            // Assert
            Assert.That(s_transitionFiber.IsTransitionPending, Is.True,
                "An action that never suspended still leaves enrolled work, so its completion cannot settle it");
        }

        // GREEN_ON_BASE(characterization): the base's completion asked for no render in any arrangement.
        // Pinned because this branch's completion does ask for one, and what separates the arrangement that
        // needs it from this one is whether the action suspended, read off the task the callback handed
        // back — so this fails if that reading stops answering.
        [Test]
        public void Given_AnAsyncTransitionActionThatNeverSuspendedAndWroteNothing_When_ItHasReturned_Then_ItAsksForNoRender()
        {
            // Arrange — nothing suspends and nothing is written, so the flag rises and falls inside the one
            // synchronous call below, exactly as the sync overload's does
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            var ranPastTheAwait = false;
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                await Cysharp.Threading.Tasks.UniTask.CompletedTask;
                ranPastTheAwait = true;
            };

            // Act — the action's completion path has already run by the time this returns
            s_transitionStarter.Invoke(asyncUpdates);

            // Assert — the run is folded in, since an action whose continuation never arrived leaves the fiber
            // just as clean
            Assert.That(
                (ranPastTheAwait, s_transitionFiber.IsDirty),
                Is.EqualTo((true, false)),
                "An action that never suspended asks for the render the synchronous overload asks for: none");
        }

        // GREEN_ON_BASE(characterization): the base's completion asked for no render in any arrangement.
        // Pinned because the async starter runs its callback inside the try that owns the release, and only a
        // callback that is not an async method can reach that release by throwing rather than by handing a
        // task back.
        [Test]
        public void Given_AnAsyncTransitionCallbackThatThrewBeforeReturningItsTask_When_ItUnwinds_Then_ItAsksForNoRender()
        {
            // Arrange — the callback is a plain lambda rather than an async method, so it throws instead of
            // returning a task, which is the one way the release runs with no task to read
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates =
                () => throw new InvalidOperationException("transition callback");
            var threw = false;

            // Act — the task is observed rather than dropped, so nothing is left for UniTask to report as an
            // unhandled exception
            var action = FiberWorkLoop.StartTransition(
                s_transitionFiber, s_transitionFiber.TransitionSlots[0], asyncUpdates);
            try
            {
                action.GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            // Assert — the throw is folded in, since a callback that was never reached leaves the fiber just
            // as clean
            Assert.That(
                (threw, s_transitionFiber.IsDirty),
                Is.EqualTo((true, false)),
                "A callback that threw before returning a task suspended nothing, so its clear asks for no render");
        }

        [Test]
        public void Given_AnAsyncTransitionsContinuation_When_ItWrapsOneOfTwoUpdatesAgain_Then_OnlyTheWrappedOneTakesTheTransitionLane()
        {
            // Arrange — the continuation makes two writes, one bare and one wrapped in a further call on the
            // same starter, so the two lanes separate the update the caller re-marked from the one it did not
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                await gate.Task;
                s_transitionSetValue.Invoke(1);
                s_transitionStarter.Invoke(() => s_transitionSetValue.Invoke(2));
            };
            s_transitionStarter.Invoke(asyncUpdates);
            var awaitingBeforeTheAct = s_transitionFiber.IsTransitionPending;

            // Act
            gate.TrySetResult();

            // Assert — the awaiting window is folded in, since a continuation that never ran leaves both lanes
            // saying nothing about which of the two writes the wrapping reached
            Assert.That(
                (awaitingBeforeTheAct,
                 s_transitionFiber.LaneQueue.Contains(FiberUpdatePriority.Normal),
                 s_transitionFiber.LaneQueue.Contains(FiberUpdatePriority.Transition)),
                Is.EqualTo((true, true, true)),
                "Re-wrapping is what puts a post-await update back in the transition, and it covers only that update");
        }

        [Test]
        public void Given_AnAwaitingAsyncTransition_When_AnUnrelatedSetterRuns_Then_ThatUpdateTakesTheNormalLane()
        {
            // Arrange — the action parks on its await having scheduled nothing, so the fiber carries no lane
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () => await gate.Task;
            s_transitionStarter.Invoke(asyncUpdates);
            var awaitingBeforeTheAct = s_transitionFiber.IsTransitionPending;

            // Act — a write belonging to nothing the action did: a timer tick, a store notification
            s_transitionSetValue.Invoke(1);

            // Assert — the in-flight window is folded in rather than assumed, since the lanes say nothing
            // unless the transition was still awaiting when the write landed
            Assert.That(
                (awaitingBeforeTheAct,
                 s_transitionFiber.LaneQueue.Contains(FiberUpdatePriority.Normal),
                 s_transitionFiber.LaneQueue.Contains(FiberUpdatePriority.Transition)),
                Is.EqualTo((true, true, false)),
                "An update outside the transition's callback keeps its own priority while that transition awaits");
        }

        [Test]
        public void Given_AnAsyncTransitionWhoseWorkAlreadyCommitted_When_ItsTaskCompletes_Then_ADrainRendersTheComponentNotPending()
        {
            // Arrange — the write lands before the await and the delayed tier commits it there, which is the
            // ordinary shape for any load outlasting that tier's delay. That commit renders the component with
            // the flag still lit, so the completion is the only thing left that can take it down.
            using var mounted = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                s_transitionSetValue.Invoke(1);
                await gate.Task;
            };
            s_transitionStarter.Invoke(asyncUpdates);
            mounted.GetSchedulerForTest().DrainDelayedForTest();
            var pendingAtTheCommit = s_transitionLastIsPending;

            // Act — the completing task is the whole interaction; nothing else touches the component
            gate.TrySetResult();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the commit render is folded in, since a component that never rendered pending reports
            // false for a reason the case is not about, and a precondition would report that as inconclusive
            Assert.That((pendingAtTheCommit, s_transitionLastIsPending), Is.EqualTo((true, false)),
                "An async action's completion asks for the render that observes its cleared flag");
        }

        // GREEN_ON_BASE(characterization): an async action's isPending clears on completion either way.
        // What this branch changed is how many lanes it leaves behind, so the case gained a second flush.
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
            var pendingWhileAwaiting = s_transitionFiber.IsTransitionPending;

            // Act — complete the awaited task so the continuation runs, then drain both lanes the action left:
            // the write before the await is the transition's, the one after it is a Normal update
            gate.TrySetResult();
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();

            // Assert — the awaiting reading is folded in, since a flag that never went up is already false
            // here and the second term would hold with nothing having cleared it
            Assert.That((pendingWhileAwaiting, s_transitionLastIsPending), Is.EqualTo((true, false)),
                "isPending returns to false once the async transition completes and flushes");
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

        [Test]
        public void Given_OneSlotsTransitionStillQueued_When_AnotherSlotsCallbackQueuesNothing_Then_OnlyTheQueueingSlotStaysPending()
        {
            // Arrange — slot A's transition leaves work on the transition lane, so the fiber is dirty
            using var mounted = V.Mount(_root, V.Component(TwoTransitionRender, key: "two"));
            s_twoStartA.Invoke(() => s_twoSetValue.Invoke(1));

            // Act — slot B's callback schedules nothing at all
            s_twoStartB.Invoke(() => { });

            // Assert — the lane term is folded in rather than assumed: B clearing means nothing unless
            // A's work is still queued at that moment, and a precondition would report that as inconclusive
            Assert.That(
                (s_twoFiber.LaneQueue.Contains(FiberUpdatePriority.Transition),
                 s_twoFiber.TransitionSlots[0].IsPending,
                 s_twoFiber.TransitionSlots[1].IsPending),
                Is.EqualTo((true, true, false)),
                "A transition that queued nothing settles when its callback returns, whatever another slot left queued");
        }

        [Test]
        public void Given_AnAwaitingAsyncTransitionThatQueuedNothing_When_AnotherSlotsCallbackQueuesWork_Then_ItStillClearsOnItsOwnCompletion()
        {
            // Arrange — slot A's action parks on its gate having written nothing; slot B then runs a
            // synchronous transition that does write, so the fiber carries B's transition lane while A awaits
            using var mounted = V.Mount(_root, V.Component(TwoTransitionRender, key: "two"));
            var gateA = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> actionA = async () => await gateA.Task;
            s_twoStarterA.Invoke(actionA);
            s_twoStartB.Invoke(() => s_twoSetValueB.Invoke(1));
            var inFlightBeforeTheAct = s_twoFiber.TransitionSlots[0].IsAsyncInFlight;

            // Act — A's action completes, still having queued nothing of its own
            gateA.TrySetResult();

            // Assert — three terms, because A clearing means nothing unless A really was in flight while B's
            // work was queued, and a precondition would report either miss as inconclusive
            Assert.That(
                (inFlightBeforeTheAct,
                 s_twoFiber.LaneQueue.Contains(FiberUpdatePriority.Transition),
                 s_twoFiber.TransitionSlots[0].IsPending),
                Is.EqualTo((true, true, false)),
                "A slot's pending flag answers for what its own callback queued, not for another slot's");
        }

        [Test]
        public void Given_TwoAsyncTransitionsInFlight_When_OneContinuationSetsState_Then_TheOtherSlotClearsOnItsOwnCompletion()
        {
            // Arrange — both slots park on their own gate, and only slot A's action ever writes
            using var mounted = V.Mount(_root, V.Component(TwoTransitionRender, key: "two"));
            var gateA = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            var gateB = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> actionA = async () =>
            {
                await gateA.Task;
                s_twoSetValue.Invoke(1);
            };
            Func<Cysharp.Threading.Tasks.UniTask> actionB = async () => await gateB.Task;
            s_twoStarterA.Invoke(actionA);
            s_twoStarterB.Invoke(actionB);
            var bothInFlightBeforeTheAct = s_twoFiber.TransitionSlots[0].IsAsyncInFlight
                && s_twoFiber.TransitionSlots[1].IsAsyncInFlight;

            // Act — A resumes and writes, then B's own action completes having queued nothing
            gateA.TrySetResult();
            gateB.TrySetResult();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — three terms, because B clearing means nothing unless two actions really were in flight
            // together and A's write really landed. A's committed value is what says the second of those: a
            // lane or dirty term reads true for the render each completion asks for, write or no write
            Assert.That(
                (bothInFlightBeforeTheAct, s_twoLastValueA, s_twoFiber.TransitionSlots[1].IsPending),
                Is.EqualTo((true, 1, false)),
                "A slot settles on what its own callback queued, not on what another slot's action wrote");
        }

        [Test]
        public void Given_AnAsyncTransitionsCompletion_When_ItIsReachedInsideAnotherSlotsScope_Then_ItsRenderIsNotDeferred()
        {
            // Arrange — slot A's action parks on a gate having written nothing, so its completion is nothing
            // but the clear; slot B's callback is what releases that gate, so A's continuation and the
            // completion behind it both run with B's transition scope open around them
            using var mounted = V.Mount(_root, V.Component(TwoTransitionRender, key: "two"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> actionA = async () => await gate.Task;
            s_twoStarterA.Invoke(actionA);
            var pendingBeforeTheAct = s_twoFiber.TransitionSlots[0].IsPending;

            // Act
            s_twoStartB.Invoke(() => gate.TrySetResult());

            // Assert — three terms: a completion that asked for nothing leaves both lanes absent, and a flag
            // that was never lit had nothing to take down
            Assert.That(
                (pendingBeforeTheAct,
                 s_twoFiber.LaneQueue.Contains(FiberUpdatePriority.Normal),
                 s_twoFiber.LaneQueue.Contains(FiberUpdatePriority.Transition)),
                Is.EqualTo((true, true, false)),
                "The render that takes an indicator down is not deferred by the transition it is reached inside");
        }

        #endregion

        #region A transition wrapping a setter owned by another fiber

        [Test]
        public void Given_AChildsTransition_When_ItWrapsASetterReceivedFromTheParent_Then_TheParentsUpdateTakesTheTransitionLane()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PropSetterParentRender, key: "prop-setter-parent"));

            // Act — the child defers an update to state it does not own, reached through the prop setter
            s_propSetterChildStart.Invoke(() => s_propSetterChildSetCount.Invoke(1));

            // Assert — both lanes are read, since a write that never landed would satisfy the transition term
            Assert.That(
                (s_propSetterParentFiber.LaneQueue.Contains(FiberUpdatePriority.Transition),
                 s_propSetterParentFiber.LaneQueue.Contains(FiberUpdatePriority.Normal)),
                Is.EqualTo((true, false)),
                "A transition covers the updates its callback schedules against another fiber's state too");
        }

        [Test]
        public void Given_AChildsTransitionOnTheParentsState_When_TheChildRendersBeforeItCommits_Then_ItObservesIsPendingTrue()
        {
            // Arrange — the child defers an update to state it does not own, reached through the prop setter
            using var mounted = V.Mount(_root, V.Component(PropSetterParentRender, key: "prop-setter-parent"));
            s_propSetterChildStart.Invoke(() => s_propSetterChildSetCount.Invoke(1));

            // Act — the child re-renders for a reason of its own while that update is still queued elsewhere
            s_propSetterChildSetTick.Invoke(1);
            FiberWorkLoop.FlushState(s_propSetterChildFiber);

            // Assert — the parent's output is folded in, since a lit flag says nothing unless the update it
            // stands for really is still uncommitted, and a precondition would report that as inconclusive
            Assert.That(
                (_root.Q<Label>("prop-setter-out").text, s_propSetterChildLastIsPending),
                Is.EqualTo(("0", true)),
                "isPending stays lit while the update the callback scheduled on another component is queued");
        }

        [Test]
        public void Given_AChildsTransitionOnTheParentsState_When_TheParentCommitsIt_Then_TheChildObservesIsPendingFalse()
        {
            // Arrange — the child is rendering its pending branch while the deferred update waits
            using var mounted = V.Mount(_root, V.Component(PropSetterParentRender, key: "prop-setter-parent"));
            s_propSetterChildStart.Invoke(() => s_propSetterChildSetCount.Invoke(1));
            s_propSetterChildSetTick.Invoke(1);
            FiberWorkLoop.FlushState(s_propSetterChildFiber);
            var indicatorBeforeTheCommit = _root.Q<Label>("prop-setter-pending").text;

            // Act — the delayed tier drains the transition lane the callback left on the parent
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert — three terms: an indicator that was never up comes down for free, and one that came
            // down proves nothing unless the content it stood in for arrived
            Assert.That(
                (indicatorBeforeTheCommit,
                 _root.Q<Label>("prop-setter-out").text,
                 _root.Q<Label>("prop-setter-pending").text),
                Is.EqualTo(("pending", "1", "idle")),
                "The indicator comes down on the commit that renders the transition's content");
        }

        [Test]
        public void Given_AChildsTransitionOnTheParentsState_When_TheParentCommitsIt_Then_TheChildRendersOnceForThatCommit()
        {
            // Arrange — the child is pending, so this commit owes it both the parent's new output and the
            // render that takes its indicator down
            using var mounted = V.Mount(_root, V.Component(PropSetterParentRender, key: "prop-setter-parent"));
            s_propSetterChildStart.Invoke(() => s_propSetterChildSetCount.Invoke(1));
            s_propSetterChildSetTick.Invoke(1);
            FiberWorkLoop.FlushState(s_propSetterChildFiber);
            var rendersBeforeTheCommit = s_propSetterChildRenderCount;

            // Act
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert — the difference, since the mount and the pending render both land before the act
            Assert.That(s_propSetterChildRenderCount - rendersBeforeTheCommit, Is.EqualTo(1),
                "The render the settle owes is the one the commit already makes, not a pass of its own");
        }

        [Test]
        public void Given_AnAsyncTransitionOnTheParentsStateThatAlreadyCommitted_When_ItsTaskCompletes_Then_TheChildsIndicatorComesDown()
        {
            // Arrange — the child's action writes the parent's state before its await, so the delayed tier
            // commits that write while the action is still in flight and the child stays lit through it
            using var mounted = V.Mount(_root, V.Component(PropSetterParentRender, key: "prop-setter-parent"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                s_propSetterChildSetCount.Invoke(1);
                await gate.Task;
            };
            s_propSetterChildStart.Invoke(asyncUpdates);
            mounted.GetSchedulerForTest().DrainDelayedForTest();
            var indicatorAfterThatCommit = _root.Q<Label>("prop-setter-pending").text;

            // Act
            gate.TrySetResult();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the still-lit indicator is folded in, since one that had already come down at the
            // parent's commit reads "idle" here whatever the completion does
            Assert.That(
                (indicatorAfterThatCommit, _root.Q<Label>("prop-setter-pending").text),
                Is.EqualTo(("pending", "idle")),
                "The completion takes the indicator down on the component that declared the transition");
        }

        [Test]
        public void Given_AnAsyncTransitionOnTheParentsStateThatAlreadyCommitted_When_ItsTaskCompletes_Then_TheChildRendersOnceForIt()
        {
            // Arrange — as above: the only render this completion is owed is the one that observes the
            // cleared flag, and no drain is left to subsume it into
            using var mounted = V.Mount(_root, V.Component(PropSetterParentRender, key: "prop-setter-parent"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                s_propSetterChildSetCount.Invoke(1);
                await gate.Task;
            };
            s_propSetterChildStart.Invoke(asyncUpdates);
            mounted.GetSchedulerForTest().DrainDelayedForTest();
            // Both tiers, so the act measures the completion alone: a callback write that did not reach the
            // delayed tier would otherwise still be queued here and commit inside the act, counting as the
            // completion's render.
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var rendersBeforeTheCompletion = s_propSetterChildRenderCount;

            // Act
            gate.TrySetResult();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the difference, since the mount and the parent's commit both land before the act
            Assert.That(s_propSetterChildRenderCount - rendersBeforeTheCompletion, Is.EqualTo(1),
                "The completion costs one render, not a pass per component the action enrolled");
        }

        // GREEN_ON_BASE(characterization): the post-await write re-renders the parent either way.
        // The parent's render is what reaches the child. What this pins is that the completion's own request
        // coalesces into that render rather than costing a second one.
        [Test]
        public void Given_AnAsyncTransitionWhoseContinuationWritesTheParentToo_When_ItCompletes_Then_TheChildStillRendersOnce()
        {
            // Arrange — the continuation writes after the await, so a render of the parent is already owed
            // when the completion asks for the child's
            using var mounted = V.Mount(_root, V.Component(PropSetterParentRender, key: "prop-setter-parent"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                s_propSetterChildSetCount.Invoke(1);
                await gate.Task;
                s_propSetterChildSetCount.Invoke(2);
            };
            s_propSetterChildStart.Invoke(asyncUpdates);
            mounted.GetSchedulerForTest().DrainDelayedForTest();
            var rendersBeforeTheCompletion = s_propSetterChildRenderCount;

            // Act
            gate.TrySetResult();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the parent's output is folded in, since a continuation whose write never landed owes
            // no render and the count would then hold for the reason the case is arranged to rule out. The
            // second term is the difference, the mount and the first commit having landed before the act
            Assert.That(
                (_root.Q<Label>("prop-setter-out").text,
                 s_propSetterChildRenderCount - rendersBeforeTheCompletion),
                Is.EqualTo(("2", 1)),
                "A render already owed absorbs the one the completion asks for");
        }

        [Test]
        public void Given_ATransitionWritingOnlyAStoreOtherComponentsRead_When_ThatWorkCommits_Then_TheDeclaringComponentClearsItsIndicator()
        {
            // Arrange — the writer's callback touches nothing of its own, so the only fiber the transition
            // enrols is the reader's, which the reader's own flush renders without going near the writer
            using var store = new TransitionCountStore();
            s_storeStore = store;
            using var mounted = V.Mount(_root, V.Component(StoreTransitionParentRender, key: "store-transition"));
            s_storeWriterStart.Invoke(() => store.Set(1));
            s_storeWriterSetTick.Invoke(1);
            FiberWorkLoop.FlushState(s_storeWriterFiber);
            var indicatorBeforeTheCommit = _root.Q<Label>("store-writer-pending").text;

            // Act — the delayed tier drains the reader's transition lane
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert — three terms: an indicator that was never up comes down for free, and one that came
            // down proves nothing unless the work the transition deferred has landed
            Assert.That(
                (indicatorBeforeTheCommit,
                 _root.Q<Label>("store-reader-out").text,
                 _root.Q<Label>("store-writer-pending").text),
                Is.EqualTo(("pending", "1", "idle")),
                "A component whose transition wrote only elsewhere renders again when that work commits");
        }

        // GREEN_ON_BASE(characterization): nothing on the base could reach the released slot.
        // It credited a Transition-lane enrolment to the slots of the fiber the write landed on, and the
        // released slot is on another one. What
        // this pins is the ambient scope this branch replaces that with, where every open call is a candidate.
        [Test]
        public void Given_AnUnmountInsideATransitionCallback_When_ALaterWriteRunsInTheSameCallback_Then_TheReleasedSlotRecordsNothing()
        {
            // Arrange — the unmount hands the child's slots back to nobody while its callback is still running,
            // and the slot list survives so a remount reuses this very slot
            using var mounted = V.Mount(_root, V.Component(PropSetterParentRender, key: "prop-setter-parent"));
            var slot = s_propSetterChildFiber.TransitionSlots[0];
            var releasedByTheUnmount = false;

            // Act — the callback tears its own component down and then writes state that lives elsewhere
            s_propSetterChildStart.Invoke(() =>
            {
                FiberRenderer.Unmount(s_propSetterChildFiber);
                releasedByTheUnmount = !slot.HasActiveOwner;
                s_propSetterChildSetCount.Invoke(1);
            });

            // Assert — the release is folded in, since a slot that was never released has an owner to
            // discharge the record and the second term would hold for a reason the case is not about
            Assert.That((releasedByTheUnmount, slot.HasQueuedWork), Is.EqualTo((true, false)),
                "A released slot records no work from the callback it was released inside");
        }

        private static ComponentFiber s_propSetterParentFiber;
        private static ComponentFiber s_propSetterChildFiber;
        private static TransitionStarter s_propSetterChildStart;
        private static StateUpdater<int> s_propSetterChildSetCount;
        private static StateUpdater<int> s_propSetterChildSetTick;
        private static bool s_propSetterChildLastIsPending;
        private static int s_propSetterChildRenderCount;

        [Component]
        private static VNode PropSetterParentRender()
        {
            s_propSetterParentFiber = FiberAmbientStack.Current;
            var (count, setCount) = Hooks.UseState(0);
            return V.Div(children: new VNode[]
            {
                V.Label(name: "prop-setter-out", text: count.ToString()),
                V.Component(PropSetterChildRender, setCount, key: "prop-setter-child"),
            });
        }

        [Component]
        private static VNode PropSetterChildRender(StateUpdater<int> setCount)
        {
            s_propSetterChildRenderCount++;
            s_propSetterChildFiber = FiberAmbientStack.Current;
            var (_, setTick) = Hooks.UseState(0);
            var (isPending, start) = Hooks.UseTransition();
            s_propSetterChildStart = start;
            s_propSetterChildSetCount = setCount;
            s_propSetterChildSetTick = setTick;
            s_propSetterChildLastIsPending = isPending;
            return V.Label(name: "prop-setter-pending", text: isPending ? "pending" : "idle");
        }

        private readonly record struct TransitionCountState(int Value);

        private sealed class TransitionCountStore : Store<TransitionCountState>
        {
            public TransitionCountStore() : base(new TransitionCountState(0)) { }
            public void Set(int value) => SetState(_ => new TransitionCountState(value));
            protected override void ResetCore() => SetState(_ => new TransitionCountState(0));
        }

        private static TransitionCountStore s_storeStore;
        private static ComponentFiber s_storeWriterFiber;
        private static TransitionStarter s_storeWriterStart;
        private static StateUpdater<int> s_storeWriterSetTick;

        [Component]
        private static VNode StoreTransitionParentRender()
            => V.Div(children: new VNode[]
            {
                V.Component(StoreTransitionWriterRender, key: "store-writer"),
                V.Component(StoreTransitionReaderRender, key: "store-reader"),
            });

        [Component]
        private static VNode StoreTransitionWriterRender()
        {
            s_storeWriterFiber = FiberAmbientStack.Current;
            var (_, setTick) = Hooks.UseState(0);
            var (isPending, start) = Hooks.UseTransition();
            s_storeWriterStart = start;
            s_storeWriterSetTick = setTick;
            return V.Label(name: "store-writer-pending", text: isPending ? "pending" : "idle");
        }

        [Component]
        private static VNode StoreTransitionReaderRender()
        {
            var value = Hooks.UseStore(s_storeStore, s => s.Value);
            return V.Label(name: "store-reader-out", text: value.ToString());
        }

        #endregion

        #region A callback enrolling two fibers

        [Test]
        public void Given_ATransitionCallbackThatEnrolledTwoFibers_When_TheFirstOfThemCommits_Then_ItStaysPendingUntilTheSecondDoes()
        {
            // Arrange — one store write reaches two sibling readers, so the callback enrols two fibers and
            // neither one's flush renders the other
            using var store = new TransitionCountStore();
            s_twoReaderStore = store;
            using var mounted = V.Mount(_root, V.Component(TwoReaderStoreParentRender, key: "two-reader"));
            s_twoReaderWriterStart.Invoke(() => store.Set(1));

            // Act — each reader is flushed on its own, so the two commits are separable
            FiberWorkLoop.FlushState(s_twoReaderFirstFiber);
            var pendingAfterTheFirstReader = s_twoReaderWriterFiber.IsTransitionPending;
            FiberWorkLoop.FlushState(s_twoReaderSecondFiber);

            // Assert — both readings, since a slot settling on the first commit and one never settling at
            // all differ from the contract in opposite directions
            Assert.That(
                (pendingAfterTheFirstReader, s_twoReaderWriterFiber.IsTransitionPending),
                Is.EqualTo((true, false)),
                "A slot waits on every fiber its callback enrolled, not on the first of them to commit");
        }

        private static TransitionCountStore s_twoReaderStore;
        private static ComponentFiber s_twoReaderWriterFiber;
        private static ComponentFiber s_twoReaderFirstFiber;
        private static ComponentFiber s_twoReaderSecondFiber;
        private static TransitionStarter s_twoReaderWriterStart;

        [Component]
        private static VNode TwoReaderStoreParentRender()
            => V.Div(children: new VNode[]
            {
                V.Component(TwoReaderStoreWriterRender, key: "two-reader-writer"),
                V.Component(TwoReaderStoreFirstReaderRender, key: "two-reader-first"),
                V.Component(TwoReaderStoreSecondReaderRender, key: "two-reader-second"),
            });

        [Component]
        private static VNode TwoReaderStoreWriterRender()
        {
            s_twoReaderWriterFiber = FiberAmbientStack.Current;
            var (isPending, start) = Hooks.UseTransition();
            s_twoReaderWriterStart = start;
            return V.Label(name: "two-reader-writer-pending", text: isPending ? "pending" : "idle");
        }

        [Component]
        private static VNode TwoReaderStoreFirstReaderRender()
        {
            s_twoReaderFirstFiber = FiberAmbientStack.Current;
            var value = Hooks.UseStore(s_twoReaderStore, s => s.Value);
            return V.Label(name: "two-reader-first-out", text: value.ToString());
        }

        [Component]
        private static VNode TwoReaderStoreSecondReaderRender()
        {
            s_twoReaderSecondFiber = FiberAmbientStack.Current;
            var value = Hooks.UseStore(s_twoReaderStore, s => s.Value);
            return V.Label(name: "two-reader-second-out", text: value.ToString());
        }

        #endregion

        #region A callback that renders the declaring component

        [Test]
        public void Given_ASynchronousTransitionCallbackThatDrivesAFlush_When_ItThenDefersAWrite_Then_ThatFlushRendersThePendingBranch()
        {
            // Arrange — an ordinary update queued before the call is what the flush inside the callback has
            // to render
            using var mounted = V.Mount(_root, V.Component(FlushingTransitionRender, key: "flushing"));
            s_flushingSetValue.Invoke(2);

            // Act — the callback renders the component by clicking a control whose handler writes nothing,
            // and only then makes the write it is deferring
            s_flushingStarter.Invoke(() =>
            {
                _root.Q<Button>("flushing-inert").SimulateClick();
                s_flushingSetValue.Invoke(3);
            });
            var indicatorAfterTheCallback = _root.Q<Label>("flushing-pending").text;
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert — the deferred write is folded in, since it is what holds the indicator up past the
            // callback and a first term left over from a callback that wrote nothing reads the same
            Assert.That(
                (indicatorAfterTheCallback, _root.Q<Label>("flushing-out").text),
                Is.EqualTo(("pending", "3")),
                "A flush driven from inside a transition callback renders the pending branch that call opened");
        }

        [Test]
        public void Given_ASynchronousTransitionCallbackThatDrivesAFlushAndEnrolsNothing_When_ItReturns_Then_TheIndicatorItRaisedComesDown()
        {
            // Arrange — as above, the queued update gives the flush inside the callback something to render
            using var mounted = V.Mount(_root, V.Component(FlushingTransitionRender, key: "flushing"));
            s_flushingSetValue.Invoke(2);

            // Act — the click is the callback's whole effect, so the flag it raised is cleared at its own
            // exit with no commit of its own left to observe that
            s_flushingStarter.Invoke(() => _root.Q<Button>("flushing-inert").SimulateClick());
            var indicatorAfterTheCallback = _root.Q<Label>("flushing-pending").text;
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the indicator the callback left up is folded in, since one that was never up comes
            // down for free and the case would hold with the pending branch never rendered at all
            Assert.That(
                (indicatorAfterTheCallback, _root.Q<Label>("flushing-pending").text),
                Is.EqualTo(("pending", "idle")),
                "The exit that clears a flag a render put on screen asks for the render that takes it down");
        }

        private static TransitionStarter s_flushingStarter;
        private static StateUpdater<int> s_flushingSetValue;

        [Component]
        private static VNode FlushingTransitionRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            var (isPending, start) = Hooks.UseTransition();
            s_flushingStarter = start;
            s_flushingSetValue = setValue;
            return V.Div(children: new VNode[]
            {
                V.Button(name: "flushing-inert", onClick: () => { }),
                V.Label(name: "flushing-pending", text: isPending ? "pending" : "idle"),
                V.Label(name: "flushing-out", text: value.ToString()),
            });
        }

        #endregion

        #region A deferred value holding the same lane

        [Test]
        public void Given_ATransitionCommittedByASubsumingRender_When_ADeferredValueHoldsTheTransitionLane_Then_IsPendingClears()
        {
            // Arrange — the child holds both hooks, and its transition queues work the parent's next render
            // commits along with the new prop
            using var mounted = V.Mount(_root,
                V.Component(DeferredTransitionParentRender, key: "deferred-transition-parent"));
            s_deferredChildStart.Invoke(() => s_deferredChildSetCount.Invoke(1));
            var pendingBeforeParentRender = s_deferredChildFiber.TransitionSlots[0].IsPending;

            // Act — the parent's state change re-renders the child inline with the new prop; the child's body
            // re-queues the transition lane for the deferred value while that render runs
            s_deferredParentSetQuery.Invoke("beta");
            FiberWorkLoop.FlushState(s_deferredParentFiber);

            // Assert — four terms, because the pending flag being false proves nothing unless the transition
            // was pending to begin with, its content is on screen, and the deferred value's lane is still there
            Assert.That(
                (pendingBeforeParentRender,
                 _root.Q<Label>("deferred-transition-out").text,
                 s_deferredChildFiber.LaneQueue.Contains(FiberUpdatePriority.Transition),
                 s_deferredChildFiber.TransitionSlots[0].IsPending),
                Is.EqualTo((true, "alpha:1", true, false)),
                "isPending belongs to the slot that queued the work, not to whoever holds the transition lane");
        }

        private static StateUpdater<string> s_deferredParentSetQuery;
        private static ComponentFiber s_deferredParentFiber;
        private static ComponentFiber s_deferredChildFiber;
        private static TransitionStarter s_deferredChildStart;
        private static StateUpdater<int> s_deferredChildSetCount;

        [Component]
        private static VNode DeferredTransitionParentRender()
        {
            s_deferredParentFiber = FiberAmbientStack.Current;
            var (query, setQuery) = Hooks.UseState("alpha");
            s_deferredParentSetQuery = setQuery;
            return V.Div(children: new VNode[]
            {
                V.Component(DeferredTransitionChildRender, query, key: "deferred-transition-child"),
            });
        }

        [Component]
        private static VNode DeferredTransitionChildRender(string query)
        {
            s_deferredChildFiber = FiberAmbientStack.Current;
            var (count, setCount) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_deferredChildStart = start;
            s_deferredChildSetCount = setCount;
            var deferred = Hooks.UseDeferredValue(query);
            return V.Label(name: "deferred-transition-out", text: $"{deferred}:{count}");
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

        // GREEN_ON_BASE(characterization): an explicitly wrapped update kept transition priority on the base.
        // Only the comment naming the mechanism changed, since the carve-out it named is gone.
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

            // Assert — an explicitly wrapped update keeps transition priority inside a discrete handler, so it
            // is queued rather than committed by the click's synchronous flush
            Assert.That(
                (_root.Q<Label>("out").text, s_clickableFiber.LaneQueue.Contains(FiberUpdatePriority.Transition)),
                Is.EqualTo(("0", true)),
                "A joined startTransition call keeps its updates on the transition lane inside a discrete event");
        }

        // GREEN_ON_BASE(characterization): the base gave a discrete-resumed continuation that same priority.
        // It was the deliberate cost of a carve-out this branch removes instead.
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

            // Assert — the continuation is outside its own transition, so what classifies it is the handler it
            // resumed inside
            Assert.That(_root.Q<Label>("out").text, Is.EqualTo("1"),
                "An action resumed inside a discrete handler has its updates classified as that handler's");
        }

        // GREEN_ON_BASE(characterization): the base's completion asked for no render at all.
        // One is therefore all the click could cost there — for want of the request rather than by
        // coalescing with it. Red against this branch's own previous head, which asked for that render at a
        // fixed Normal: Expected 1 But was 2.
        [Test]
        public void Given_AnAwaitingAsyncTransition_When_AClickCompletesItsAwaitedTask_Then_TheComponentRendersOnceForIt()
        {
            // Arrange — same shape as the case above: the continuation's write and the completion's cleared
            // flag are two requests on one fiber inside one handler
            using var mounted = V.Mount(_root, V.Component(ClickableTransitionRender, key: "clickable"));
            var gate = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            s_clickableGate = gate;
            Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates = async () =>
            {
                await gate.Task;
                s_clickableSetValue.Invoke(v => v + 1);
            };
            s_clickableStarter.Invoke(asyncUpdates);
            var rendersBeforeTheClick = s_clickableRenderCount;

            // Act
            _root.Q<Button>("release").SimulateClick();

            // Assert
            Assert.That(s_clickableRenderCount - rendersBeforeTheClick, Is.EqualTo(1),
                "The render a cleared pending flag asks for coalesces with the update that resumed alongside it");
        }

        #endregion

        #region Two-transition component

        private static Action<int> s_twoSetValue;
        private static Action<int> s_twoSetValueB;
        private static int s_twoLastValueA;
        private static Action<Action> s_twoStartA;
        private static Action<Action> s_twoStartB;
        private static TransitionStarter s_twoStarterA;
        private static TransitionStarter s_twoStarterB;
        private static bool s_twoLastIsPendingA;
        private static bool s_twoLastIsPendingB;
        private static ComponentFiber s_twoFiber;

        [Component]
        private static VNode TwoTransitionRender()
        {
            s_twoFiber = FiberAmbientStack.Current;
            var (valueA, setValueA) = Hooks.UseState(0);
            var (_, setValueB) = Hooks.UseState(0);
            s_twoLastValueA = valueA;
            s_twoSetValue = setValueA;
            s_twoSetValueB = setValueB;
            var (isPendingA, startA) = Hooks.UseTransition();
            var (isPendingB, startB) = Hooks.UseTransition();
            s_twoStartA = startA;
            s_twoStartB = startB;
            s_twoStarterA = startA;
            s_twoStarterB = startB;
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
        private static int s_clickableRenderCount;

        [Component]
        private static VNode ClickableTransitionRender()
        {
            s_clickableRenderCount++;
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
        private static int s_transitionLastValue;
        private static Action<int> s_transitionSetValue;
        private static Action<Action> s_transitionStart;
        private static TransitionStarter s_transitionStarter;
        private static ComponentFiber s_transitionFiber;

        private static void ResetTransition()
        {
            s_transitionRenderCount = 0;
            s_transitionLastIsPending = false;
            s_transitionLastValue = 0;
            s_transitionSetValue = null;
            s_transitionStart = null;
            s_transitionStarter = default;
            s_transitionFiber = null;
            s_twoSetValue = null;
            s_twoSetValueB = null;
            s_twoLastValueA = 0;
            s_twoStartA = null;
            s_twoStartB = null;
            s_twoStarterA = default;
            s_twoStarterB = default;
            s_twoLastIsPendingA = false;
            s_twoLastIsPendingB = false;
            s_twoFiber = null;
            s_clickableStarter = default;
            s_clickableFiber = null;
            s_clickableSetValue = default;
            s_clickableGate = null;
            s_clickableRenderCount = 0;
            s_deferredParentSetQuery = default;
            s_deferredParentFiber = null;
            s_deferredChildFiber = null;
            s_deferredChildStart = default;
            s_deferredChildSetCount = default;
            s_propSetterParentFiber = null;
            s_propSetterChildFiber = null;
            s_propSetterChildStart = default;
            s_propSetterChildSetCount = default;
            s_propSetterChildSetTick = default;
            s_propSetterChildLastIsPending = false;
            s_propSetterChildRenderCount = 0;
            s_storeStore = null;
            s_storeWriterFiber = null;
            s_storeWriterStart = default;
            s_storeWriterSetTick = default;
            s_twoReaderStore = null;
            s_twoReaderWriterFiber = null;
            s_twoReaderFirstFiber = null;
            s_twoReaderSecondFiber = null;
            s_twoReaderWriterStart = default;
            s_flushingStarter = default;
            s_flushingSetValue = default;
        }

        [Component]
        private static VNode TransitionRender()
        {
            s_transitionRenderCount++;
            s_transitionFiber = FiberAmbientStack.Current;
            var (value, setValue) = Hooks.UseState(0);
            s_transitionLastValue = value;
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
