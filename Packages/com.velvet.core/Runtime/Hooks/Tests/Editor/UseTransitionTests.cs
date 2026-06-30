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
    /// <item>An async <c>startTransition</c> keeps <c>isPending</c> true across awaits until the task completes.</item>
    /// <item>A nested <c>startTransition</c> joins the outer transition: it applies its updates without starting a
    /// new transition and without throwing.</item>
    /// <item>Each <c>UseTransition</c> slot tracks its own pending flag independently of other slots in the same
    /// component.</item>
    /// <item>Calling the hook outside a render throws an <see cref="InvalidOperationException"/>.</item>
    /// <item>Pending state does not survive unmount: a remounted component starts with <c>isPending == false</c>.</item>
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

        #region Reset after unmount

        [Test]
        public void Given_PendingTransition_When_UnmountAndRemount_Then_IsPendingIsReset()
        {
            // Arrange
            var first = V.Mount(_root, V.Component(TransitionRender, key: "transition"));
            s_transitionStart.Invoke(() => s_transitionSetValue.Invoke(1));
            first.Dispose();

            // Act
            using var second = V.Mount(_root, V.Component(TransitionRender, key: "transition"));

            // Assert
            Assert.IsFalse(s_transitionLastIsPending, "A remounted component starts with isPending = false");
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

        #endregion

        #region Two-transition component

        private static Action<int> s_twoSetValue;
        private static Action<int> s_twoSetValueB;
        private static Action<Action> s_twoStartA;
        private static Action<Action> s_twoStartB;
        private static bool s_twoLastIsPendingA;
        private static bool s_twoLastIsPendingB;

        [Component]
        private static VNode TwoTransitionRender()
        {
            var (_, setValueA) = Hooks.UseState(0);
            var (_, setValueB) = Hooks.UseState(0);
            s_twoSetValue = setValueA;
            s_twoSetValueB = setValueB;
            var (isPendingA, startA) = Hooks.UseTransition();
            var (isPendingB, startB) = Hooks.UseTransition();
            s_twoStartA = startA;
            s_twoStartB = startB;
            s_twoLastIsPendingA = isPendingA;
            s_twoLastIsPendingB = isPendingB;
            return V.Label();
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
            s_twoLastIsPendingA = false;
            s_twoLastIsPendingB = false;
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
