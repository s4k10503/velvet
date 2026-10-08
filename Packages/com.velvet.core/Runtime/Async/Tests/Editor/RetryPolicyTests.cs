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
        // Both signals supplied, so no case reads the Application's own focus or connectivity.
        private static readonly RetryPolicy s_ready = new() { IsOnline = () => true, IsFocused = () => true };

        private static readonly RetryPolicy s_noDelay = s_ready with { RetryDelay = TimeSpan.Zero };

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
            var policy = s_ready with
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
            var policy = s_ready with
            {
                Retry = 2,
                RetryDelay = RetryDelayRule.By((failureCount, _) => TimeSpan.FromMilliseconds(10 * (failureCount + 1))),
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
        public IEnumerator Given_AnOperationThatThrowsCancellationOnce_When_TheTokenIsNotCancelled_Then_ItIsRetried() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the cancellation is the operation's own: the token the policy hands it is never
            // cancelled, so the exception is a failure like any other, as TanStack retries an AbortError the
            // function throws.
            var attempts = 0;
            VelvetTask<int> Operation(CancellationToken _) =>
                ++attempts == 1 ? throw new OperationCanceledException("aborted by the operation's own source") : VelvetTask.FromResult(attempts);

            // Act
            var result = await s_noDelay.RunAsync(Operation);

            // Assert
            Assert.That((result, attempts), Is.EqualTo((2, 2)), "An OperationCanceledException from the operation is retried while the caller's token is live");
        });

        [UnityTest]
        public IEnumerator Given_ATokenCancelledByTheCaller_When_TheOperationThrowsCancellation_Then_ItIsNotRetried() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var attempts = 0;

            // Act
            try
            {
                await s_noDelay.RunAsync<int>(token =>
                {
                    attempts++;
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                    return VelvetTask.FromResult(0);
                }, cts.Token);
            }
            catch (OperationCanceledException) { }

            // Assert
            Assert.That(attempts, Is.EqualTo(1), "A caller who has cancelled gets no further attempt");
        });

        [UnityTest]
        public IEnumerator Given_AConstantRetryDelay_When_AnOperationFailsTenTimes_Then_EveryRetryWaitsForIt() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var waits = new List<TimeSpan>();
            var policy = s_ready with
            {
                RetryDelay = TimeSpan.FromSeconds(5),
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
            Assert.That(string.Join(",", waits), Is.EqualTo("00:00:05,00:00:05,00:00:05"),
                "A TimeSpan is TanStack's constant retryDelay: the same wait before each of the default three retries");
        });

        [UnityTest]
        public IEnumerator Given_ARetryDelayAndAWhen_When_AnOperationFailsOnce_Then_TheDelayIsAskedBeforeTheDecision() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the function declines, so the delay is asked for a retry that never happens, as the
            // retryer computes it before consulting retry.
            var asked = new List<string>();
            var policy = s_ready with
            {
                RetryDelay = RetryDelayRule.By((_, _) =>
                {
                    asked.Add("delay");
                    return TimeSpan.Zero;
                }),
                Retry = RetryRule.When((_, _) =>
                {
                    asked.Add("retry");
                    return false;
                }),
            };

            // Act
            try { await policy.RunAsync(FailingTenTimes(new List<Exception>())); }
            catch (InvalidOperationException) { }

            // Assert
            Assert.That(string.Join(",", asked), Is.EqualTo("delay,retry"), "retryDelay(failureCount, error) is evaluated first, then retry(failureCount, error)");
        });

        [Test]
        public void Given_ANullFunction_When_RetryDelayRuleByIsCalled_Then_ItThrows()
        {
            // Act
            TestDelegate create = () => RetryDelayRule.By(null!);

            // Assert
            Assert.That(create, Throws.ArgumentNullException, "A rule with no function is refused where it is made");
        }

        [UnityTest]
        public IEnumerator Given_AWaitThatIgnoresTheToken_When_CancelledDuringIt_Then_NoFurtherAttemptStarts() => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var gate = new VelvetTaskCompletionSource();
            var attempts = 0;
            var policy = s_ready with { Wait = (_, _) => gate.Task };
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
            var policy = s_ready with { RetryDelay = TimeSpan.FromHours(1) };
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

        private static void DrainFrames()
        {
            for (var frame = 0; frame < 5; frame++)
            {
                DrainEditorUpdateForTest();
            }
        }

        [Test]
        public void Given_NetworkModeOnline_When_TheDeviceIsOfflineAtTheStart_Then_NoAttemptRunsUntilItIsOnline()
        {
            // Arrange
            var online = false;
            var attempts = 0;
            var policy = s_ready with { IsOnline = () => online };
            _ = policy.RunAsync(_ => VelvetTask.FromResult(++attempts));

            // Act
            DrainFrames();
            var attemptsOffline = attempts;
            online = true;
            DrainFrames();

            // Assert
            Assert.That((attemptsOffline, attempts), Is.EqualTo((0, 1)), "The first attempt waits for a connection, then runs");
        }

        [Test]
        public void Given_NetworkModeAlways_When_TheDeviceIsOffline_Then_TheFirstAttemptRunsAtOnce()
        {
            // Arrange
            var attempts = 0;
            var policy = s_ready with { NetworkMode = NetworkMode.Always, IsOnline = () => false };

            // Act
            _ = policy.RunAsync(_ => VelvetTask.FromResult(++attempts));

            // Assert
            Assert.That(attempts, Is.EqualTo(1), "Always never waits for a connection");
        }

        [Test]
        public void Given_NetworkModeOfflineFirst_When_TheDeviceIsOffline_Then_TheFirstAttemptRunsAndTheRetryWaitsForAConnection()
        {
            // Arrange
            var online = false;
            var attempts = 0;
            var policy = s_noDelay with { NetworkMode = NetworkMode.OfflineFirst, IsOnline = () => online };
            _ = policy.RunAsync<int>(_ =>
            {
                attempts++;
                throw new InvalidOperationException("down");
            });

            // Act
            DrainFrames();
            var attemptsOffline = attempts;
            online = true;
            DrainFrames();

            // Assert
            Assert.That((attemptsOffline, attempts > 1), Is.EqualTo((1, true)), "The first attempt does not wait and the retry does");
        }

        [Test]
        public void Given_ARetryDueWhileOffline_When_TheDeviceComesBackOnline_Then_TheRetryRuns()
        {
            // Arrange
            var online = true;
            var attempts = 0;
            var policy = s_noDelay with { IsOnline = () => online };
            var running = policy.RunAsync(_ =>
            {
                online = false;
                return ++attempts == 1 ? throw new InvalidOperationException("transient") : VelvetTask.FromResult(attempts);
            });

            // Act
            DrainFrames();
            var attemptsOffline = attempts;
            online = true;
            DrainFrames();

            // Assert
            Assert.That((attemptsOffline, running.Status), Is.EqualTo((1, VelvetTaskStatus.Succeeded)),
                "A retry that comes due offline waits for the connection");
        }

        [Test]
        public void Given_ARetryDueWhileUnfocused_When_TheApplicationRegainsFocus_Then_TheRetryRuns()
        {
            // Arrange — Always, so only focus can be what holds the retry.
            var focused = false;
            var attempts = 0;
            var policy = s_noDelay with { NetworkMode = NetworkMode.Always, IsFocused = () => focused };
            var running = policy.RunAsync(_ =>
                ++attempts == 1 ? throw new InvalidOperationException("transient") : VelvetTask.FromResult(attempts));

            // Act
            DrainFrames();
            var attemptsUnfocused = attempts;
            focused = true;
            DrainFrames();

            // Assert
            Assert.That((attemptsUnfocused, running.Status), Is.EqualTo((1, VelvetTaskStatus.Succeeded)),
                "A retry waits for focus in every network mode");
        }

        [Test]
        public void Given_ARetryPausedOffline_When_TheCallerCancels_Then_TheRunRejectsAsCancelled()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var online = true;
            var policy = s_noDelay with { IsOnline = () => online };
            var running = policy.RunAsync<int>(_ =>
            {
                online = false;
                throw new InvalidOperationException("down");
            }, cts.Token);
            DrainFrames();

            // Act
            cts.Cancel();
            DrainFrames();

            // Assert
            Assert.That(running.Status, Is.EqualTo(VelvetTaskStatus.Canceled), "A pause ends when its caller leaves");
        }

        [Test]
        public void Given_TheDefaultWait_When_CancelledDuringIt_Then_TheRunRejectsAsCancelledOnceFramesPass()
        {
            // Arrange — an hour, so the wait cannot end on its own while the frames below are drained.
            using var cts = new CancellationTokenSource();
            var policy = s_ready with { RetryDelay = TimeSpan.FromHours(1) };
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
