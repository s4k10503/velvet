using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using static Velvet.TestUtilities.VelvetTaskFrameDriverTestExtensions;
#endif

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="RetryPolicy"/>: how many attempts it makes, what it waits between them, which
    /// failures it declines, and that a cancelled caller gets no further attempt. No case waits for
    /// wall-clock time to pass: one that runs the policy supplies a <see cref="RetryPolicy.Wait"/> or a zero
    /// delay, or drains frames under a delay of an hour.
    /// </summary>
    [TestFixture]
    internal sealed class RetryPolicyTests
    {
        private static readonly RetryPolicy s_noDelay = new() { RetryDelay = (_, _) => TimeSpan.Zero };

        // Ten failures and then a result, rather than failing forever: a policy that lost its bound then
        // returns, which the case reads as red, instead of retrying without end on a wait that completes at once.
        private static Func<CancellationToken, VelvetTask<int>> FailingTenTimes(List<Exception> thrown) => _ =>
        {
            if (thrown.Count == 10)
            {
                return VelvetTask.FromResult(thrown.Count);
            }

            var failure = new InvalidOperationException("attempt " + (thrown.Count + 1));
            thrown.Add(failure);
            throw failure;
        };

        [UnityTest]
        public IEnumerator Given_AnOperationFailingTwice_When_RunWithThreeRetries_Then_ItReturnsTheThirdAttemptsResult() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var attempts = 0;
            VelvetTask<int> Operation(CancellationToken _) =>
                ++attempts < 3 ? throw new InvalidOperationException("transient") : VelvetTask.FromResult(7);

            // Act
            var result = await s_noDelay.RunAsync(Operation);

            // Assert
            Assert.That((result, attempts), Is.EqualTo((7, 3)),
                "Two failures within three retries end in the third attempt's result");
        });

        [UnityTest]
        public IEnumerator Given_AnOperationFailingTenTimes_When_RunWithTwoRetries_Then_TheThirdFailureIsRethrown() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var policy = s_noDelay with { MaxRetries = 2 };
            var thrown = new List<Exception>();
            Exception? caught = null;

            // Act
            try { await policy.RunAsync(FailingTenTimes(thrown)); }
            catch (InvalidOperationException failure) { caught = failure; }

            // Assert — the identity term separates the last failure from the first, which a rethrow of
            // whatever was caught first would hand back instead.
            Assert.That((thrown.Count, ReferenceEquals(caught, thrown.LastOrDefault())), Is.EqualTo((3, true)),
                "Two retries make three attempts, and the caller receives the last attempt's failure");
        });

        [UnityTest]
        public IEnumerator Given_TheDefaultDelay_When_AnOperationFailsTenTimes_Then_TheWaitsDoubleFromOneSecond() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var waits = new List<TimeSpan>();
            var policy = new RetryPolicy
            {
                Wait = (delay, _) =>
                {
                    waits.Add(delay);
                    return VelvetTask.CompletedTask;
                },
            };

            // Act
            try { await policy.RunAsync(FailingTenTimes(new List<Exception>())); }
            catch (InvalidOperationException) { }

            // Assert — three waits is the default MaxRetries as well, since one wait precedes each retry.
            Assert.That(string.Join(",", waits), Is.EqualTo("00:00:01,00:00:02,00:00:04"),
                "The default policy waits 1 s, 2 s and 4 s before its three retries");
        });

        [Test]
        public void Given_ManyFailures_When_TheDefaultDelayIsRead_Then_ItIsCappedAtThirtySeconds()
        {
            // Act
            var delay = RetryPolicy.DefaultRetryDelay(10);

            // Assert
            Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(30)), "The doubling stops at thirty seconds");
        }

        [UnityTest]
        public IEnumerator Given_ACustomRetryDelay_When_AnOperationFailsTenTimes_Then_ItIsWaitedForEachFailureCount() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var waits = new List<TimeSpan>();
            var policy = new RetryPolicy
            {
                MaxRetries = 2,
                RetryDelay = (failureCount, _) => TimeSpan.FromMilliseconds(10 * (failureCount + 1)),
                Wait = (delay, _) =>
                {
                    waits.Add(delay);
                    return VelvetTask.CompletedTask;
                },
            };

            // Act
            try { await policy.RunAsync(FailingTenTimes(new List<Exception>())); }
            catch (InvalidOperationException) { }

            // Assert
            Assert.That(string.Join(",", waits.Select(wait => wait.TotalMilliseconds)), Is.EqualTo("10,20"),
                "RetryDelay is asked with failure counts 0 and 1, and what it returns is what is waited");
        });

        [UnityTest]
        public IEnumerator Given_AShouldRetryAcceptingOnlyTheFirstFailure_When_EveryAttemptFails_Then_ItRunsTwice() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var asked = new List<int>();
            var policy = s_noDelay with
            {
                ShouldRetry = (failureCount, _) =>
                {
                    asked.Add(failureCount);
                    return failureCount == 0;
                },
            };

            // Act
            try { await policy.RunAsync<int>(_ => throw new InvalidOperationException("down")); }
            catch (InvalidOperationException) { }

            // Assert — two attempts, so the predicate was asked after each and declined the second.
            Assert.That(string.Join(",", asked), Is.EqualTo("0,1"),
                "ShouldRetry is asked with each failure's count, and a refusal ends the retries");
        });

        [UnityTest]
        public IEnumerator Given_AShouldRetryAcceptingMoreThanMaxRetries_When_EveryAttemptFails_Then_MaxRetriesStillBoundsIt() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the predicate accepts four failures rather than every one, so a policy that let it
            // override the bound ends after five attempts instead of retrying forever.
            var attempts = 0;
            var policy = s_noDelay with { MaxRetries = 1, ShouldRetry = (failureCount, _) => failureCount < 4 };

            // Act
            try
            {
                await policy.RunAsync<int>(_ =>
                {
                    attempts++;
                    throw new InvalidOperationException("down");
                });
            }
            catch (InvalidOperationException) { }

            // Assert
            Assert.That(attempts, Is.EqualTo(2), "A predicate accepting more failures than MaxRetries still gets one retry");
        });

        [UnityTest]
        public IEnumerator Given_AnOperationThatThrowsItsOwnCancellation_When_Run_Then_ItIsNotRetried() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the operation's cancellation is its own: the token the policy hands it is never
            // cancelled, so only the exception's type can decline the retry.
            var attempts = 0;

            // Act
            try
            {
                await s_noDelay.RunAsync<int>(_ =>
                {
                    attempts++;
                    throw new OperationCanceledException("aborted by the caller's own source");
                });
            }
            catch (OperationCanceledException) { }

            // Assert
            Assert.That(attempts, Is.EqualTo(1), "An OperationCanceledException ends the operation at its first attempt");
        });

        [UnityTest]
        public IEnumerator Given_AWaitThatIgnoresTheToken_When_CancelledDuringIt_Then_NoFurtherAttemptStarts() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var gate = new VelvetTaskCompletionSource();
            var attempts = 0;
            var policy = new RetryPolicy { Wait = (_, _) => gate.Task };
            var running = policy.RunAsync<int>(_ =>
            {
                attempts++;
                throw new InvalidOperationException("down");
            }, cts.Token);

            // Act
            cts.Cancel();
            gate.TrySetResult();
            var cancelled = false;
            try { await running; } catch (OperationCanceledException) { cancelled = true; }

            // Assert
            Assert.That((attempts, cancelled), Is.EqualTo((1, true)),
                "A caller cancelled during the wait gets no second attempt, and its run rejects as cancelled");
        });

#if UNITY_EDITOR
        [Test]
        public void Given_TheDefaultWait_When_CancelledDuringIt_Then_TheRunRejectsAsCancelledOnceFramesPass()
        {
            // Arrange — an hour, so the wait cannot end on its own while the frames below are drained.
            using var cts = new CancellationTokenSource();
            var policy = new RetryPolicy { RetryDelay = (_, _) => TimeSpan.FromHours(1) };
            var running = policy.RunAsync<int>(_ => throw new InvalidOperationException("down"), cts.Token);

            // Act
            cts.Cancel();
            DrainEditorUpdateForTest();
            DrainEditorUpdateForTest();

            // Assert
            Assert.That(running.Status, Is.EqualTo(VelvetTaskStatus.Canceled),
                "The default wait checks the token each frame and rejects once it is cancelled");
        }
#endif

        [Test]
        public void Given_ANullOperation_When_Run_Then_ItThrowsBeforeReturningATask()
        {
            // Act
            TestDelegate run = () => new RetryPolicy().RunAsync<int>(null!);

            // Assert
            Assert.That(run, Throws.ArgumentNullException, "A null operation is refused at the call");
        }
    }
}
