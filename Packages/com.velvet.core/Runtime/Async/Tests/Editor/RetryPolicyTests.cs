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
        // returns, which the case reads as red, instead of retrying until the runner's timeout.
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
            var policy = s_noDelay with { Retry = 2 };
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

            // Assert — three waits is the default Retry count as well, since one wait precedes each retry.
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
                Retry = 2,
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
        public IEnumerator Given_AWhenAcceptingOnlyTheFirstFailure_When_EveryAttemptFails_Then_ItRunsTwice() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var asked = new List<int>();
            var policy = s_noDelay with
            {
                Retry = RetryRule.When((failureCount, _) =>
                {
                    asked.Add(failureCount);
                    return failureCount == 0;
                }),
            };

            // Act
            try { await policy.RunAsync<int>(_ => throw new InvalidOperationException("down")); }
            catch (InvalidOperationException) { }

            // Assert — two attempts, so the predicate was asked after each and declined the second.
            Assert.That(string.Join(",", asked), Is.EqualTo("0,1"),
                "The function is asked with each failure's count, and a refusal ends the retries");
        });

        [UnityTest]
        public IEnumerator Given_AWhenAcceptingFourFailures_When_EveryAttemptFails_Then_ItRunsFiveTimesPastTheDefaultCount() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the function accepts four failures rather than every one, so a policy that kept the
            // default count of three beside it ends after four attempts, and one that lost the function's
            // bound retries until the operation stops failing.
            var attempts = 0;
            var policy = s_noDelay with { Retry = RetryRule.When((failureCount, _) => failureCount < 4) };

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
            Assert.That(attempts, Is.EqualTo(5), "A function replaces the count, as TanStack's retry function does");
        });

        [UnityTest]
        public IEnumerator Given_AWhen_When_AnOperationFails_Then_ItIsAskedWithTheFailureItself() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var thrown = new List<Exception>();
            var asked = new List<Exception>();
            var policy = s_noDelay with
            {
                Retry = RetryRule.When((_, error) =>
                {
                    asked.Add(error);
                    return false;
                }),
            };

            // Act
            try { await policy.RunAsync(FailingTenTimes(thrown)); }
            catch (InvalidOperationException) { }

            // Assert
            Assert.That(asked, Is.EqualTo(thrown), "The function receives the exception the attempt threw");
        });

        [UnityTest]
        public IEnumerator Given_RetryTrue_When_AnOperationFailsTenTimes_Then_ItIsRetriedUntilItSucceeds() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — ten failures exceed the default count of three, so only a rule without a bound
            // reaches the eleventh attempt's result.
            var thrown = new List<Exception>();
            var policy = s_noDelay with { Retry = true };

            // Act
            var result = await policy.RunAsync(FailingTenTimes(thrown));

            // Assert
            Assert.That((result, thrown.Count), Is.EqualTo((10, 10)), "true retries every failure, as TanStack's retry: true does");
        });

        [UnityTest]
        public IEnumerator Given_RetryFalse_When_AnOperationFails_Then_ItIsNotRetried() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var attempts = 0;
            var policy = s_noDelay with { Retry = false };

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
            Assert.That(attempts, Is.EqualTo(1), "false retries none, as TanStack's retry: false does");
        });

        [UnityTest]
        public IEnumerator Given_RetryZero_When_AnOperationFails_Then_ItIsNotRetried() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var attempts = 0;
            var policy = s_noDelay with { Retry = 0 };

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
            Assert.That(attempts, Is.EqualTo(1), "A count of zero retries none, as TanStack's retry: 0 does");
        });

        [Test]
        public void Given_ANullFunction_When_RetryRuleWhenIsCalled_Then_ItThrows()
        {
            // Act
            TestDelegate create = () => RetryRule.When(null!);

            // Assert
            Assert.That(create, Throws.ArgumentNullException, "A rule with no function is refused where it is made");
        }

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

        [UnityTest]
        public IEnumerator Given_ATokenCancelledBeforeAFailure_When_TheOperationFails_Then_TheFailurePropagatesAsItself() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the operation cancels its caller's token and then fails with an error of its own, so
            // the failure is neither an OperationCanceledException nor a cancellation the wait could report.
            using var cts = new CancellationTokenSource();
            var failure = new InvalidOperationException("failed after the caller left");
            Exception? caught = null;

            // Act
            try
            {
                await s_noDelay.RunAsync<int>(_ =>
                {
                    cts.Cancel();
                    throw failure;
                }, cts.Token);
            }
            catch (Exception propagated) { caught = propagated; }

            // Assert
            Assert.That(caught, Is.SameAs(failure),
                "A failure arriving once the caller has cancelled is not retried and reaches the caller as it is");
        });

#if UNITY_EDITOR
        [Test]
        public void Given_AZeroDelay_When_AnOperationFailsSynchronously_Then_TheRetryWaitsForAFrame()
        {
            // Arrange
            var attempts = 0;
            var running = s_noDelay.RunAsync<int>(_ =>
                ++attempts == 1 ? throw new InvalidOperationException("transient") : VelvetTask.FromResult(attempts));
            var attemptsBeforeAFrame = attempts;

            // Act
            DrainEditorUpdateForTest();

            // Assert
            Assert.That((attemptsBeforeAFrame, running.Status), Is.EqualTo((1, VelvetTaskStatus.Succeeded)),
                "The retry runs at the next frame rather than on the stack of the failure it follows");
        }

        [Test]
        public void Given_TheDefaultWait_When_FramesPassBeforeTheDelay_Then_NoRetryHasStarted()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var attempts = 0;
            var policy = new RetryPolicy { RetryDelay = (_, _) => TimeSpan.FromHours(1) };
            _ = policy.RunAsync<int>(_ =>
            {
                attempts++;
                throw new InvalidOperationException("down");
            }, cts.Token);

            // Act — the cancel afterwards ends the hour's polling rather than leaving it running.
            DrainEditorUpdateForTest();
            DrainEditorUpdateForTest();
            var attemptsAfterFrames = attempts;
            cts.Cancel();
            DrainEditorUpdateForTest();

            // Assert
            Assert.That(attemptsAfterFrames, Is.EqualTo(1), "Two frames into an hour's delay, the retry has not run");
        }

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
