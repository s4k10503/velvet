using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseMutation{TVariables, TData}"/> in a function component.
    /// <list type="bullet">
    /// <item>The handle starts <c>Idle</c> with no data, error, or variables.</item>
    /// <item><c>MutateAsync</c> drives the lifecycle Idle → Pending → Success, exposing the result, the latest
    /// variables, and clearing the error on success.</item>
    /// <item>A throwing mutation function transitions to <c>Error</c>, retains the thrown exception, and rethrows it
    /// to the caller's await.</item>
    /// <item><c>Reset</c> restores the handle to <c>Idle</c> and clears data, error, and variables.</item>
    /// <item>The handle reference is stable across re-renders.</item>
    /// <item>Generic positions accept <see cref="Unit"/> for "no variables" / "no return value", with void-return and
    /// no-input overloads that adapt to a <see cref="Unit"/> result.</item>
    /// <item>Concurrent mutations both run: neither cancels the other and each fires its own callbacks, while
    /// the observed status, data and variables come from the latest call.</item>
    /// <item>If the component unmounts while a mutation is in flight, the caller's await still observes the function
    /// result but the disposed fiber does not receive a Success state transition.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Lifecycle transitions are observed across renders, so the success / error / reset / concurrency cases run as
    /// coroutine tests that await the mutation and then flush. The mutation function and captured handles are reset
    /// together in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseMutationHookTests
    {
        private VisualElement _root = null!;
        private static MutationResult<int, int>? s_captured;
        private static Func<int, CancellationToken, UniTask<int>> s_mutationFn = (v, _) => UniTask.FromResult(v * 2);

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_captured = null;
            s_mutationFn = (v, _) => UniTask.FromResult(v * 2);
            s_onErrorCount = 0;
            s_voidCaptured = null;
            s_noInputCaptured = null;
            s_onSuccessThrowException = null;
            s_delivered.Clear();
            s_onErrorThrowException = null;
        }

        [Test]
        public void Given_FirstRender_When_Mounted_Then_StatusIsIdle()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "idle"));
            Assume.That(s_captured, Is.Not.Null, "Precondition: the hook produced a handle");

            // Assert
            Assert.That(s_captured!.Status, Is.EqualTo(MutationStatus.Idle), "The handle starts in the Idle status");
        }

        [Test]
        public void Given_FirstRender_When_Mounted_Then_DataErrorAndIdleFlagAreInitial()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "idle-fields"));
            Assume.That(s_captured, Is.Not.Null, "Precondition: the hook produced a handle");

            // Assert
            Assert.That((s_captured!.IsIdle, s_captured.Data, s_captured.Error), Is.EqualTo((true, default(int), (Exception?)null)),
                "An Idle handle reports IsIdle with default data and no error");
        }

        [UnityTest]
        public IEnumerator Given_IdleMutation_When_MutateAsyncSucceeds_Then_StatusIsSuccess() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "success"));

            // Act
            await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_captured.Status, Is.EqualTo(MutationStatus.Success), "A completed mutation transitions to Success");
        });

        [UnityTest]
        public IEnumerator Given_IdleMutation_When_MutateAsyncSucceeds_Then_ExposesResultAndVariables() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "success-fields"));

            // Act
            var result = await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((result, s_captured.Data, s_captured.Variables, s_captured.Error),
                Is.EqualTo((42, 42, 21, (Exception?)null)),
                "Success exposes the result as the return value and Data, retains the variables, and clears the error");
        });

        [UnityTest]
        public IEnumerator Given_ThrowingMutation_When_MutateAsync_Then_StatusIsError() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var failingException = new InvalidOperationException("simulated");
            s_mutationFn = (_, _) => throw failingException;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "error"));

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_captured!.Status, Is.EqualTo(MutationStatus.Error), "A throwing mutation transitions to Error");
        });

        [UnityTest]
        public IEnumerator Given_ThrowingMutation_When_MutateAsync_Then_RethrowsAndRetainsException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var failingException = new InvalidOperationException("simulated");
            s_mutationFn = (_, _) => throw failingException;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "error-rethrow"));
            Exception? rethrown = null;

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException caught) { rethrown = caught; }
            mounted.FlushStateForTest();
            Assume.That(rethrown, Is.SameAs(failingException), "Precondition: MutateAsync rethrew the underlying exception");

            // Assert
            Assert.That(s_captured!.Error, Is.SameAs(failingException), "The handle retains the thrown exception");
        });

        [UnityTest]
        public IEnumerator Given_ThrowingMutation_When_FireAndForgetMutate_Then_DeliversOnErrorWithoutUnobservedException() => UniTask.ToCoroutine(async () =>
        {
            // Fire-and-forget Mutate reports a failure through onError / the Error status
            // only. Unlike MutateAsync it has no awaiter, so it must NOT rethrow — a rethrow on the .Forget()
            // path surfaces as an unobserved exception, which the test framework's implicit log check flags as
            // a failure (the RED signal here).
            // Arrange
            var failingException = new InvalidOperationException("simulated");
            s_mutationFn = (_, _) => throw failingException;
            s_onErrorCount = 0;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationWithOnErrorRender, key: "fire-and-forget-error"));

            // Act
            s_captured!.Mutate(1);
            await UniTask.Yield();
            await UniTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured!.Status, s_onErrorCount), Is.EqualTo((MutationStatus.Error, 1)),
                "Fire-and-forget Mutate routes the failure to onError and the Error status without an unobserved rethrow");
        });

        [UnityTest]
        public IEnumerator Given_SucceededMutation_When_Reset_Then_RestoresIdleState() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "reset"));
            await s_captured!.MutateAsync(10);
            mounted.FlushStateForTest();
            Assume.That(s_captured.Status, Is.EqualTo(MutationStatus.Success), "Precondition: the mutation succeeded before reset");

            // Act
            s_captured.Reset();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.Status, s_captured.Data, s_captured.Variables, s_captured.Error),
                Is.EqualTo((MutationStatus.Idle, default(int), default(int), (Exception?)null)),
                "Reset restores Idle and clears data, variables, and error");
        });

        [Test]
        public void Given_MountedMutation_When_ReRendered_Then_HandleReferenceIsStable()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "stable"));
            var firstHandle = s_captured;

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_captured, Is.SameAs(firstHandle), "UseMutation returns the same handle instance across renders");
        }

        [Test]
        public void Given_UnitGenerics_When_Mounted_Then_RendersSuccessfully()
        {
            // Arrange — Unit-based generic positions cover the "no variables / no return value" use case.

            // Act
            using var mounted = V.Mount(_root, V.Component(CaptureUnitMutationRender, key: "unit"));

            // Assert
            Assert.That(_root.Q<Label>(), Is.Not.Null, "A Unit-typed mutation hook compiles and mounts");
        }

        [UnityTest]
        public IEnumerator Given_VoidReturnOverload_When_MutateAsync_Then_CompletesWithUnitData() => UniTask.ToCoroutine(async () =>
        {
            // Arrange — the void-return overload adapts to a Unit result so the caller's function returns UniTask.
            var observedInput = 0;
            s_voidCaptured = null;
            using var mounted = V.Mount(_root, V.Component(() =>
            {
                s_voidCaptured = Hooks.UseMutation(new MutationOptions<int>(
                    MutationFn: (v, _) => { observedInput = v; return UniTask.CompletedTask; }));
                return V.Label(text: "void");
            }, key: "void"));

            // Act
            await s_voidCaptured!.MutateAsync(42);
            mounted.FlushStateForTest();
            Assume.That(observedInput, Is.EqualTo(42), "Precondition: the mutation function observed the input");

            // Assert
            Assert.That((s_voidCaptured.Status, s_voidCaptured.Data), Is.EqualTo((MutationStatus.Success, Unit.Default)),
                "A void-return mutation succeeds with Unit data");
        });

        [UnityTest]
        public IEnumerator Given_NoInputVoidOverload_When_MutateAsync_Then_RunsAndSucceeds() => UniTask.ToCoroutine(async () =>
        {
            // Arrange — the no-input void overload adapts to a Unit/Unit result; MutateAsync() takes no argument.
            var invoked = false;
            s_noInputCaptured = null;
            using var mounted = V.Mount(_root, V.Component(() =>
            {
                s_noInputCaptured = Hooks.UseMutation(new MutationOptions(
                    MutationFn: _ => { invoked = true; return UniTask.CompletedTask; }));
                return V.Label(text: "noinput");
            }, key: "noinput"));

            // Act
            await s_noInputCaptured!.MutateAsync();
            mounted.FlushStateForTest();
            Assume.That(invoked, Is.True, "Precondition: the no-input mutation function ran");

            // Assert
            Assert.That(s_noInputCaptured.Status, Is.EqualTo(MutationStatus.Success), "A no-input mutation succeeds");
        });

        [UnityTest]
        public IEnumerator Given_ConcurrentMutations_When_LatestCompletes_Then_FinalStateComesFromLatestCall() => UniTask.ToCoroutine(async () =>
        {
            // Arrange — both calls run; the observed result is the latest one's, and the first is not
            // cancelled out from under its caller. AttachExternalCancellation is what makes the first
            // term decisive: a completion source ignores the token, so without it the superseded call
            // returned 11 under the old cancelling behaviour too and the case could not fail.
            var first = new UniTaskCompletionSource<int>();
            var second = new UniTaskCompletionSource<int>();
            var callIndex = 0;
            s_mutationFn = (v, ct) =>
                (callIndex++ == 0 ? first.Task : second.Task).AttachExternalCancellation(ct);
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "concurrent"));

            // Act
            var firstTask = s_captured!.MutateAsync(1);
            var secondTask = s_captured!.MutateAsync(2);
            second.TrySetResult(99);
            var winnerResult = await secondTask;
            mounted.FlushStateForTest();
            first.TrySetResult(11);
            var firstResult = await firstTask;
            mounted.FlushStateForTest();

            // Assert — the first term is what separates running to completion from being cancelled; the rest
            // is the observed state, which stays the latest call's even though the earlier one settled after.
            Assert.That((firstResult, winnerResult, s_captured.Data, s_captured.Variables, s_captured.Status),
                Is.EqualTo((11, 99, 99, 2, MutationStatus.Success)),
                "Both calls complete; the latest determines the observed result, data, variables, and status");
        });

        [UnityTest]
        public IEnumerator Given_ASucceededMutation_When_AnotherStarts_Then_DataIsNotTheOlderResult() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var first = new UniTaskCompletionSource<int>();
            var second = new UniTaskCompletionSource<int>();
            var callIndex = 0;
            s_mutationFn = (v, ct) =>
                (callIndex++ == 0 ? first.Task : second.Task).AttachExternalCancellation(ct);
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "pending-data"));
            var firstTask = s_captured!.MutateAsync(1);
            first.TrySetResult(11);
            await firstTask;
            mounted.FlushStateForTest();
            var observedFirst = s_captured.Data;

            // Act
            var secondTask = s_captured.MutateAsync(2);
            await UniTask.Yield();
            mounted.FlushStateForTest();

            // Assert — Status alone would not catch it: a stale Data under a Pending status reads as
            // this call's result to anything rendering Data without checking Status first.
            Assert.That((observedFirst, s_captured.Status, s_captured.Data),
                Is.EqualTo((11, MutationStatus.Pending, 0)),
                "A newly started call shows no data until it has produced its own");
            second.TrySetResult(99);
            await secondTask;
        });

        [UnityTest]
        public IEnumerator Given_ASupersededMutation_When_ItSettles_Then_ItsOwnCallbackStillFires() => UniTask.ToCoroutine(async () =>
        {
            // Arrange — the failure this replaces: a double-tapped Buy cancelled the first request while the
            // server had already committed it, and the OnSuccess that writes the purchase never ran.
            var first = new UniTaskCompletionSource<int>();
            var second = new UniTaskCompletionSource<int>();
            var callIndex = 0;
            s_mutationFn = (v, ct) =>
                (callIndex++ == 0 ? first.Task : second.Task).AttachExternalCancellation(ct);
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRecordingSuccessesRender, key: "superseded-callback"));

            // Act
            var firstTask = s_captured!.MutateAsync(1);
            var secondTask = s_captured!.MutateAsync(2);
            second.TrySetResult(99);
            await secondTask;
            first.TrySetResult(11);
            await firstTask;
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_delivered, Is.EquivalentTo(new[] { 99, 11 }),
                "Every call delivers its own success, whether or not a later one has already settled");
        });

        [UnityTest]
        public IEnumerator Given_TwoMutationsInFlight_When_TheComponentUnmounts_Then_TheUnmountCompletes() => UniTask.ToCoroutine(async () =>
        {
            // Arrange — two live sources, where cancelling the first settles the second. Unmount clears the
            // list before cancelling, so the second's own finally finds nothing to remove; disposing there
            // anyway would leave the loop cancelling a source it had already disposed. One call cannot
            // reach that, since disposing an already-disposed source returns without complaint.
            var first = new UniTaskCompletionSource<int>();
            var second = new UniTaskCompletionSource<int>();
            var callIndex = 0;
            s_mutationFn = (v, ct) =>
            {
                var mine = callIndex++ == 0 ? first.Task : second.Task;
                ct.Register(() => second.TrySetResult(0));
                return mine.AttachExternalCancellation(ct);
            };
            var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "two-in-flight"));
            var firstCall = s_captured!.MutateAsync(1).SuppressCancellationThrow();
            var secondCall = s_captured.MutateAsync(2).SuppressCancellationThrow();
            await UniTask.Yield();
            var bothStarted = callIndex;

            // Act
            Exception? thrown = null;
            try { mounted.Dispose(); } catch (Exception ex) { thrown = ex; }

            // Assert — the first term keeps the reading load-bearing: with fewer than two calls in flight
            // the disposal order this pins is never exercised.
            Assert.That((bothStarted, thrown), Is.EqualTo((2, (Exception?)null)));
            await firstCall;
            await secondCall;
        });

        [UnityTest]
        public IEnumerator Given_AMutationInFlight_When_TheComponentUnmounts_Then_TheUnmountCompletes() => UniTask.ToCoroutine(async () =>
        {
            // Arrange — a token honoured by the mutation resumes its continuation inside Cancel(), and the
            // continuation's finally reaches back into the list Dispose is walking.
            var pending = new UniTaskCompletionSource<int>();
            s_mutationFn = (v, ct) => pending.Task.AttachExternalCancellation(ct);
            var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "unmount-reentrancy"));
            var inFlight = s_captured!.MutateAsync(1).SuppressCancellationThrow();
            await UniTask.Yield();
            var startedPending = s_captured.Status;

            // Act
            Exception? thrown = null;
            try { mounted.Dispose(); } catch (Exception ex) { thrown = ex; }

            // Assert — the first term keeps the reading load-bearing: an unmount with nothing in flight
            // cannot throw, so a call that never started would pass on the second term alone. An
            // exception out of here aborts the enclosing reconcile, not just this hook.
            Assert.That((startedPending, thrown),
                Is.EqualTo((MutationStatus.Pending, (Exception?)null)));
            await inFlight;
        });

        [UnityTest]
        public IEnumerator Given_InFlightMutation_When_ComponentUnmounted_Then_CallerObservesResult() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var gate = new UniTaskCompletionSource<int>();
            s_mutationFn = (_, _) => gate.Task;
            var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "unmount-observe"));
            var captured = s_captured!;
            var inflight = captured.MutateAsync(7);

            // Act
            mounted.Dispose(); // unmount while in flight
            gate.TrySetResult(42);

            // Assert — the caller's await still observes the function result regardless of fiber lifecycle
            var observed = await inflight;
            Assert.That(observed, Is.EqualTo(42), "The caller's MutateAsync await observes the function result after unmount");
        });

        [UnityTest]
        public IEnumerator Given_SucceededMutation_When_OnSuccessThrows_Then_StatusIsError() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            s_onSuccessThrowException = new InvalidOperationException("onSuccess");
            using var mounted = V.Mount(_root, V.Component(CaptureMutationWithThrowingOnSuccessRender, key: "onsuccess-status"));

            // Act
            try { await s_captured!.MutateAsync(21); } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_captured!.Status, Is.EqualTo(MutationStatus.Error),
                "A throwing onSuccess handler transitions the slot to Error");
        });

        [UnityTest]
        public IEnumerator Given_SucceededMutation_When_OnSuccessThrows_Then_ResultErrorIsHandlerException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            s_onSuccessThrowException = new InvalidOperationException("onSuccess");
            using var mounted = V.Mount(_root, V.Component(CaptureMutationWithThrowingOnSuccessRender, key: "onsuccess-error"));
            Exception? rethrown = null;

            // Act
            try { await s_captured!.MutateAsync(21); } catch (InvalidOperationException caught) { rethrown = caught; }
            mounted.FlushStateForTest();
            Assume.That(rethrown, Is.SameAs(s_onSuccessThrowException),
                "Precondition: MutateAsync rethrew the onSuccess exception");

            // Assert
            Assert.That(s_captured!.Error, Is.SameAs(s_onSuccessThrowException),
                "The handle retains the onSuccess exception as Error");
        });

        [UnityTest]
        public IEnumerator Given_SucceededMutation_When_OnSuccessThrows_Then_OnErrorRuns() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            s_onSuccessThrowException = new InvalidOperationException("onSuccess");
            s_onErrorCount = 0;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationWithThrowingOnSuccessRender, key: "onsuccess-onerror"));

            // Act
            try { await s_captured!.MutateAsync(21); } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_onErrorCount, Is.EqualTo(1),
                "A throwing onSuccess handler routes the exception through onError");
        });

        [UnityTest]
        public IEnumerator Given_SucceededMutation_When_OnSuccessThrows_Then_MutateAsyncRethrowsHandlerException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            s_onSuccessThrowException = new InvalidOperationException("onSuccess");
            using var mounted = V.Mount(_root, V.Component(CaptureMutationWithThrowingOnSuccessRender, key: "onsuccess-rethrow"));
            Exception? rethrown = null;

            // Act
            try { await s_captured!.MutateAsync(21); } catch (InvalidOperationException caught) { rethrown = caught; }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(rethrown, Is.SameAs(s_onSuccessThrowException),
                "MutateAsync rethrows the onSuccess exception instead of returning the mutation data");
        });

        [UnityTest]
        public IEnumerator Given_SucceededMutation_When_OnSuccessThrowsOnFireAndForgetMutate_Then_OnErrorRunsWithoutUnobservedException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            s_onSuccessThrowException = new InvalidOperationException("onSuccess");
            s_onErrorCount = 0;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationWithThrowingOnSuccessRender, key: "onsuccess-forget"));

            // Act
            s_captured!.Mutate(21);
            await UniTask.Yield();
            await UniTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured!.Status, s_onErrorCount), Is.EqualTo((MutationStatus.Error, 1)),
                "Fire-and-forget Mutate routes a throwing onSuccess through onError without an unobserved rethrow");
        });

        [UnityTest]
        public IEnumerator Given_FailingMutation_When_OnErrorThrows_Then_StatusRemainsErrorWithMutationException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var failingException = new InvalidOperationException("mutation");
            s_onErrorThrowException = new InvalidOperationException("onError");
            s_mutationFn = (_, _) => throw failingException;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationWithThrowingOnErrorRender, key: "onerror-status"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: onError"));

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_captured!.Error, Is.SameAs(failingException),
                "A throwing onError handler does not replace the mutation error stored on the slot");
        });

        [UnityTest]
        public IEnumerator Given_FailingMutation_When_OnErrorThrowsOnFireAndForgetMutate_Then_LogsWithoutDisturbingOutcome() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var failingException = new InvalidOperationException("mutation");
            s_onErrorThrowException = new InvalidOperationException("onError");
            s_mutationFn = (_, _) => throw failingException;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationWithThrowingOnErrorRender, key: "onerror-forget"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: onError"));

            // Act
            s_captured!.Mutate(1);
            await UniTask.Yield();
            await UniTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured!.Status, ReferenceEquals(s_captured.Error, failingException)),
                Is.EqualTo((MutationStatus.Error, true)),
                "A throwing onError handler is logged and does not replace the mutation outcome on the slot");
        });

        [UnityTest]
        public IEnumerator Given_FailingMutation_When_OnErrorThrows_Then_MutateAsyncRethrowsMutationException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var failingException = new InvalidOperationException("mutation");
            s_onErrorThrowException = new InvalidOperationException("onError");
            s_mutationFn = (_, _) => throw failingException;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationWithThrowingOnErrorRender, key: "onerror-rethrow"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: onError"));
            Exception? rethrown = null;

            // Act
            try { await s_captured!.MutateAsync(1); } catch (InvalidOperationException caught) { rethrown = caught; }
            mounted.FlushStateForTest();

            // Assert
            Assert.That(rethrown, Is.SameAs(failingException),
                "MutateAsync rethrows the mutation exception even when onError throws");
        });

        [UnityTest]
        public IEnumerator Given_InFlightMutation_When_ComponentUnmounted_Then_DisposedFiberNotSetToSuccess() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var gate = new UniTaskCompletionSource<int>();
            s_mutationFn = (_, _) => gate.Task;
            var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "unmount-nostate"));
            var captured = s_captured!;
            var inflight = captured.MutateAsync(7);

            // Act
            mounted.Dispose(); // unmount while in flight
            gate.TrySetResult(42);
            await inflight;

            // Assert
            Assert.That(captured.Status, Is.Not.EqualTo(MutationStatus.Success),
                "A disposed fiber does not receive a Success state transition");
        });

        [Component]
        public static VNode CaptureMutationRender()
        {
            s_captured = Hooks.UseMutation(new MutationOptions<int, int>(MutationFn: s_mutationFn));
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode CaptureMutationRecordingSuccessesRender()
        {
            s_captured = Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: s_mutationFn,
                OnSuccess: (data, _) => s_delivered.Add(data)));
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode CaptureMutationWithOnErrorRender()
        {
            s_captured = Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: s_mutationFn,
                OnError: (_, _) => s_onErrorCount++));
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode CaptureMutationWithThrowingOnSuccessRender()
        {
            s_captured = Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: s_mutationFn,
                OnSuccess: (_, _) => throw s_onSuccessThrowException!,
                OnError: (_, _) => s_onErrorCount++));
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode CaptureMutationWithThrowingOnErrorRender()
        {
            s_captured = Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: s_mutationFn,
                OnError: (ex, _) =>
                {
                    s_onErrorCount++;
                    throw s_onErrorThrowException!;
                }));
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode CaptureUnitMutationRender()
        {
            _ = Hooks.UseMutation(new MutationOptions<Unit, Unit>(
                MutationFn: (_, _) => UniTask.FromResult(Unit.Default)));
            return V.Label(text: "ok");
        }

        private static readonly System.Collections.Generic.List<int> s_delivered = new();

        private static int s_onErrorCount;
        private static MutationResult<int, Unit>? s_voidCaptured;
        private static MutationResult<Unit, Unit>? s_noInputCaptured;
        private static Exception? s_onSuccessThrowException;
        private static Exception? s_onErrorThrowException;
    }
}
