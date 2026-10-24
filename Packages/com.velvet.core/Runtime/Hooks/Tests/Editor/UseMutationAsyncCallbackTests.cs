using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a mutation waits for the task each of its callbacks returns, as TanStack Query v5 awaits the
    /// promise <c>onMutate</c>, <c>onSuccess</c>, <c>onError</c> and <c>onSettled</c> return.
    /// <list type="bullet">
    /// <item><c>OnMutate</c> is awaited before the mutation function starts, and its result is the context the
    /// later callbacks receive; a fault in it fails the call before the function is reached.</item>
    /// <item><c>OnSuccess</c>, <c>OnError</c> and <c>OnSettled</c> are each awaited before the next stage, and the
    /// outcome is committed to the handle only after the last of them.</item>
    /// <item>A task <c>OnSuccess</c> returns that faults fails the call as a throw does, and one <c>OnError</c>
    /// returns that faults costs the call neither its <c>OnSettled</c> nor its own exception.</item>
    /// <item>The option records that carry no context await their callbacks too.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Every gate starts completed, so a case opens only the one it is about. The log records each callback's start
    /// and end, which is how order is read.
    /// </remarks>
    [TestFixture]
    internal sealed class UseMutationAsyncCallbackTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_log.Clear();
            s_captured = null;
            s_voidCaptured = null;
            s_noInputCaptured = null;
            s_contextFreeCaptured = null;
            s_mutateGate = Open();
            s_successGate = Open();
            s_errorGate = Open();
            s_settledGate = Open();
            s_mutationFn = (v, _) =>
            {
                s_log.Add("fn");
                return VelvetTask.FromResult(v * 2);
            };
        }

        [UnityTest]
        public IEnumerator Given_AnOnMutateTaskNotYetComplete_When_Mutating_Then_TheMutationFunctionHasNotRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutateGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "mutate-pending"));

            // Act
            var inFlight = s_captured!.MutateAsync(21);
            var ranWhilePending = s_log.Contains("fn");
            s_mutateGate.TrySetResult();
            await inFlight;

            // Assert
            Assert.That(ranWhilePending, Is.False, "The call waits for the task OnMutate returned before it starts the function");
        });

        [UnityTest]
        public IEnumerator Given_AnOnMutateTask_When_ItCompletes_Then_TheMutationFunctionRunsAfterIt() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutateGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "mutate-order"));
            var inFlight = s_captured!.MutateAsync(21);

            // Act
            s_mutateGate.TrySetResult();
            await inFlight;

            // Assert
            Assert.That(string.Join(",", s_log.Where(entry => entry is "mutate-end" or "fn")), Is.EqualTo("mutate-end,fn"));
        });

        [UnityTest]
        public IEnumerator Given_AnOnMutateTask_When_TheCallSucceeds_Then_OnSuccessReceivesItsResultAsTheContext() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "mutate-context"));

            // Act
            await s_captured!.MutateAsync(21);

            // Assert
            Assert.That(s_log, Does.Contain("success-start ctx21"), "The context is what the awaited task produced");
        });

        [UnityTest]
        public IEnumerator Given_AnOnMutateTaskThatFaults_When_Mutating_Then_TheCallFailsWithItsException() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutateGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "mutate-faults"));
            var inFlight = s_captured!.MutateAsync(21);

            // Act
            s_mutateGate.TrySetException(new InvalidOperationException("mutate-failed"));
            try { await inFlight; } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.Status, s_captured.Error?.Message), Is.EqualTo((MutationStatus.Error, "mutate-failed")),
                "A rejection from OnMutate fails the mutation, as in v5");
        });

        [UnityTest]
        public IEnumerator Given_AnOnMutateTaskThatFaults_When_Mutating_Then_TheMutationFunctionNeverRuns() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutateGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "mutate-faults-fn"));
            var inFlight = s_captured!.MutateAsync(21);

            // Act
            s_mutateGate.TrySetException(new InvalidOperationException("mutate-failed"));
            try { await inFlight; } catch (InvalidOperationException) { }

            // Assert
            Assert.That(s_log, Does.Not.Contain("fn"));
        });

        [UnityTest]
        public IEnumerator Given_AnOnSuccessTaskNotYetComplete_When_TheFunctionReturns_Then_TheHandleIsStillPending() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_successGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "success-pending"));
            var inFlight = s_captured!.MutateAsync(21);

            // Act
            await VelvetTask.Yield();
            var statusWhileWaiting = s_captured.Status;
            s_successGate.TrySetResult();
            await inFlight;

            // Assert
            Assert.That(statusWhileWaiting, Is.EqualTo(MutationStatus.Pending), "The outcome is committed after the awaited handlers");
        });

        [UnityTest]
        public IEnumerator Given_AnOnSuccessTaskNotYetComplete_When_TheFunctionReturns_Then_OnSettledHasNotRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_successGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "success-settled-waits"));
            var inFlight = s_captured!.MutateAsync(21);

            // Act
            await VelvetTask.Yield();
            var settledWhileWaiting = s_log.Contains("settled-start");
            s_successGate.TrySetResult();
            await inFlight;

            // Assert
            Assert.That(settledWhileWaiting, Is.False, "OnSettled waits for the task OnSuccess returned");
        });

        [UnityTest]
        public IEnumerator Given_AnOnSuccessTask_When_ItCompletes_Then_OnSettledRunsAfterIt() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_successGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "success-order"));
            var inFlight = s_captured!.MutateAsync(21);

            // Act
            s_successGate.TrySetResult();
            await inFlight;

            // Assert
            Assert.That(string.Join(",", s_log.Where(entry => entry is "success-end" or "settled-start")),
                Is.EqualTo("success-end,settled-start"));
        });

        [UnityTest]
        public IEnumerator Given_AnOnSuccessTaskThatFaults_When_TheCallSucceeds_Then_OnErrorRunsWithItsException() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_successGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "success-faults"));
            var inFlight = s_captured!.MutateAsync(21);

            // Act
            s_successGate.TrySetException(new InvalidOperationException("success-failed"));
            try { await inFlight; } catch (InvalidOperationException) { }

            // Assert
            Assert.That(s_log, Does.Contain("error-start success-failed ctx21"),
                "A rejected OnSuccess fails the call, which then runs OnError with that exception");
        });

        [UnityTest]
        public IEnumerator Given_AnOnErrorTaskNotYetComplete_When_TheFunctionFails_Then_OnSettledHasNotRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutationFn = (_, _) => throw new InvalidOperationException("fn-failed");
            s_errorGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "error-pending"));
            var inFlight = s_captured!.MutateAsync(21);

            // Act
            await VelvetTask.Yield();
            var settledWhileWaiting = s_log.Contains("settled-start");
            s_errorGate.TrySetResult();
            try { await inFlight; } catch (InvalidOperationException) { }

            // Assert
            Assert.That(settledWhileWaiting, Is.False, "OnSettled waits for the task OnError returned");
        });

        [UnityTest]
        public IEnumerator Given_AnOnErrorTask_When_ItCompletes_Then_OnSettledRunsAfterIt() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutationFn = (_, _) => throw new InvalidOperationException("fn-failed");
            s_errorGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "error-order"));
            var inFlight = s_captured!.MutateAsync(21);

            // Act
            s_errorGate.TrySetResult();
            try { await inFlight; } catch (InvalidOperationException) { }

            // Assert
            Assert.That(string.Join(",", s_log.Where(entry => entry is "error-end" or "settled-start")),
                Is.EqualTo("error-end,settled-start"));
        });

        [UnityTest]
        public IEnumerator Given_AnOnErrorTaskThatFaults_When_TheFunctionFails_Then_OnSettledStillRuns() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutationFn = (_, _) => throw new InvalidOperationException("fn-failed");
            s_errorGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "error-faults-settled"));
            LogAssert.Expect(LogType.Exception, new Regex("ApplicationException: error-handler-failed"));
            var inFlight = s_captured!.MutateAsync(21);

            // Act
            s_errorGate.TrySetException(new ApplicationException("error-handler-failed"));
            try { await inFlight; } catch (InvalidOperationException) { }

            // Assert
            Assert.That(s_log, Does.Contain("settled-end"), "A handler that fails costs the call nothing after it");
        });

        [UnityTest]
        public IEnumerator Given_AnOnErrorTaskThatFaults_When_TheFunctionFails_Then_MutateAsyncRethrowsTheMutationException() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutationFn = (_, _) => throw new InvalidOperationException("fn-failed");
            s_errorGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "error-faults-rethrow"));
            LogAssert.Expect(LogType.Exception, new Regex("ApplicationException: error-handler-failed"));
            var inFlight = s_captured!.MutateAsync(21);
            string? thrown = null;

            // Act
            s_errorGate.TrySetException(new ApplicationException("error-handler-failed"));
            try { await inFlight; } catch (Exception ex) { thrown = ex.Message; }

            // Assert
            Assert.That(thrown, Is.EqualTo("fn-failed"), "The caller sees the mutation's failure, not the handler's");
        });

        [UnityTest]
        public IEnumerator Given_AnOnSettledTaskNotYetComplete_When_TheCallSucceeds_Then_TheHandleIsStillPending() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_settledGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "settled-pending"));
            var inFlight = s_captured!.MutateAsync(21);

            // Act
            await VelvetTask.Yield();
            var statusWhileWaiting = s_captured.Status;
            s_settledGate.TrySetResult();
            await inFlight;

            // Assert
            Assert.That(statusWhileWaiting, Is.EqualTo(MutationStatus.Pending), "The outcome is committed after OnSettled's task");
        });

        [UnityTest]
        public IEnumerator Given_AnOnSettledTask_When_ItCompletes_Then_TheHandleShowsTheOutcome() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_settledGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "settled-commits"));
            var inFlight = s_captured!.MutateAsync(21);

            // Act
            s_settledGate.TrySetResult();
            await inFlight;
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.Status, s_captured.Data), Is.EqualTo((MutationStatus.Success, 42)));
        });

        [UnityTest]
        public IEnumerator Given_AContextFreeMutationWithAnOnSuccessTaskNotYetComplete_When_TheFunctionReturns_Then_TheHandleIsStillPending() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_successGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(ContextFreeRender, key: "context-free"));
            var inFlight = s_contextFreeCaptured!.MutateAsync(21);

            // Act
            await VelvetTask.Yield();
            var statusWhileWaiting = s_contextFreeCaptured.Status;
            s_successGate.TrySetResult();
            await inFlight;

            // Assert
            Assert.That(statusWhileWaiting, Is.EqualTo(MutationStatus.Pending), "MutationOptions<TVariables, TData> awaits OnSuccess");
        });

        [UnityTest]
        public IEnumerator Given_AVoidMutationWithAnOnSuccessTaskNotYetComplete_When_TheFunctionReturns_Then_TheHandleIsStillPending() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_successGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(VoidRender, key: "void"));
            var inFlight = s_voidCaptured!.MutateAsync(21);

            // Act
            await VelvetTask.Yield();
            var statusWhileWaiting = s_voidCaptured.Status;
            s_successGate.TrySetResult();
            await inFlight;

            // Assert
            Assert.That(statusWhileWaiting, Is.EqualTo(MutationStatus.Pending), "MutationOptions<TVariables> awaits OnSuccess");
        });

        [UnityTest]
        public IEnumerator Given_ANoInputMutationWithAnOnSettledTaskNotYetComplete_When_TheFunctionReturns_Then_TheHandleIsStillPending() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_settledGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(NoInputRender, key: "no-input"));
            var inFlight = s_noInputCaptured!.MutateAsync();

            // Act
            await VelvetTask.Yield();
            var statusWhileWaiting = s_noInputCaptured.Status;
            s_settledGate.TrySetResult();
            await inFlight;

            // Assert
            Assert.That(statusWhileWaiting, Is.EqualTo(MutationStatus.Pending), "MutationOptions awaits OnSettled");
        });

        [UnityTest]
        public IEnumerator Given_AFireAndForgetCallWithAnOnSuccessTaskNotYetComplete_When_TheFunctionReturns_Then_TheHandleIsStillPending() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_successGate = new VelvetTaskCompletionSource();
            using var mounted = V.Mount(_root, V.Component(AsyncMutationRender, key: "forget"));

            // Act
            s_captured!.Mutate(21);
            await VelvetTask.Yield();
            var statusWhileWaiting = s_captured.Status;
            s_successGate.TrySetResult();
            await VelvetTask.Yield();

            // Assert
            Assert.That(statusWhileWaiting, Is.EqualTo(MutationStatus.Pending), "Mutate waits for its callbacks as MutateAsync does");
        });

        private static VelvetTaskCompletionSource Open()
        {
            var source = new VelvetTaskCompletionSource();
            source.TrySetResult();
            return source;
        }

        [Component]
        public static VNode AsyncMutationRender()
        {
            s_captured = Hooks.UseMutation(new MutationOptions<int, int, string>(
                MutationFn: (v, ct) => s_mutationFn(v, ct),
                OnMutate: async v =>
                {
                    s_log.Add("mutate-start");
                    await s_mutateGate.Task;
                    s_log.Add("mutate-end");
                    return "ctx" + v;
                },
                OnSuccess: async (data, v, context) =>
                {
                    s_log.Add("success-start " + context);
                    await s_successGate.Task;
                    s_log.Add("success-end");
                },
                OnError: async (error, v, context) =>
                {
                    s_log.Add($"error-start {error.Message} {context}");
                    await s_errorGate.Task;
                    s_log.Add("error-end");
                },
                OnSettled: async (data, error, v, context) =>
                {
                    s_log.Add("settled-start");
                    await s_settledGate.Task;
                    s_log.Add("settled-end");
                }));
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode ContextFreeRender()
        {
            s_contextFreeCaptured = Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: (v, ct) => s_mutationFn(v, ct),
                OnSuccess: async (data, v) => await s_successGate.Task));
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode VoidRender()
        {
            s_voidCaptured = Hooks.UseMutation(new MutationOptions<int>(
                MutationFn: (_, _) => VelvetTask.CompletedTask,
                OnSuccess: async v => await s_successGate.Task));
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode NoInputRender()
        {
            s_noInputCaptured = Hooks.UseMutation(new MutationOptions(
                MutationFn: _ => VelvetTask.CompletedTask,
                OnSuccess: null)
            {
                OnSettled = async _ => await s_settledGate.Task,
            });
            return V.Label(text: "ok");
        }

        private static readonly List<string> s_log = new();
        private static MutationResult<int, int>? s_captured;
        private static MutationResult<int, int>? s_contextFreeCaptured;
        private static MutationResult<int, Unit>? s_voidCaptured;
        private static MutationResult<Unit, Unit>? s_noInputCaptured;
        private static VelvetTaskCompletionSource s_mutateGate = null!;
        private static VelvetTaskCompletionSource s_successGate = null!;
        private static VelvetTaskCompletionSource s_errorGate = null!;
        private static VelvetTaskCompletionSource s_settledGate = null!;
        private static Func<int, CancellationToken, VelvetTask<int>> s_mutationFn = (v, _) => VelvetTask.FromResult(v * 2);
    }
}
