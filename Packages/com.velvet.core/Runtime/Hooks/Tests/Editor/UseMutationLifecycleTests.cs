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
    /// Specifies the lifecycle callbacks of <see cref="Hooks.UseMutation{TVariables, TData, TContext}"/> and the
    /// <c>OnSettled</c> of the context-free option records, against TanStack Query v5's
    /// <c>onMutate</c> / <c>onSuccess</c> / <c>onError</c> / <c>onSettled</c>.
    /// <list type="bullet">
    /// <item><c>OnMutate</c> runs after the handle turns pending and before the mutation function, and what it
    /// returns is the context that call's later callbacks receive.</item>
    /// <item><c>OnSettled</c> runs after <c>OnSuccess</c> or <c>OnError</c>, before the outcome is committed.</item>
    /// <item>A throwing <c>OnMutate</c> fails the call with its exception, and the callbacks get no context.</item>
    /// <item>A throwing <c>OnError</c> does not cost the call its <c>OnSettled</c>; a throwing <c>OnSettled</c> on
    /// the failure path does not cost it its outcome; one on the success path fails the call, which then
    /// settles again with that exception.</item>
    /// <item>Overlapping calls each hand their callbacks their own context.</item>
    /// <item>A call whose component unmounts while it is in flight still runs its callbacks, with the cancellation
    /// where the mutation function honours its token, and its <c>MutateAsync</c> rejects with that cancellation.</item>
    /// <item>Per-call callbacks run after the options' own, once the outcome is on the handle, and only for the call
    /// the handle still follows: a newer call, <c>Reset</c> and an unmount each drop them.</item>
    /// </list>
    /// <see cref="UseMutationHookTests"/> owns the lifecycle the handle reports, an unmounted call's included.
    /// </summary>
    [TestFixture]
    internal sealed class UseMutationLifecycleTests
    {
        private const string None = "none";
        private static int s_renderCount;

        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_log.Clear();
            s_captured = null;
            s_voidCaptured = null;
            s_noInputCaptured = null;
            s_mutationFn = (v, _) => VelvetTask.FromResult(v * 2);
            s_voidMutationFn = (_, _) => VelvetTask.CompletedTask;
            s_onMutateThrows = null;
            s_onErrorThrows = null;
            s_settledThrowsOnSuccess = null;
            s_settledThrowsOnFailure = null;
            s_callSuccessThrows = null;
            s_renderCount = 0;
        }

        [UnityTest]
        public IEnumerator Given_AContextMutation_When_Called_Then_OnMutateRunsBeforeTheMutationFunction() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutationFn = (v, _) =>
            {
                Record("fn", 0, null, v, null);
                return VelvetTask.FromResult(v * 2);
            };
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "mutate-order"));

            // Act
            await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind is "mutate" or "fn", e => e.Kind), Is.EqualTo("mutate,fn"),
                "OnMutate runs before the mutation function, as TanStack's execute awaits it before starting the retryer");
        });

        [UnityTest]
        public IEnumerator Given_AContextMutation_When_OnMutateRuns_Then_ItReadsTheHandleAsThisCallsPendingOne() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "mutate-pending"));

            // Act
            await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "mutate", e => $"{e.Status} {e.HandleVariables}"), Is.EqualTo("Pending 21"),
                "The handle is already this call's pending one when OnMutate runs, as TanStack dispatches pending before calling it");
        });

        [UnityTest]
        public IEnumerator Given_AContextMutation_When_ItSucceeds_Then_OnSuccessReceivesTheContextOnMutateReturned() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "success-context"));

            // Act
            await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "success", e => $"{e.Data} {e.Variables} {e.Context}"), Is.EqualTo("42 21 ctx21"),
                "OnSuccess receives the data, the variables and the context OnMutate returned");
        });

        [UnityTest]
        public IEnumerator Given_AContextMutation_When_ItSucceeds_Then_OnSettledReceivesTheDataNoErrorAndTheContext() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "success-settled"));

            // Act
            await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "settled", e => $"{e.Data} {e.Error} {e.Variables} {e.Context}"),
                Is.EqualTo("42 none 21 ctx21"),
                "A successful call settles once, with its data, no error, its variables and its context");
        });

        [UnityTest]
        public IEnumerator Given_AContextMutation_When_ItSucceeds_Then_OnSettledRunsAfterOnSuccess() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "success-order"));

            // Act
            await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind is "success" or "settled", e => e.Kind), Is.EqualTo("success,settled"),
                "OnSettled runs after OnSuccess");
        });

        [UnityTest]
        public IEnumerator Given_AContextMutation_When_OnSettledRunsOnSuccess_Then_TheOutcomeIsNotCommittedYet() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "success-settled-pending"));

            // Act
            await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "settled", e => e.Status.ToString()), Is.EqualTo("Pending"),
                "OnSettled runs before the success is committed, as TanStack dispatches it after onSettled");
        });

        [UnityTest]
        public IEnumerator Given_AFailingContextMutation_When_OnErrorRuns_Then_ItReceivesTheContext() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutationFn = (_, _) => throw new InvalidOperationException("boom");
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "error-context"));

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "error", e => $"{e.Error} {e.Variables} {e.Context}"), Is.EqualTo("boom 1 ctx1"),
                "OnError receives the exception, the variables and the context to roll back to");
        });

        [UnityTest]
        public IEnumerator Given_AFailingContextMutation_When_ItFails_Then_OnSettledReceivesNoDataTheErrorAndTheContext() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutationFn = (_, _) => throw new InvalidOperationException("boom");
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "error-settled"));

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "settled", e => $"{e.Data} {e.Error} {e.Variables} {e.Context}"),
                Is.EqualTo("0 boom 1 ctx1"),
                "A failed call settles once, with no data, its exception, its variables and its context");
        });

        [UnityTest]
        public IEnumerator Given_AFailingContextMutation_When_ItFails_Then_OnSettledRunsAfterOnError() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutationFn = (_, _) => throw new InvalidOperationException("boom");
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "error-order"));

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind is "error" or "settled", e => e.Kind), Is.EqualTo("error,settled"),
                "OnSettled runs after OnError");
        });

        [UnityTest]
        public IEnumerator Given_AFailingContextMutation_When_OnErrorThrows_Then_OnSettledStillRuns() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the expected log is what makes the throw load-bearing: without it OnSettled runs
            // whether or not the throw is contained apart from it.
            s_mutationFn = (_, _) => throw new InvalidOperationException("boom");
            s_onErrorThrows = new InvalidOperationException("onError threw");
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "error-throws-settled"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: onError threw"));

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "settled", e => e.Error), Is.EqualTo("boom"),
                "A throwing OnError is reported on its own and the call still settles with its own exception");
        });

        [UnityTest]
        public IEnumerator Given_AFailingContextMutation_When_OnSettledThrows_Then_TheCallStillEndsInItsOwnError() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var failure = new InvalidOperationException("boom");
            s_mutationFn = (_, _) => throw failure;
            s_settledThrowsOnFailure = new InvalidOperationException("onSettled threw");
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "error-settled-throws"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: onSettled threw"));
            Exception? rethrown = null;

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException caught) { rethrown = caught; }
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured!.Status, ReferenceEquals(s_captured.Error, failure), ReferenceEquals(rethrown, failure)),
                Is.EqualTo((MutationStatus.Error, true, true)),
                "A throwing OnSettled on the failure path is reported on its own and leaves the call's own outcome");
        });

        [UnityTest]
        public IEnumerator Given_AContextMutation_When_OnMutateThrows_Then_TheCallFailsWithItAndTheCallbacksGetNoContext() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_onMutateThrows = new InvalidOperationException("onMutate");
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "mutate-throws"));
            Exception? rethrown = null;

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException caught) { rethrown = caught; }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (Read(e => e.Kind is "error" or "settled", e => $"{e.Kind} {e.Error} {e.Context}"),
                    ReferenceEquals(rethrown, s_onMutateThrows), s_captured!.Status),
                Is.EqualTo(("error onMutate none,settled onMutate none", true, MutationStatus.Error)),
                "A throwing OnMutate fails the call with its exception, and OnError / OnSettled receive no context");
        });

        [UnityTest]
        public IEnumerator Given_TwoOverlappingContextMutations_When_TheySettleOutOfOrder_Then_EachCallbackGetsItsOwnCallsContext() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the second OnMutate runs while the first call is still in flight, so a context held
            // in one place both calls write reaches the first call's OnSuccess as the second call's.
            var first = new VelvetTaskCompletionSource<int>();
            var second = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (v, _) => v == 1 ? first.Task : second.Task;
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "overlap-context"));
            var firstCall = s_captured!.MutateAsync(1);
            var secondCall = s_captured.MutateAsync(2);

            // Act
            second.TrySetResult(20);
            await secondCall;
            first.TrySetResult(10);
            await firstCall;
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "success", e => $"{e.Variables} {e.Context}"), Is.EqualTo("2 ctx2,1 ctx1"),
                "Each call's OnSuccess receives the context its own OnMutate returned");
        });

        [UnityTest]
        public IEnumerator Given_AContextMutation_When_OnSettledThrowsOnSuccess_Then_TheCallFailsAndSettlesAgainWithThatError() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_settledThrowsOnSuccess = new InvalidOperationException("onSettled threw");
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "success-settled-throws"));
            Exception? rethrown = null;

            // Act
            try { await s_captured!.MutateAsync(21); } catch (InvalidOperationException caught) { rethrown = caught; }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (Read(e => e.Kind is "error" or "settled", e => $"{e.Kind} {e.Error}"),
                    ReferenceEquals(rethrown, s_settledThrowsOnSuccess), s_captured!.Status),
                Is.EqualTo(("settled none,error onSettled threw,settled onSettled threw", true, MutationStatus.Error)),
                "A throwing OnSettled on the success path fails the call, as a throwing OnSuccess does, and TanStack's " +
                "failure path then runs OnError and OnSettled with that exception");
        });

        [UnityTest]
        public IEnumerator Given_AnInFlightContextMutation_When_TheComponentUnmountsAndItThenSucceeds_Then_OnSuccessAndOnSettledStillRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the mutation function ignores its token, so the call completes after the unmount
            // rather than being cancelled by it.
            var gate = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (_, _) => gate.Task;
            var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "unmount-success"));
            var inFlight = s_captured!.MutateAsync(7);

            // Act
            mounted.Dispose();
            gate.TrySetResult(42);
            await inFlight;

            // Assert
            Assert.That(Read(_ => true, e => e.Kind), Is.EqualTo("mutate,success,settled"),
                "A call that completes after its component unmounted still runs OnSuccess and OnSettled, as " +
                "TanStack's option callbacks outlive the observer");
        });

        [UnityTest]
        public IEnumerator Given_AContextMutationHonouringItsToken_When_TheComponentUnmountsAndItThenSucceeds_Then_OnSuccessAndOnSettledRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the function honours the token it is handed, so a call the unmount cancelled would end
            // in the cancellation instead of its result.
            var gate = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (_, ct) => gate.Task.AttachExternalCancellation(ct);
            var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "unmount-honours-token"));
            var inFlight = s_captured!.MutateAsync(7);

            // Act
            mounted.Dispose();
            gate.TrySetResult(42);
            await inFlight;

            // Assert
            Assert.That(Read(_ => true, e => e.Kind), Is.EqualTo("mutate,success,settled"),
                "An unmount cancels nothing, so a call whose function honours its token still ends in its result");
        });

        [UnityTest]
        public IEnumerator Given_AMutationHonouringItsToken_When_TheComponentUnmounts_Then_MutateAsyncResolvesWithTheResult() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var gate = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (_, ct) => gate.Task.AttachExternalCancellation(ct);
            var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "unmount-resolves"));
            var inFlight = s_captured!.MutateAsync(7);

            // Act
            mounted.Dispose();
            gate.TrySetResult(42);
            var resolved = await inFlight;

            // Assert
            Assert.That(resolved, Is.EqualTo(42),
                "A call in flight at the unmount resolves its MutateAsync with the function's own result");
        });

        [UnityTest]
        public IEnumerator Given_AnUnmountedComponent_When_MutateAsyncIsCalled_Then_ItRunsAndResolvesWithTheResult() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "late-call-runs"));
            var handle = s_captured!;
            mounted.Dispose();

            // Act
            var resolved = await handle.MutateAsync(7);

            // Assert — the default function doubles its variable
            Assert.That(resolved, Is.EqualTo(14),
                "A call made after the unmount runs, as TanStack's mutate does once the observer is gone");
        });

        [UnityTest]
        public IEnumerator Given_AnUnmountedComponent_When_MutateAsyncIsCalled_Then_TheHandleIsNotWritten() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "late-call-no-write"));
            var handle = s_captured!;
            mounted.Dispose();

            // Act
            await handle.MutateAsync(7);

            // Assert — a handle written for a fiber that is gone would read Pending or Success here
            Assert.That(handle.Status, Is.EqualTo(MutationStatus.Idle),
                "A call made after the unmount runs without writing the handle");
        });

        [UnityTest]
        public IEnumerator Given_ACallWithACancellationTokenOnItsOptions_When_TheMutationFunctionRuns_Then_ItReceivesThatToken() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var source = new CancellationTokenSource();
            CancellationToken received = default;
            s_mutationFn = (v, ct) =>
            {
                received = ct;
                return VelvetTask.FromResult(v);
            };
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "caller-token"));

            // Act
            await s_captured!.MutateAsync(1, new MutateOptions<int, int> { CancellationToken = source.Token });

            // Assert
            Assert.That(received, Is.EqualTo(source.Token),
                "The token a caller puts on the call's options is the one the function receives");
        });

        [UnityTest]
        public IEnumerator Given_ACallWhoseCallerCancelsItsToken_When_TheFunctionHonoursIt_Then_TheHandleShowsTheCancellation() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var source = new CancellationTokenSource();
            var gate = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (_, ct) => gate.Task.AttachExternalCancellation(ct);
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "caller-cancels"));
            var inFlight = s_captured!.MutateAsync(1, new MutateOptions<int, int> { CancellationToken = source.Token });

            // Act
            source.Cancel();
            try { await inFlight; } catch (OperationCanceledException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_captured.Error, Is.InstanceOf<OperationCanceledException>(),
                "A call its caller cancelled is a failure like any other: the handle shows the cancellation");
        });

        [UnityTest]
        public IEnumerator Given_PerCallCallbacks_When_TheCallSucceeds_Then_TheyRunAfterTheHookOptionsCallbacks() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "call-order"));

            // Act
            await s_captured!.MutateAsync(21, CallOptions());
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(_ => true, e => e.Kind), Is.EqualTo("mutate,success,settled,call-success,call-settled"),
                "Per-call callbacks follow the options' OnSuccess and OnSettled, as TanStack's observer notifies after the mutation ran its own");
        });

        [UnityTest]
        public IEnumerator Given_PerCallCallbacks_When_TheCallSucceeds_Then_TheyReceiveTheDataVariablesAndContext() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "call-success-args"));

            // Act
            await s_captured!.MutateAsync(21, CallOptions());
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind.StartsWith("call-"), e => $"{e.Kind} {e.Data} {e.Error} {e.Variables} {e.Context}"),
                Is.EqualTo("call-success 42 none 21 ctx21,call-settled 42 none 21 ctx21"),
                "Per-call OnSuccess and OnSettled receive the data, a null exception, the variables and the OnMutate context");
        });

        [UnityTest]
        public IEnumerator Given_PerCallCallbacks_When_TheCallFails_Then_TheyReceiveTheErrorVariablesAndContext() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutationFn = (_, _) => throw new InvalidOperationException("boom");
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "call-error-args"));

            // Act
            try { await s_captured!.MutateAsync(3, CallOptions()); } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind.StartsWith("call-"), e => $"{e.Kind} {e.Data} {e.Error} {e.Variables} {e.Context}"),
                Is.EqualTo("call-error 0 boom 3 ctx3,call-settled 0 boom 3 ctx3"),
                "Per-call OnError and OnSettled receive the exception, default data, the variables and the OnMutate context");
        });

        [UnityTest]
        public IEnumerator Given_PerCallCallbacks_When_TheyRun_Then_TheHandleAlreadyShowsTheCallsOutcome() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "call-sees-outcome"));

            // Act
            await s_captured!.MutateAsync(21, CallOptions());
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "call-success", e => e.Status.ToString()), Is.EqualTo("Success"),
                "Per-call callbacks run once the outcome is on the handle, where the options' callbacks run before it");
        });

        [UnityTest]
        public IEnumerator Given_PerCallCallbacksOnFireAndForgetMutate_When_TheCallSucceeds_Then_TheyRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var gate = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (_, _) => gate.Task;
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "call-mutate"));
            s_captured!.Mutate(5, CallOptions());

            // Act
            gate.TrySetResult(10);
            await VelvetTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind.StartsWith("call-"), e => e.Kind), Is.EqualTo("call-success,call-settled"),
                "Mutate takes per-call callbacks as MutateAsync does");
        });

        [UnityTest]
        public IEnumerator Given_TwoOverlappingCallsWithPerCallCallbacks_When_TheFirstSettlesLast_Then_OnlyTheNewestCallsRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — TanStack's observer detaches from the previous mutation when a new one starts, so
            // the older call's per-call callbacks are dropped.
            var first = new VelvetTaskCompletionSource<int>();
            var second = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (v, _) => v == 1 ? first.Task : second.Task;
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "call-superseded"));
            var firstCall = s_captured!.MutateAsync(1, CallOptions());
            var secondCall = s_captured.MutateAsync(2, CallOptions());

            // Act
            second.TrySetResult(20);
            await secondCall;
            first.TrySetResult(10);
            await firstCall;
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "call-success", e => e.Variables.ToString()), Is.EqualTo("2"),
                "A call a newer one superseded still runs the options' callbacks, but not its per-call ones");
        });

        [UnityTest]
        public IEnumerator Given_ACallWithPerCallCallbacks_When_ItIsResetOutOfBeforeItSettles_Then_ThePerCallCallbacksDoNotRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — Reset detaches the observer in TanStack, and the call goes on to run the options' callbacks.
            var gate = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (_, _) => gate.Task;
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "call-reset"));
            var inFlight = s_captured!.MutateAsync(1, CallOptions());
            s_captured.Reset();

            // Act
            gate.TrySetResult(10);
            await inFlight;
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(_ => true, e => e.Kind), Is.EqualTo("mutate,success,settled"),
                "A reset call delivers the options' callbacks and drops its per-call ones");
        });

        [UnityTest]
        public IEnumerator Given_ACallWithPerCallCallbacks_When_TheComponentUnmountsBeforeItSettles_Then_ThePerCallCallbacksDoNotRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the function ignores its token, so the call completes after the unmount.
            var gate = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (_, _) => gate.Task;
            var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "call-unmount"));
            var inFlight = s_captured!.MutateAsync(1, CallOptions());

            // Act
            mounted.Dispose();
            gate.TrySetResult(10);
            await inFlight;

            // Assert
            Assert.That(Read(_ => true, e => e.Kind), Is.EqualTo("mutate,success,settled"),
                "A call whose component unmounted delivers the options' callbacks and drops its per-call ones, " +
                "as TanStack skips them once the observer has no listener");
        });

        [UnityTest]
        public IEnumerator Given_ACallWithPerCallCallbacksHonouringItsToken_When_TheComponentUnmountsBeforeItSettles_Then_OnlyTheOptionsCallbacksRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the function honours its token, so a call the unmount cancelled would end in an error
            // and run the options' error callbacks, not the success ones read here.
            var gate = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (_, ct) => gate.Task.AttachExternalCancellation(ct);
            var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "call-unmount-honours"));
            var inFlight = s_captured!.MutateAsync(1, CallOptions());

            // Act
            mounted.Dispose();
            gate.TrySetResult(10);
            await inFlight;

            // Assert
            Assert.That(Read(_ => true, e => e.Kind), Is.EqualTo("mutate,success,settled"),
                "The options' callbacks run with the call's real outcome and its per-call ones are dropped");
        });

        [UnityTest]
        public IEnumerator Given_ACallInFlightFromAnEarlierMount_When_TheSameFiberIsMountedAgainAndTheCallSettles_Then_ItsPerCallCallbacksDoNotRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — Unmount then Mount reuses one fiber and clears its IsDisposed, so the call from the
            // first mount settles against a fiber that reads as live.
            var gate = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (_, _) => gate.Task;
            var fiber = FiberRenderer.CreateRoot(ContextMutationRender);
            try
            {
                FiberRenderer.Mount(fiber, _root);
                var inFlight = s_captured!.MutateAsync(1, CallOptions());
                FiberRenderer.Unmount(fiber);
                FiberRenderer.Mount(fiber, _root);

                // Act
                gate.TrySetResult(10);
                await inFlight;

                // Assert — the options' callbacks are the earlier call's own and still run; the per-call ones
                // belong to the mount it was made in.
                Assert.That(Read(_ => true, e => e.Kind), Is.EqualTo("mutate,success,settled"),
                    "A call outliving its mount delivers the options' callbacks and drops its per-call ones");
            }
            finally
            {
                FiberRenderer.Dispose(fiber);
            }
        });

        // GREEN_ON_BASE(characterization): the base clears its mutation slots on unmount too, so a remount's first call
        // works there; the cut that leaves the retired slot in the list is what reddens this case.
        [UnityTest]
        public IEnumerator Given_AFiberUnmounted_When_MountedAgainAndTheNewHandleMutates_Then_TheHandleShowsTheNewCallsOutcome() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — Unmount then Mount reuses one fiber, so the slot list the unmount leaves behind is what
            // the remount's first render reads at index 0.
            var fiber = FiberRenderer.CreateRoot(ContextMutationRender);
            try
            {
                FiberRenderer.Mount(fiber, _root);
                FiberRenderer.Unmount(fiber);
                FiberRenderer.Mount(fiber, _root);

                // Act
                await s_captured!.MutateAsync(3);

                // Assert
                Assert.That((s_captured.Status, s_captured.Data), Is.EqualTo((MutationStatus.Success, 6)),
                    "A remount builds its own mutation slot, so its handle follows the calls it makes");
            }
            finally
            {
                FiberRenderer.Dispose(fiber);
            }
        });

        // GREEN_ON_BASE(characterization): the base also refuses a call started while the fiber is being disposed; the cut that drops the
        // IsDisposed clause from the owner check is what reddens this case, since the cleanup runs before the slots are retired.
        [Test]
        public void Given_AnEffectCleanupThatMutates_When_TheRootFiberIsDisposed_Then_TheHandleIsNotWritten()
        {
            // Arrange — a root fiber: Dispose sets IsDisposed and then Unmount runs the effect cleanups, ahead of
            // the point where it retires the mutation slots, so only the fiber's flag can refuse this call.
            // A child fiber under V.Mount is cleaned up before it is itself marked disposed.
            var fiber = FiberRenderer.CreateRoot(CleanupMutatesRender);
            try
            {
                FiberRenderer.Mount(fiber, _root);
                var handle = s_captured!;
                FiberEffects.FlushPendingPassiveEffects(fiber);

                // Act
                FiberRenderer.Dispose(fiber);

                // Assert
                Assert.That(handle.Status, Is.EqualTo(MutationStatus.Idle),
                    "A call a cleanup starts while the fiber is being disposed runs without writing the handle");
            }
            finally
            {
                FiberRenderer.Dispose(fiber);
            }
        }

        [Test]
        public void Given_AStaleHandleFromAnEarlierMount_When_ItIsResetAfterTheRemount_Then_TheNewMountDoesNotRender()
        {
            // Arrange — the first handle belongs to the slot the unmount retired; the remount renders a new one
            var fiber = FiberRenderer.CreateRoot(ContextMutationRender);
            try
            {
                FiberRenderer.Mount(fiber, _root);
                var stale = s_captured!;
                FiberRenderer.Unmount(fiber);
                FiberRenderer.Mount(fiber, _root);
                FiberWorkLoop.FlushState(fiber);
                var rendersBefore = s_renderCount;

                // Act
                stale.Reset();
                FiberWorkLoop.FlushState(fiber);

                // Assert
                Assert.That(s_renderCount, Is.EqualTo(rendersBefore),
                    "A handle from a mount that has gone cannot ask the live mount to render");
            }
            finally
            {
                FiberRenderer.Dispose(fiber);
            }
        }

        [UnityTest]
        public IEnumerator Given_APerCallOnSuccessThatThrows_When_TheCallSucceeds_Then_OnSettledStillRunsAndTheCallStaysASuccess() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_callSuccessThrows = new InvalidOperationException("call onSuccess threw");
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "call-throws"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: call onSuccess threw"));

            // Act
            await s_captured!.MutateAsync(21, CallOptions());
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Read(e => e.Kind == "call-settled", e => e.Kind), s_captured.Status),
                Is.EqualTo(("call-settled", MutationStatus.Success)),
                "A throwing per-call callback is reported on its own and costs neither the next one nor the outcome");
        });

        [UnityTest]
        public IEnumerator Given_ANoInputMutation_When_MutateIsCalledWithPerCallCallbacks_Then_TheyRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NoInputMutationRender, key: "no-input-call-mutate"));
            var settled = 0;

            // Act
            s_noInputCaptured!.Mutate(new MutateOptions<Unit, Unit> { OnSettled = (_, _, _, _) => settled++ });
            await VelvetTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(settled, Is.EqualTo(1), "The no-input Mutate shorthand forwards its per-call callbacks");
        });

        [UnityTest]
        public IEnumerator Given_ANoInputMutation_When_MutateAsyncIsCalledWithPerCallCallbacks_Then_TheyRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NoInputMutationRender, key: "no-input-call-async"));
            var settled = 0;

            // Act
            await s_noInputCaptured!.MutateAsync(new MutateOptions<Unit, Unit> { OnSettled = (_, _, _, _) => settled++ });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(settled, Is.EqualTo(1), "The no-input MutateAsync shorthand forwards its per-call callbacks");
        });

        [UnityTest]
        public IEnumerator Given_AContextFreeMutation_When_PerCallCallbacksRun_Then_TheyReceiveANullContext() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ContextFreeMutationRender, key: "context-free-call-context"));
            object? received = "unset";

            // Act
            await s_captured!.MutateAsync(1, new MutateOptions<int, int> { OnSuccess = (_, _, context) => received = context });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(received, Is.Null, "A context-free record has no OnMutate result, so its per-call callbacks get null rather than a boxed Unit");
        });

        [UnityTest]
        public IEnumerator Given_TwoOverlappingCallsWithPerCallCallbacks_When_TheFirstFailsLast_Then_ItsPerCallOnErrorDoesNotRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var first = new VelvetTaskCompletionSource<int>();
            var second = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (v, _) => v == 1 ? first.Task : second.Task;
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "call-superseded-error"));
            var firstCall = s_captured!.MutateAsync(1, CallOptions());
            var secondCall = s_captured.MutateAsync(2, CallOptions());

            // Act
            second.TrySetResult(20);
            await secondCall;
            first.TrySetException(new InvalidOperationException("boom"));
            try { await firstCall; } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind is "call-error" or "call-settled", e => $"{e.Kind} {e.Variables}"),
                Is.EqualTo("call-settled 2"),
                "A superseded call that fails delivers no per-call OnError or OnSettled");
        });

        [UnityTest]
        public IEnumerator Given_ACallWithPerCallCallbacks_When_ItIsResetOutOfAndThenFails_Then_ItsPerCallOnErrorDoesNotRun() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var gate = new VelvetTaskCompletionSource<int>();
            s_mutationFn = (_, _) => gate.Task;
            using var mounted = V.Mount(_root, V.Component(ContextMutationRender, key: "call-reset-error"));
            var inFlight = s_captured!.MutateAsync(1, CallOptions());
            s_captured.Reset();

            // Act
            gate.TrySetException(new InvalidOperationException("boom"));
            try { await inFlight; } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind.StartsWith("call-"), e => e.Kind), Is.EqualTo(""),
                "A reset call that fails delivers no per-call callbacks");
        });

        [UnityTest]
        public IEnumerator Given_AContextFreeMutation_When_ItSucceeds_Then_OnSettledRuns() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ContextFreeMutationRender, key: "context-free-settled"));

            // Act
            await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "settled", e => $"{e.Error} {e.Variables}"), Is.EqualTo("none 21"),
                "MutationOptions<TVariables, TData>.OnSettled runs once on success");
        });

        [UnityTest]
        public IEnumerator Given_AVoidMutation_When_ItFails_Then_OnSettledReceivesTheError() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_voidMutationFn = (_, _) => throw new InvalidOperationException("boom");
            using var mounted = V.Mount(_root, V.Component(VoidMutationRender, key: "void-settled"));

            // Act
            try { await s_voidCaptured!.MutateAsync(1); } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "settled", e => $"{e.Error} {e.Variables}"), Is.EqualTo("boom 1"),
                "MutationOptions<TVariables>.OnSettled receives the exception and the variables on failure");
        });

        [UnityTest]
        public IEnumerator Given_ANoInputMutation_When_ItSucceeds_Then_OnSettledReceivesNoError() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NoInputMutationRender, key: "no-input-settled"));

            // Act
            await s_noInputCaptured!.MutateAsync();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Read(e => e.Kind == "settled", e => e.Error), Is.EqualTo(None),
                "MutationOptions.OnSettled runs once on success, with no exception");
        });

        [Test]
        public void Given_NullContextOptions_When_UseMutationCalled_Then_ThrowsArgumentNullException()
        {
            // Act + Assert — called outside a render, so without the guard the hook's render check throws
            // an InvalidOperationException instead.
            Assert.Throws<ArgumentNullException>(() => Hooks.UseMutation((MutationOptions<int, int, string>)null!));
        }

        [Component]
        public static VNode CleanupMutatesRender()
        {
            var handle = Hooks.UseMutation(new MutationOptions<int, int, string>(MutationFn: s_mutationFn));
            s_captured = handle;
            Hooks.UseEffect((Func<Action?>)(() => () => handle.Mutate(1)), Array.Empty<object?>());
            return V.Label(text: "ok");
        }

        public static VNode ContextMutationRender()
        {
            s_renderCount++;
            s_captured = Hooks.UseMutation(new MutationOptions<int, int, string>(
                MutationFn: s_mutationFn,
                OnMutate: v =>
                {
                    Record("mutate", 0, null, v, null);
                    if (s_onMutateThrows != null) throw s_onMutateThrows;
                    return VelvetTask.FromResult($"ctx{v}");
                },
                OnSuccess: (data, v, context) =>
                {
                    Record("success", data, null, v, context);
                    return VelvetTask.CompletedTask;
                },
                OnError: (error, v, context) =>
                {
                    Record("error", 0, error, v, context);
                    if (s_onErrorThrows != null) throw s_onErrorThrows;
                    return VelvetTask.CompletedTask;
                },
                OnSettled: (data, error, v, context) =>
                {
                    Record("settled", data, error, v, context);
                    if (error == null && s_settledThrowsOnSuccess != null) throw s_settledThrowsOnSuccess;
                    if (error != null && s_settledThrowsOnFailure != null) throw s_settledThrowsOnFailure;
                    return VelvetTask.CompletedTask;
                }));
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode ContextFreeMutationRender()
        {
            s_captured = Hooks.UseMutation(new MutationOptions<int, int>(MutationFn: s_mutationFn)
            {
                OnSettled = (data, error, v) =>
                {
                    Record("settled", data, error, v, null);
                    return VelvetTask.CompletedTask;
                },
            });
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode VoidMutationRender()
        {
            s_voidCaptured = Hooks.UseMutation(new MutationOptions<int>(MutationFn: s_voidMutationFn)
            {
                OnSettled = (error, v) =>
                {
                    Record("settled", 0, error, v, null);
                    return VelvetTask.CompletedTask;
                },
            });
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode NoInputMutationRender()
        {
            s_noInputCaptured = Hooks.UseMutation(new MutationOptions(MutationFn: _ => VelvetTask.CompletedTask)
            {
                OnSettled = error =>
                {
                    Record("settled", 0, error, 0, null);
                    return VelvetTask.CompletedTask;
                },
            });
            return V.Label(text: "ok");
        }

        private static MutateOptions<int, int> CallOptions() => new()
        {
            OnSuccess = (data, v, context) =>
            {
                Record("call-success", data, null, v, context as string);
                if (s_callSuccessThrows != null) throw s_callSuccessThrows;
            },
            OnError = (error, v, context) => Record("call-error", 0, error, v, context as string),
            OnSettled = (data, error, v, context) => Record("call-settled", data, error, v, context as string),
        };

        // The handle's status and variables are read when the entry is written, which is what a callback
        // reading the handle would see at that point.
        private static void Record(string kind, int data, Exception? error, int variables, string? context) =>
            s_log.Add(new Entry
            {
                Kind = kind,
                Data = data,
                Error = error?.Message ?? None,
                Variables = variables,
                Context = context ?? None,
                Status = s_captured?.Status ?? MutationStatus.Idle,
                HandleVariables = s_captured?.Variables ?? 0,
            });

        private static string Read(Func<Entry, bool> which, Func<Entry, string> projection) =>
            string.Join(",", s_log.Where(which).Select(projection));

        private sealed record Entry
        {
            public string Kind { get; init; } = None;
            public int Data { get; init; }
            public string Error { get; init; } = None;
            public int Variables { get; init; }
            public string Context { get; init; } = None;
            public MutationStatus Status { get; init; }
            public int HandleVariables { get; init; }
        }

        private static readonly List<Entry> s_log = new();

        private static MutationResult<int, int>? s_captured;
        private static MutationResult<int, Unit>? s_voidCaptured;
        private static MutationResult<Unit, Unit>? s_noInputCaptured;
        private static Func<int, CancellationToken, VelvetTask<int>> s_mutationFn = (v, _) => VelvetTask.FromResult(v * 2);
        private static Func<int, CancellationToken, VelvetTask> s_voidMutationFn = (_, _) => VelvetTask.CompletedTask;
        private static Exception? s_onMutateThrows;
        private static Exception? s_callSuccessThrows;
        private static Exception? s_onErrorThrows;
        private static Exception? s_settledThrowsOnSuccess;
        private static Exception? s_settledThrowsOnFailure;
    }
}
