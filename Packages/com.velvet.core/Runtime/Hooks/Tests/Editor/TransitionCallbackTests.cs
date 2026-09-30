using System;
using System.Text.RegularExpressions;
using UnityEngine;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a <c>startTransition</c> callback's awaits and errors do.
    /// <list type="bullet">
    /// <item>An await of a completed <see cref="VelvetTask"/> inside an action resumes once the discrete handler
    /// that started it has returned, or on the main thread's next tick outside a handler, so what it writes
    /// after the await is outside the transition and lands after the handler's own writes. A background
    /// thread's await is not held back.</item>
    /// <item>An error a callback throws never reaches the caller: it is thrown from the declaring component's
    /// Transition-lane render to the error boundary above it, that render being its own or an ancestor's pass
    /// reaching it. The outcome rendered is that of the call whose callback returned last, an action's
    /// settlement included, and a starter whose component has unmounted drops its error, for either overload.</item>
    /// <item>A continuation held back that throws is logged and does not keep the ones behind it from running,
    /// and one held back while a handler set the discrete flag itself runs once the flag clears.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class TransitionCallbackTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_hostStart = default;
            s_hostSetValue = default;
            s_throwingStart = default;
            s_filterStart = default;
            s_filterSet = default;
            FiberWorkLoop.IsInDiscreteEvent = false;
        }

        #region An await of a completed VelvetTask

        [Test]
        public void Given_AClickStartingAnActionThatAwaitsACompletedVelvetTask_When_TheHandlerWritesAfterTheStart_Then_TheActionsWriteLandsAfterIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(AwaitClickRender, key: "await-click"));

            // Act — the handler starts the action, whose write follows its await, then doubles the count itself
            _root.Q<Button>("await-completed").SimulateClick();

            // Assert — JavaScript runs the action's continuation after the handler returns: 0 * 2, then + 1
            Assert.That(_root.Q<Label>("await-out").text, Is.EqualTo("1"),
                "An await of a completed VelvetTask resumes once the handler that started the action has returned");
        }

        [Test]
        public void Given_AClickStartingAnActionThatAwaitsACompletedVelvetTaskOfT_When_TheHandlerWritesAfterTheStart_Then_TheActionsWriteLandsAfterIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(AwaitClickRender, key: "await-click"));

            // Act
            _root.Q<Button>("await-completed-result").SimulateClick();

            // Assert — 0 * 2, then + 5
            Assert.That(_root.Q<Label>("await-out").text, Is.EqualTo("5"),
                "An await of a completed VelvetTask<T> resumes once the handler that started the action has returned");
        }

        [Test]
        public void Given_AnActionOutsideAHandlerThatAwaitsACompletedVelvetTask_When_ItIsStarted_Then_ItsWriteWaitsForTheMainThreadsNextTick()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var ranPastTheAwait = false;

            // Act — nothing on the stack drains the continuation, so the main thread's hand-off queue does
            s_hostStart.Invoke(async () =>
            {
                await VelvetTask.CompletedTask;
                ranPastTheAwait = true;
                s_hostSetValue.Invoke(1);
            });
            var ranBeforeTheTick = ranPastTheAwait;
            VelvetMainThread.RunHandoffs();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the committed value is folded in, since a write the transition covered would wait for the
            // delayed tier
            Assert.That((ranBeforeTheTick, _root.Q<Label>("host-out").text), Is.EqualTo((false, "1")),
                "Outside a handler the continuation waits for the next main-thread tick, outside the transition");
        }

        // GREEN_ON_BASE(characterization): the base's awaiters knew nothing of a transition scope.
        // What this pins is that the scope open on the main thread holds back no continuation a background
        // thread's await reaches.
        [Test]
        public void Given_AnOpenTransitionScope_When_ABackgroundThreadAwaitsACompletedVelvetTask_Then_ItsContinuationRunsOnThatThreadAtOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var ranBeforeJoin = false;

            // Act — the callback blocks on the background thread, so a continuation held for the scope's close
            // has not run when the join returns
            s_hostStart.Invoke(() =>
            {
                var worker = new System.Threading.Thread(
                    () => ranBeforeJoin = AwaitCompletedOnThisThread().Status == VelvetTaskStatus.Succeeded);
                worker.Start();
                worker.Join();
            });

            // Assert
            Assert.That(ranBeforeJoin, Is.True,
                "A background thread's await of a completed VelvetTask continues inline whatever the main thread holds open");
        }

        private static async VelvetTask AwaitCompletedOnThisThread()
        {
            await VelvetTask.CompletedTask;
        }

        #endregion

        #region A callback that throws

        [Test]
        public void Given_ACallbackThatThrows_When_TheTransitionLaneRenders_Then_TheErrorReachesTheBoundaryAboveTheDeclaringComponent()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(StarterBoundaryRender, key: "starter-boundary"),
                new MountOptions((_, _) => { }));
            var threw = false;

            // Act
            try
            {
                s_throwingStart.Invoke(() => throw new InvalidOperationException("boom"));
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert — the caller's view is folded in, since React's startTransition does not rethrow either
            Assert.That((threw, _root.Q<Label>("starter-fallback")?.text), Is.EqualTo((false, "boom")),
                "The callback's error is thrown from the declaring component's Transition-lane render, as React's is");
        }

        [Test]
        public void Given_ACallbackThatWritesItsParentThenThrows_When_TheParentsTransitionPassReachesIt_Then_TheErrorReachesTheBoundary()
        {
            // Arrange — the parent's write puts the parent on the lane too, so the child's render is the one the
            // parent's pass reaches rather than a flush of its own
            using var mounted = V.Mount(_root, V.Component(FilterBoundaryRender, key: "filter-boundary"),
                new MountOptions((_, _) => { }));

            // Act
            s_filterStart.Invoke(() =>
            {
                s_filterSet.Invoke(1);
                throw new InvalidOperationException("filter boom");
            });
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(_root.Q<Label>("filter-fallback")?.text, Is.EqualTo("filter boom"),
                "A declaring component reached by an ancestor's Transition-lane pass throws the error there");
        }

        [Test]
        public void Given_TwoCallbacksThatThrow_When_TheTransitionLaneRenders_Then_TheSecondErrorIsThrown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(StarterBoundaryRender, key: "starter-boundary"),
                new MountOptions((_, _) => { }));

            // Act
            try
            {
                s_throwingStart.Invoke(() => throw new InvalidOperationException("first"));
                s_throwingStart.Invoke(() => throw new InvalidOperationException("second"));
            }
            catch (InvalidOperationException)
            {
            }
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(_root.Q<Label>("starter-fallback")?.text, Is.EqualTo("second"),
                "The outcome rendered is that of the call whose callback returned last");
        }

        [Test]
        public void Given_AnActionThatFaultsAfterALaterCallReturned_When_TheTransitionLaneRendersBeforeTheLaterOneSettles_Then_NoErrorIsThrown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(StarterBoundaryRender, key: "starter-boundary"),
                new MountOptions((_, _) => { }));
            var first = new VelvetTaskCompletionSource();
            var second = new VelvetTaskCompletionSource();
            s_throwingStart.Invoke(async () =>
            {
                await first.Task;
                throw new InvalidOperationException("first");
            });
            s_throwingStart.Invoke(async () => await second.Task);

            // Act
            first.TrySetResult();
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(_root.Q<Label>("starter-out")?.text, Is.EqualTo("content"),
                "React dispatches each call's outcome where its callback returns, so the later call's pending one is last");
        }

        [Test]
        public void Given_AnActionStartedInsideACallbackThatThenThrows_When_TheActionsAwaitResumes_Then_TheCallbacksErrorIsThrown()
        {
            // Arrange — the action returns a completed task, so its settlement is an await the open scope holds back
            using var mounted = V.Mount(_root, V.Component(StarterBoundaryRender, key: "starter-boundary"),
                new MountOptions((_, _) => { }));
            try
            {
                s_throwingStart.Invoke(() =>
                {
                    s_throwingStart.Invoke(() => VelvetTask.CompletedTask);
                    throw new InvalidOperationException("outer");
                });
            }
            catch (InvalidOperationException)
            {
            }

            // Act
            VelvetMainThread.RunHandoffs();
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(_root.Q<Label>("starter-fallback")?.text, Is.EqualTo("outer"),
                "The action's callback returned first, so the outer callback's error is the outcome rendered");
        }

        [Test]
        public void Given_ACallbackThatThrowsThenOneThatSucceeds_When_TheTransitionLaneRenders_Then_NoErrorIsThrown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(StarterBoundaryRender, key: "starter-boundary"),
                new MountOptions((_, _) => { }));

            // Act
            try
            {
                s_throwingStart.Invoke(() => throw new InvalidOperationException("first"));
            }
            catch (InvalidOperationException)
            {
            }
            s_throwingStart.Invoke(() => { });
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(_root.Q<Label>("starter-out")?.text, Is.EqualTo("content"),
                "The successful call's outcome replaces the error before it renders");
        }

        [Test]
        public void Given_AStarterWhoseComponentUnmounted_When_ItsCallbackThrows_Then_TheCallerSeesNothing()
        {
            // Arrange
            var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var start = s_hostStart;
            mounted.Dispose();
            var threw = false;

            // Act
            try
            {
                start.Invoke(() => throw new InvalidOperationException("after unmount"));
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            // Assert
            Assert.That(threw, Is.False,
                "React's dispatch to an unmounted component does nothing, so the error goes nowhere");
        }

        [Test]
        public void Given_AnActionThatFaultsAfterItsComponentUnmounted_When_ItCompletes_Then_NothingIsReported()
        {
            // Arrange — the action is awaiting when the component unmounts
            var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var gate = new VelvetTaskCompletionSource();
            s_hostStart.Invoke(async () =>
            {
                await gate.Task;
                throw new InvalidOperationException("after the unmount");
            });
            mounted.Dispose();

            // Act
            gate.TrySetResult();

            // Assert — a fault published as unobserved is logged, which this reads
            LogAssert.NoUnexpectedReceived();
        }

        #endregion

        #region Held-back continuations

        [Test]
        public void Given_TwoContinuationsHeldBackByATransition_When_TheFirstThrows_Then_TheSecondStillRuns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var secondRan = false;
            LogAssert.Expect(LogType.Exception, new Regex("continuation boom"));
            try
            {
                s_hostStart.Invoke(() =>
                {
                    var awaiter = VelvetTask.CompletedTask.GetAwaiter();
                    awaiter.OnCompleted(() => throw new InvalidOperationException("continuation boom"));
                    awaiter.OnCompleted(() => secondRan = true);
                });
            }
            catch (InvalidOperationException)
            {
            }

            // Act
            VelvetMainThread.RunHandoffs();

            // Assert — the expected log is what says the first one's error was published rather than dropped
            Assert.That(secondRan, Is.True, "A continuation that throws is logged and the drain goes on to the next");
        }

        // GREEN_ON_BASE(characterization): the base ran this continuation inline, holding none back to lose.
        // What this pins is that clearing the flag hands what the scope held back to the next tick.
        [Test]
        public void Given_AHandlerThatSetsTheDiscreteFlagItself_When_ItClearsTheFlag_Then_TheContinuationItsTransitionHeldBackRuns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var ranPastTheAwait = false;
            FiberWorkLoop.IsInDiscreteEvent = true;
            s_hostStart.Invoke(async () =>
            {
                await VelvetTask.CompletedTask;
                ranPastTheAwait = true;
            });

            // Act
            FiberWorkLoop.IsInDiscreteEvent = false;
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.That(ranPastTheAwait, Is.True, "Clearing the flag posts the drain a bracketed handler runs itself");
        }

        #endregion

        #region Components

        private static TransitionStarter s_hostStart;
        private static StateUpdater<int> s_hostSetValue;

        [Component]
        private static VNode HostRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_hostStart = start;
            s_hostSetValue = setValue;
            return V.Label(name: "host-out", text: value.ToString());
        }

        [Component]
        private static VNode AwaitClickRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            return V.Div(children: new VNode[]
            {
                V.Button(name: "await-completed", onClick: () =>
                {
                    start.Invoke(async () =>
                    {
                        await VelvetTask.CompletedTask;
                        setCount.Invoke(c => c + 1);
                    });
                    setCount.Invoke(c => c * 2);
                }),
                V.Button(name: "await-completed-result", onClick: () =>
                {
                    start.Invoke(async () =>
                    {
                        var amount = await VelvetTask.FromResult(5);
                        setCount.Invoke(c => c + amount);
                    });
                    setCount.Invoke(c => c * 2);
                }),
                V.Label(name: "await-out", text: count.ToString()),
            });
        }

        private static TransitionStarter s_throwingStart;

        [Component(IsErrorBoundary = true)]
        private static VNode StarterBoundaryRender()
        {
            Hooks.UseFallback(ex => V.Label(name: "starter-fallback", text: ex.Message));
            return V.Component(ThrowingStarterRender, key: "starter");
        }

        [Component]
        private static VNode ThrowingStarterRender()
        {
            var (_, start) = Hooks.UseTransition();
            s_throwingStart = start;
            return V.Label(name: "starter-out", text: "content");
        }

        private static TransitionStarter s_filterStart;
        private static Action<int> s_filterSet;

        [Component(IsErrorBoundary = true)]
        private static VNode FilterBoundaryRender()
        {
            Hooks.UseFallback(ex => V.Label(name: "filter-fallback", text: ex.Message));
            return V.Component(FilterParentRender, key: "filter-parent");
        }

        [Component]
        private static VNode FilterParentRender()
        {
            var (filter, setFilter) = Hooks.UseState(0);
            s_filterSet = setFilter;
            return V.Div(children: new VNode[]
            {
                V.Label(name: "filter-out", text: filter.ToString()),
                V.Component(FilterChildRender, key: "filter-child"),
            });
        }

        [Component]
        private static VNode FilterChildRender()
        {
            var (_, start) = Hooks.UseTransition();
            s_filterStart = start;
            return V.Label(name: "filter-child-out", text: "child");
        }

        #endregion
    }
}
