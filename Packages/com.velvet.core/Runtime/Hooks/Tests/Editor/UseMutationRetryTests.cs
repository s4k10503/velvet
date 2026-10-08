using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
#if UNITY_EDITOR
using static Velvet.TestUtilities.VelvetTaskFrameDriverTestExtensions;
#endif

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="MutationOptions{TVariables, TData}.Retry"/> on a mounted
    /// <see cref="Hooks.UseMutation{TVariables, TData}"/>: retries stay inside one call, so the call commits
    /// and delivers one outcome; a retry waits on the token unmount cancels; a call still retrying does not
    /// publish over a newer one; and the latest render's <c>Retry</c> and <c>MutationFn</c> are the ones used.
    /// <see cref="RetryPolicyTests"/> owns the policy's own schedule.
    /// </summary>
    [TestFixture]
    internal sealed class UseMutationRetryTests
    {
        private VisualElement _root = null!;
        private static MutationResult<int, int>? s_captured;
        private static MutationResult<int, Unit>? s_voidCaptured;
        private static MutationResult<Unit, Unit>? s_noInputCaptured;
        private static RetryPolicy? s_retry;
        private static Func<int, CancellationToken, VelvetTask<int>> s_mutationFn = (v, _) => VelvetTask.FromResult(v);
        private static int s_onErrorCount;
        private static readonly List<int> s_delivered = new();

        // Both signals supplied, so no case reads the Application's own focus or connectivity.
        private static readonly RetryPolicy s_ready = new() { IsOnline = () => true, IsFocused = () => true };

        private static readonly RetryPolicy s_noDelay = s_ready with { RetryDelay = TimeSpan.Zero };

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_captured = null;
            s_voidCaptured = null;
            s_noInputCaptured = null;
            s_retry = null;
            s_mutationFn = (v, _) => VelvetTask.FromResult(v);
            s_onErrorCount = 0;
            s_delivered.Clear();
        }

        [UnityTest]
        public IEnumerator Given_ARetryPolicy_When_MutationFnFailsOnceThenSucceeds_Then_TheCallSucceedsWithoutOnError() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var attempts = 0;
            s_retry = s_noDelay;
            s_mutationFn = (v, _) =>
                ++attempts == 1 ? throw new InvalidOperationException("transient") : VelvetTask.FromResult(v * 2);
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-success"));

            // Act
            await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.Status, s_captured.Data, s_onErrorCount, attempts),
                Is.EqualTo((MutationStatus.Success, 42, 0, 2)),
                "A failure the policy retries is not the call's outcome: the second attempt's result is");
        });

        [Test]
        public void Given_ARetryWaiting_When_TheFirstAttemptHasFailed_Then_TheCallIsStillPendingWithNoErrorDelivered()
        {
            // Arrange
            var gate = new VelvetTaskCompletionSource();
            s_retry = s_ready with { Wait = (_, _) => gate.Task };
            s_mutationFn = (_, _) => throw new InvalidOperationException("transient");
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-pending"));

            // Act
            _ = s_captured!.MutateAsync(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.Status, s_captured.Error, s_onErrorCount),
                Is.EqualTo((MutationStatus.Pending, (Exception?)null, 0)),
                "Between attempts the handle shows the call pending and OnError has not run");
        }

        [Test]
        public void Given_ARetryWaiting_When_TheComponentUnmounts_Then_TheTokenItWaitsOnIsCancelled()
        {
            // Arrange
            var waitedOn = default(CancellationToken);
            var gate = new VelvetTaskCompletionSource();
            s_retry = s_ready with
            {
                Wait = (_, token) =>
                {
                    waitedOn = token;
                    return gate.Task;
                },
            };
            s_mutationFn = (_, _) => throw new InvalidOperationException("transient");
            var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-unmount"));
            _ = s_captured!.MutateAsync(1);

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(waitedOn.IsCancellationRequested, Is.True,
                "The retry waits on the call's own token, which unmount cancels");
        }

        [UnityTest]
        public IEnumerator Given_AnOlderCallRetrying_When_ANewerCallSucceedsFirst_Then_TheOlderRetryDoesNotPublishOverIt() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the older call fails once and waits on the gate; the newer one succeeds at once.
            var gate = new VelvetTaskCompletionSource();
            var olderAttempts = 0;
            s_retry = s_ready with { Wait = (_, _) => gate.Task };
            s_mutationFn = (v, _) =>
                v == 1 && ++olderAttempts == 1 ? throw new InvalidOperationException("transient") : VelvetTask.FromResult(v * 2);
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRecordingSuccessesRender, key: "retry-overlap"));
            var older = s_captured!.MutateAsync(1);
            await s_captured.MutateAsync(2);

            // Act
            gate.TrySetResult();
            await older;
            mounted.FlushStateForTest();

            // Assert — the first term is the older call's retry landing, without which the handle keeps the
            // newer outcome whatever the retry does.
            Assert.That((string.Join(",", s_delivered), s_captured.Data, s_captured.Variables),
                Is.EqualTo(("4,2", 4, 2)),
                "The older call's retried success is delivered to its own OnSuccess and not to the handle");
        });

        [UnityTest]
        public IEnumerator Given_ARetryPolicyGivenOnAReRender_When_MutationFnFailsOnce_Then_TheCallRetries() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — mounted without a policy, so only the re-render's options can supply one.
            var attempts = 0;
            s_mutationFn = (v, _) =>
                ++attempts == 1 ? throw new InvalidOperationException("transient") : VelvetTask.FromResult(v * 2);
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-rerender"));
            s_retry = s_noDelay;
            mounted.Render(V.Component(CaptureMutationRender, key: "retry-rerender"));

            // Act
            await s_captured!.MutateAsync(21);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_captured.Status, Is.EqualTo(MutationStatus.Success),
                "A Retry the latest render passed is the one the next call uses");
        });

        [UnityTest]
        public IEnumerator Given_AReRenderDuringARetryWait_When_TheRetryRuns_Then_ItCallsTheNewMutationFn() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the first render's function always fails, so only the re-render's can succeed.
            var gate = new VelvetTaskCompletionSource();
            s_retry = s_ready with { Wait = (_, _) => gate.Task };
            s_mutationFn = (_, _) => throw new InvalidOperationException("the first render's function");
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-latest-fn"));
            var call = s_captured!.MutateAsync(1);
            s_mutationFn = (v, _) => VelvetTask.FromResult(v * 100);
            mounted.Render(V.Component(CaptureMutationRender, key: "retry-latest-fn"));

            // Act
            gate.TrySetResult();
            var result = await call;

            // Assert
            Assert.That(result, Is.EqualTo(100), "The retry calls the MutationFn of the latest render, as v5 does");
        });

        [UnityTest]
        public IEnumerator Given_AVoidMutationWithRetry_When_ItFailsOnce_Then_ItSucceeds() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var attempts = 0;
            s_retry = s_noDelay;
            s_mutationFn = (v, _) =>
                ++attempts == 1 ? throw new InvalidOperationException("transient") : VelvetTask.FromResult(v);
            using var mounted = V.Mount(_root, V.Component(CaptureVoidMutationRender, key: "retry-void"));

            // Act
            await s_voidCaptured!.MutateAsync(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_voidCaptured.Status, Is.EqualTo(MutationStatus.Success),
                "The void overload hands its Retry to the call it adapts to");
        });

        [UnityTest]
        public IEnumerator Given_ANoInputMutationWithRetry_When_ItFailsOnce_Then_ItSucceeds() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var attempts = 0;
            s_retry = s_noDelay;
            s_mutationFn = (v, _) =>
                ++attempts == 1 ? throw new InvalidOperationException("transient") : VelvetTask.FromResult(v);
            using var mounted = V.Mount(_root, V.Component(CaptureNoInputMutationRender, key: "retry-no-input"));

            // Act
            await s_noInputCaptured!.MutateAsync();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_noInputCaptured.Status, Is.EqualTo(MutationStatus.Success),
                "The no-input overload hands its Retry to the call it adapts to");
        });

        [Test]
        public void Given_ARetryWaiting_When_TheFirstAttemptHasFailed_Then_TheHandleShowsTheFailureCountAndReason()
        {
            // Arrange
            var failure = new InvalidOperationException("transient");
            var gate = new VelvetTaskCompletionSource();
            s_retry = s_ready with { Wait = (_, _) => gate.Task };
            s_mutationFn = (_, _) => throw failure;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-failure-count"));

            // Act
            _ = s_captured!.MutateAsync(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.FailureCount, s_captured.FailureReason), Is.EqualTo((1, (Exception)failure)),
                "failureCount and failureReason follow each failed attempt a retry is waiting on");
        }

        [UnityTest]
        public IEnumerator Given_ARetryPolicyOfThree_When_EveryAttemptFails_Then_TheHandleCountsFourFailuresAndKeepsTheLastReason() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var failure = new InvalidOperationException("down");
            s_retry = s_noDelay;
            s_mutationFn = (_, _) => throw failure;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-exhausted"));

            // Act
            try { await s_captured!.MutateAsync(1); }
            catch (InvalidOperationException) { }
            mounted.FlushStateForTest();

            // Assert — three retries are three failed attempts counted as they happen, and the call's own
            // failure is the fourth, as the mutation reducer adds one to the count on 'error'.
            Assert.That((s_captured.Status, s_captured.FailureCount, s_captured.FailureReason),
                Is.EqualTo((MutationStatus.Error, 4, (Exception)failure)),
                "The final failure counts with the retried ones");
        });

        [UnityTest]
        public IEnumerator Given_AFailedAttempt_When_ARetrySucceeds_Then_TheHandleClearsTheFailureCountAndReason() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var attempts = 0;
            s_retry = s_noDelay;
            s_mutationFn = (v, _) =>
                ++attempts == 1 ? throw new InvalidOperationException("transient") : VelvetTask.FromResult(v);
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-failure-cleared"));

            // Act
            await s_captured!.MutateAsync(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.FailureCount, s_captured.FailureReason), Is.EqualTo((0, (Exception?)null)),
                "A success resets the counters, as the mutation reducer does");
        });

        [UnityTest]
        public IEnumerator Given_AFailedCall_When_TheHandleIsReset_Then_TheFailureCountAndReasonAreCleared() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mutationFn = (_, _) => throw new InvalidOperationException("down");
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-reset"));
            try { await s_captured!.MutateAsync(1); }
            catch (InvalidOperationException) { }

            // Act
            s_captured!.Reset();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.FailureCount, s_captured.FailureReason), Is.EqualTo((0, (Exception?)null)),
                "Reset clears the failure bookkeeping with the rest of the handle");
        });

        [Test]
        public void Given_NoRetryPolicy_When_TheCallFails_Then_TheHandleCountsOneFailure()
        {
            // Arrange
            var failure = new InvalidOperationException("down");
            s_mutationFn = (_, _) => throw failure;
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-none-failed"));

            // Act
            s_captured!.Mutate(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.FailureCount, s_captured.FailureReason), Is.EqualTo((1, (Exception)failure)),
                "A failure without a retry is v5's retry: 0 case, whose reducer also counts it");
        }

