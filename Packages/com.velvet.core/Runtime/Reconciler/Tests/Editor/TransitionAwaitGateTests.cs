using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins when a completed <c>VelvetTask</c>'s awaiter holds a continuation back around a <c>startTransition</c>
    /// callback on the main thread: while a synchronous callback runs, and while an action runs up to its first
    /// suspension, past a commit it ran whose layout effect started and finished a transition of its own included;
    /// and not once either kind of callback has returned.
    /// </summary>
    [TestFixture]
    internal sealed class TransitionAwaitGateTests
    {
        private static readonly List<MountedTree> s_mounts = new();
        private static TransitionStarter s_start;
        private static bool s_innerTransitionStarted;

        [SetUp]
        public void SetUp()
        {
            FiberStrictMode.Enabled = false;
            s_start = default;
            s_innerTransitionStarted = false;
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = s_mounts.Count - 1; i >= 0; i--) s_mounts[i].Dispose();
            s_mounts.Clear();
        }

        private static void Mount(VNode tree) => s_mounts.Add(V.Mount(new VisualElement(), tree));

        [Component]
        private static VNode StartsTransitions()
        {
            var (_, start) = Hooks.UseTransition();
            s_start = start;
            return V.Div();
        }

        [Component]
        private static VNode StartsATransitionFromItsMountLayoutEffect()
        {
            var (_, start) = Hooks.UseTransition();
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                start.Invoke(() => { });
                s_innerTransitionStarted = true;
                return null;
            }), Array.Empty<object>());
            return V.Div();
        }

        // GREEN_ON_BASE(characterization): the base holds back an action's await of a completed VelvetTask after a mount.
        // The commit inside the mount sets the action's scope aside while a layout effect opens and closes a scope of
        // its own, and this pins that the action's await after the mount is still held back for the next tick.
        [Test]
        public void Given_AnActionThatMountsARootWhoseLayoutEffectStartsATransition_When_ItThenAwaitsACompletedVelvetTask_Then_ItsContinuationWaitsForTheMainThreadsNextTick()
        {
            // Arrange
            Mount(V.Component(StartsTransitions));
            var reachedTheAwait = false;
            var continued = false;

            // Act — the hand-offs then run a continuation held back, so it does not outlive the case
            s_start.Invoke(async () =>
            {
                Mount(V.Component(StartsATransitionFromItsMountLayoutEffect));
                reachedTheAwait = true;
                await VelvetTask.CompletedTask;
                continued = true;
            });
            var continuedBeforeTheTick = continued;
            VelvetMainThread.RunHandoffs();

            // Assert — null where the action did not reach its await past a layout effect that started a transition
            Assert.That(reachedTheAwait && s_innerTransitionStarted ? continuedBeforeTheTick : (bool?)null, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base holds back an action's await of a completed VelvetTask.
        // This change rewrote the assignment that raises the gate as the action's scope opens.
        [Test]
        public void Given_AnAction_When_ItAwaitsACompletedVelvetTask_Then_ItsContinuationWaitsForTheMainThreadsNextTick()
        {
            // Arrange
            Mount(V.Component(StartsTransitions));
            var reachedTheAwait = false;
            var continued = false;

            // Act — the hand-offs then run the continuation held back, so it does not outlive the case
            s_start.Invoke(async () =>
            {
                reachedTheAwait = true;
                await VelvetTask.CompletedTask;
                continued = true;
            });
            var continuedBeforeTheTick = continued;
            VelvetMainThread.RunHandoffs();

            // Assert — null where the action did not reach its await
            Assert.That(reachedTheAwait ? continuedBeforeTheTick : (bool?)null, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base holds back a continuation a synchronous callback hands over.
        // This change rewrote the assignment that raises the gate as that callback's scope opens.
        [Test]
        public void Given_ASynchronousTransitionCallback_When_ItHandsACompletedVelvetTaskAContinuation_Then_TheContinuationWaitsForTheMainThreadsNextTick()
        {
            // Arrange
            Mount(V.Component(StartsTransitions));
            var handedOver = false;
            var ran = false;

            // Act — the hand-offs then run a continuation held back, so it does not outlive the case
            s_start.Invoke(() =>
            {
                VelvetTask.CompletedTask.GetAwaiter().OnCompleted(() => ran = true);
                handedOver = true;
            });
            var ranBeforeTheTick = ran;
            VelvetMainThread.RunHandoffs();

            // Assert — null where the callback did not hand the continuation over
            Assert.That(handedOver ? ranBeforeTheTick : (bool?)null, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base runs a continuation at once after a synchronous callback returned.
        // This change rewrote the assignment that lowers the gate as that callback's scope closes.
        [Test]
        public void Given_ASynchronousTransitionCallbackThatReturned_When_ACompletedVelvetTaskIsHandedAContinuation_Then_TheContinuationRunsAtOnce()
        {
            // Arrange
            Mount(V.Component(StartsTransitions));
            var returned = false;
            s_start.Invoke(() => returned = true);
            var ran = false;

            // Act
            VelvetTask.CompletedTask.GetAwaiter().OnCompleted(() => ran = true);

            // Assert — null where no callback ran
            Assert.That(returned ? ran : (bool?)null, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base runs a continuation at once after an action that never suspended.
        // This change rewrote the assignment that lowers the gate as the action's scope closes.
        [Test]
        public void Given_AnActionThatReturnedWithoutSuspending_When_ACompletedVelvetTaskIsHandedAContinuation_Then_TheContinuationRunsAtOnce()
        {
            // Arrange
            Mount(V.Component(StartsTransitions));
            var returned = false;
            s_start.Invoke(async () =>
            {
                await Task.CompletedTask;
                returned = true;
            });
            var ran = false;

            // Act
            VelvetTask.CompletedTask.GetAwaiter().OnCompleted(() => ran = true);

            // Assert — null where no action ran to its end
            Assert.That(returned ? ran : (bool?)null, Is.True);
        }
    }
}
