using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

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

        private static readonly RetryPolicy s_noDelay = new() { RetryDelay = (_, _) => TimeSpan.Zero };

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
            s_retry = new RetryPolicy { Wait = (_, _) => gate.Task };
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
            s_retry = new RetryPolicy
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
            s_retry = new RetryPolicy { Wait = (_, _) => gate.Task };
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
            s_retry = new RetryPolicy { Wait = (_, _) => gate.Task };
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