#if UNITY_EDITOR
        [Test]
        public void Given_ARetryPolicyOffline_When_TheCallStarts_Then_TheHandleIsPausedAndPending()
        {
            // Arrange
            var attempts = 0;
            s_retry = s_ready with { IsOnline = () => false };
            s_mutationFn = (v, _) => VelvetTask.FromResult(v + ++attempts);
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-paused-start"));

            // Act
            _ = s_captured!.MutateAsync(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.IsPaused, s_captured.Status, attempts), Is.EqualTo((true, MutationStatus.Pending, 0)),
                "A mutation started offline is paused, pending and has not called MutationFn");
        }

        [Test]
        public void Given_APausedCall_When_TheDeviceComesOnline_Then_TheCallRunsAndTheHandleIsNoLongerPaused()
        {
            // Arrange
            var online = false;
            s_retry = s_ready with { IsOnline = () => online };
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-paused-resume"));
            _ = s_captured!.MutateAsync(1);

            // Act
            online = true;
            for (var frame = 0; frame < 5; frame++) DrainEditorUpdateForTest();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.IsPaused, s_captured.Status), Is.EqualTo((false, MutationStatus.Success)),
                "Coming online continues the call and clears IsPaused");
        }

        [Test]
        public void Given_ARetryDueWhileUnfocused_When_FramesPass_Then_TheHandleIsPausedWithItsFailureCount()
        {
            // Arrange
            var attempts = 0;
            s_retry = s_noDelay with { IsFocused = () => false };
            s_mutationFn = (v, _) =>
                ++attempts == 1 ? throw new InvalidOperationException("transient") : VelvetTask.FromResult(v);
            using var mounted = V.Mount(_root, V.Component(CaptureMutationRender, key: "retry-paused-focus"));
            _ = s_captured!.MutateAsync(1);

            // Act
            for (var frame = 0; frame < 5; frame++) DrainEditorUpdateForTest();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_captured.IsPaused, s_captured.FailureCount, s_captured.Status, attempts),
                Is.EqualTo((true, 1, MutationStatus.Pending, 1)),
                "A retry that is due while the application is unfocused waits, and the handle says so");
        }
#endif

        [Component]
        public static VNode CaptureMutationRender()
        {
            s_captured = Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: s_mutationFn,
                OnError: (_, _) => s_onErrorCount++) { Retry = s_retry });
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode CaptureMutationRecordingSuccessesRender()
        {
            s_captured = Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: s_mutationFn,
                OnSuccess: (data, _) => s_delivered.Add(data)) { Retry = s_retry });
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode CaptureVoidMutationRender()
        {
            s_voidCaptured = Hooks.UseMutation(new MutationOptions<int>(
                MutationFn: async (v, ct) => await s_mutationFn(v, ct)) { Retry = s_retry });
            return V.Label(text: "ok");
        }

        [Component]
        public static VNode CaptureNoInputMutationRender()
        {
            s_noInputCaptured = Hooks.UseMutation(new MutationOptions(
                MutationFn: async ct => await s_mutationFn(0, ct)) { Retry = s_retry });
            return V.Label(text: "ok");
        }
    }
}
