#nullable enable
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
    /// Specifies how <see cref="Hooks.UseQuery{T}"/> retries a failed request, TanStack Query's <c>retry</c>
    /// and <c>retryDelay</c>.
    /// <list type="bullet">
    /// <item>A query retries three times by default, after one second, then two, then four, and reports the
    /// failure as the entry's error only when the fourth request fails. A wait is over once its delay has
    /// passed on the client's clock, not a moment before, and doubles at each failure up to thirty seconds.</item>
    /// <item><c>Retry</c> on the query overrides <c>QueryClientOptions.Retry</c>, and <c>RetryDelay</c> replaces
    /// the doubling and is handed the failures so far and the exception; a delay of zero still waits a
    /// frame.</item>
    /// <item>While a retry waits, the query stays pending and fetching and reports the failure beside its
    /// <c>FailureCount</c>; a retry that succeeds leaves no failure behind. A query function that throws
    /// before returning is retried like one returning a faulted task.</item>
    /// <item>With its last reader gone a request in flight of a function taking no token is not retried and stops waiting out its delay, and
    /// a reader mounting before the wait is over lets it be. A delay function that throws fails the
    /// request.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The client's clock is <see cref="s_now"/>, advanced by hand, and each case advances frames with
    /// <c>VelvetTask.Yield()</c> because a wait is checked once a frame. The query function hands back a
    /// completion source the case fails or completes itself, and <see cref="s_calls"/> counts the requests that
    /// ran.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryRetryTests
    {
        private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

        private static TimeSpan Seconds(double count) => TimeSpan.FromSeconds(count);

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static int? s_retry;
        private static Func<int, Exception, TimeSpan>? s_retryDelay;
        private static bool s_throwsSynchronously;
        private static bool s_takesToken;
        private static int s_calls;
        private static int s_clockReads;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<QueryResult<int>> s_renders = new();
        private static StateUpdater<bool> s_setShow;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.Zero;
            s_retry = null;
            s_retryDelay = null;
            s_throwsSynchronously = false;
            s_takesToken = true;
            s_calls = 0;
            s_clockReads = 0;
            s_client = NewClient(retry: 3);
            s_sources.Clear();
            s_renders.Clear();
            s_setShow = default;
        }

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_AFailingQuery_When_EachDefaultDelayPasses_Then_ItRunsFourTimesAndThenReportsTheError_Calls()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            await Pass(Second);
            Fail(1);
            await Pass(Seconds(2));
            Fail(2);
            await Pass(Seconds(4));
            Fail(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(4),
                "Three retries follow the first request, each after twice the wait before it");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_AFailingQuery_When_EachDefaultDelayPasses_Then_ItRunsFourTimesAndThenReportsTheError_Status()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            await Pass(Second);
            Fail(1);
            await Pass(Seconds(2));
            Fail(2);
            await Pass(Seconds(4));
            Fail(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().Status, Is.EqualTo(QueryStatus.Error),
                "Three retries follow the first request, each after twice the wait before it");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_AFailingQuery_When_EachDefaultDelayPasses_Then_ItRunsFourTimesAndThenReportsTheError_FailureCount()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            await Pass(Second);
            Fail(1);
            await Pass(Seconds(2));
            Fail(2);
            await Pass(Seconds(4));
            Fail(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().FailureCount, Is.EqualTo(4),
                "Three retries follow the first request, each after twice the wait before it");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_AFailedRequest_When_TheFirstDelayIsOneMillisecondShort_Then_ItIsRetriedOnlyOnceItHasPassed_BeforeTheDelay()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);

            // Act
            await Pass(Seconds(0.999));
            var beforeTheDelay = s_calls;
            await Pass(Seconds(0.001));

            // Assert
            Assert.That(beforeTheDelay, Is.EqualTo(1),
                "A wait of one second ends when one second has passed, not before");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_AFailedRequest_When_TheFirstDelayIsOneMillisecondShort_Then_ItIsRetriedOnlyOnceItHasPassed_Calls()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);

            // Act
            await Pass(Seconds(0.999));
            var beforeTheDelay = s_calls;
            await Pass(Seconds(0.001));

            // Assert
            Assert.That(s_calls, Is.EqualTo(2),
                "A wait of one second ends when one second has passed, not before");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_ASecondFailure_When_OnlyTheFirstDelayPassesAgain_Then_ItIsRetriedAfterTwiceThat_AfterOneSecond()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);
            await Pass(Second);
            Fail(1);

            // Act
            await Pass(Second);
            var afterOneSecond = s_calls;
            await Pass(Second);

            // Assert
            Assert.That(afterOneSecond, Is.EqualTo(2), "The wait doubles at each failure");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_ASecondFailure_When_OnlyTheFirstDelayPassesAgain_Then_ItIsRetriedAfterTwiceThat_Calls()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);
            await Pass(Second);
            Fail(1);

            // Act
            await Pass(Second);
            var afterOneSecond = s_calls;
            await Pass(Second);

            // Assert
            Assert.That(s_calls, Is.EqualTo(3), "The wait doubles at each failure");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_ASixthFailure_When_TwentyNineSecondsPass_Then_ItWaitsOneMoreSecondBeforeTheRetry_AfterTwentyNineSeconds()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the delays before it add up to 31 s, and a sixth wait of 32 s is capped at 30 s.
            s_retry = 10;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            for (var failure = 0; failure < 5; failure++)
            {
                Fail(failure);
                await Pass(Seconds(1 << failure));
            }
            Fail(5);

            // Act
            await Pass(Seconds(29));
            var afterTwentyNineSeconds = s_calls;
            await Pass(Second);

            // Assert
            Assert.That(afterTwentyNineSeconds, Is.EqualTo(6), "A wait never exceeds thirty seconds");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_ASixthFailure_When_TwentyNineSecondsPass_Then_ItWaitsOneMoreSecondBeforeTheRetry_Calls()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the delays before it add up to 31 s, and a sixth wait of 32 s is capped at 30 s.
            s_retry = 10;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            for (var failure = 0; failure < 5; failure++)
            {
                Fail(failure);
                await Pass(Seconds(1 << failure));
            }
            Fail(5);

            // Act
            await Pass(Seconds(29));
            var afterTwentyNineSeconds = s_calls;
            await Pass(Second);

            // Assert
            Assert.That(s_calls, Is.EqualTo(7), "A wait never exceeds thirty seconds");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_ARetryOfOne_When_BothRequestsFail_Then_TheQueryReportsTheErrorAfterTwoRuns_Calls()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_retry = 1;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            await Pass(Second);
            Fail(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2),
                "A query's own retry count replaces the client's");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_ARetryOfOne_When_BothRequestsFail_Then_TheQueryReportsTheErrorAfterTwoRuns_Status()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_retry = 1;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            await Pass(Second);
            Fail(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().Status, Is.EqualTo(QueryStatus.Error),
                "A query's own retry count replaces the client's");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_AClientRetryOfOne_When_ABareQueryFailsTwice_Then_TheQueryReportsTheErrorAfterTwoRuns_Calls()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client = NewClient(retry: 1);
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            await Pass(Second);
            Fail(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2),
                "A query that sets no retry count takes the client's");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_AClientRetryOfOne_When_ABareQueryFailsTwice_Then_TheQueryReportsTheErrorAfterTwoRuns_Status()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_client = NewClient(retry: 1);
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            await Pass(Second);
            Fail(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().Status, Is.EqualTo(QueryStatus.Error),
                "A query that sets no retry count takes the client's");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [Test]
        public void Given_AQueryRetryOfZeroOverAClientRetryOfThree_When_TheRequestFails_Then_TheQueryReportsTheErrorAtOnce_Calls()
        {
            // Arrange
            s_retry = 0;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1),
                "Zero retries make the first failure the entry's error, whatever the client retries by default");
        }

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [Test]
        public void Given_AQueryRetryOfZeroOverAClientRetryOfThree_When_TheRequestFails_Then_TheQueryReportsTheErrorAtOnce_Status()
        {
            // Arrange
            s_retry = 0;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().Status, Is.EqualTo(QueryStatus.Error),
                "Zero retries make the first failure the entry's error, whatever the client retries by default");
        }

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [Test]
        public void Given_AQueryRetryOfZeroOverAClientRetryOfThree_When_TheRequestFails_Then_TheQueryReportsTheErrorAtOnce_FailureCount()
        {
            // Arrange
            s_retry = 0;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().FailureCount, Is.EqualTo(1),
                "Zero retries make the first failure the entry's error, whatever the client retries by default");
        }

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_ACustomRetryDelay_When_TheFailureIsRetried_Then_ItWaitsThatLong_AfterNineSeconds()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_retryDelay = (_, _) => Seconds(10);
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);

            // Act
            await Pass(Seconds(9));
            var afterNineSeconds = s_calls;
            await Pass(Second);

            // Assert
            Assert.That(afterNineSeconds, Is.EqualTo(1), "The delay function replaces the doubling");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_ACustomRetryDelay_When_TheFailureIsRetried_Then_ItWaitsThatLong_Calls()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_retryDelay = (_, _) => Seconds(10);
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);

            // Act
            await Pass(Seconds(9));
            var afterNineSeconds = s_calls;
            await Pass(Second);

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "The delay function replaces the doubling");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_ARetryDelayOfZero_When_TheRequestFails_Then_TheRetryRunsOnlyAFrameLater_OnTheFailingStack()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_retryDelay = (_, _) => TimeSpan.Zero;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            var onTheFailingStack = s_calls;
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(onTheFailingStack, Is.EqualTo(1),
                "A retry never starts inside the completion of the request that failed");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_ARetryDelayOfZero_When_TheRequestFails_Then_TheRetryRunsOnlyAFrameLater_Calls()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_retryDelay = (_, _) => TimeSpan.Zero;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            var onTheFailingStack = s_calls;
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(s_calls, Is.EqualTo(2),
                "A retry never starts inside the completion of the request that failed");
        });

        // GREEN_ON_BASE(characterization): the base runs the same wait; this change only gives the fixture's client a gcTime that never expires.
        [UnityTest]
        public IEnumerator Given_ACustomRetryDelay_When_TwoFailuresAreRetried_Then_ItIsHandedEachCountAndException()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var handed = new List<string>();
            s_retryDelay = (failureCount, error) =>
            {
                handed.Add(failureCount + ":" + error.Message);
                return Second;
            };
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            await Pass(Second);
            Fail(1);

            // Assert
            Assert.That(string.Join(" ", handed), Is.EqualTo("0:fail-1 1:fail-2"),
                "The count is the failures before this one, as v5 passes it");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [Test]
        public void Given_AFailedRequestWaitingToRetry_When_TheQueryRenders_Then_ItIsStillPendingAndFetchingBesideTheFailure_ResultStatus()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            mounted.FlushStateForTest();
            var result = Last();

            // Assert
            Assert.That(result.Status, Is.EqualTo(QueryStatus.Pending),
                "A failure that is going to be retried is not yet the entry's error");
        }

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [Test]
        public void Given_AFailedRequestWaitingToRetry_When_TheQueryRenders_Then_ItIsStillPendingAndFetchingBesideTheFailure_ResultIsFetching()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            mounted.FlushStateForTest();
            var result = Last();

            // Assert
            Assert.That(result.IsFetching, Is.EqualTo(true),
                "A failure that is going to be retried is not yet the entry's error");
        }

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [Test]
        public void Given_AFailedRequestWaitingToRetry_When_TheQueryRenders_Then_ItIsStillPendingAndFetchingBesideTheFailure_ResultErrorMessage()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            mounted.FlushStateForTest();
            var result = Last();

            // Assert
            Assert.That(result.Error?.Message, Is.EqualTo((string?)null),
                "A failure that is going to be retried is not yet the entry's error");
        }

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [Test]
        public void Given_AFailedRequestWaitingToRetry_When_TheQueryRenders_Then_ItIsStillPendingAndFetchingBesideTheFailure_ResultFailureCount()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            mounted.FlushStateForTest();
            var result = Last();

            // Assert
            Assert.That(result.FailureCount, Is.EqualTo(1),
                "A failure that is going to be retried is not yet the entry's error");
        }

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [Test]
        public void Given_AFailedRequestWaitingToRetry_When_TheQueryRenders_Then_ItIsStillPendingAndFetchingBesideTheFailure_ResultFailureReasonMessage()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            mounted.FlushStateForTest();
            var result = Last();

            // Assert
            Assert.That(result.FailureReason?.Message, Is.EqualTo("fail-1"),
                "A failure that is going to be retried is not yet the entry's error");
        }

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_AFailedRequest_When_TheRetrySucceeds_Then_TheQueryRendersTheDataBesideNoFailure_ResultStatus()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);

            // Act
            await Pass(Second);
            s_sources[1].TrySetResult(7);
            mounted.FlushStateForTest();
            var result = Last();

            // Assert
            Assert.That(result.Status, Is.EqualTo(QueryStatus.Success),
                "A request that lands clears the failures that preceded it");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_AFailedRequest_When_TheRetrySucceeds_Then_TheQueryRendersTheDataBesideNoFailure_ResultData()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);

            // Act
            await Pass(Second);
            s_sources[1].TrySetResult(7);
            mounted.FlushStateForTest();
            var result = Last();

            // Assert
            Assert.That(result.Data, Is.EqualTo(7),
                "A request that lands clears the failures that preceded it");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_AFailedRequest_When_TheRetrySucceeds_Then_TheQueryRendersTheDataBesideNoFailure_ResultIsFetching()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);

            // Act
            await Pass(Second);
            s_sources[1].TrySetResult(7);
            mounted.FlushStateForTest();
            var result = Last();

            // Assert
            Assert.That(result.IsFetching, Is.EqualTo(false),
                "A request that lands clears the failures that preceded it");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_AFailedRequest_When_TheRetrySucceeds_Then_TheQueryRendersTheDataBesideNoFailure_ResultFailureCount()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);

            // Act
            await Pass(Second);
            s_sources[1].TrySetResult(7);
            mounted.FlushStateForTest();
            var result = Last();

            // Assert
            Assert.That(result.FailureCount, Is.EqualTo(0),
                "A request that lands clears the failures that preceded it");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [UnityTest]
        public IEnumerator Given_AFailedRequest_When_TheRetrySucceeds_Then_TheQueryRendersTheDataBesideNoFailure_ResultFailureReason()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);

            // Act
            await Pass(Second);
            s_sources[1].TrySetResult(7);
            mounted.FlushStateForTest();
            var result = Last();

            // Assert
            Assert.That(result.FailureReason, Is.EqualTo((Exception?)null),
                "A request that lands clears the failures that preceded it");
        });

        // GREEN_ON_BASE(characterization): the base runs the same wait; this change only gives the fixture's client a gcTime that never expires.
        [UnityTest]
        public IEnumerator Given_AQueryFunctionThatThrowsBeforeReturning_When_TheDelayPasses_Then_ItIsCalledAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_throwsSynchronously = true;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            await Pass(TimeSpan.Zero);

            // Act
            await Pass(Second);

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "A throw is a failed request, which is retried like any other");
        });

        // GREEN_ON_BASE(characterization): the base runs the same wait; this change only gives the fixture's client a gcTime that never expires.
        [UnityTest]
        public IEnumerator Given_AFailedRequestWaitingToRetry_When_ItsOnlyReaderUnmounts_Then_NoRetryRuns()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_takesToken = false;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);

            // Act
            Hide(mounted);
            await Pass(Second);

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "With nobody reading, the request in flight is not run again");
        });

        // GREEN_ON_BASE(characterization): the base runs the same wait; this change only gives the fixture's client a gcTime that never expires.
        [UnityTest]
        public IEnumerator Given_AFailedRequestWaitingToRetry_When_ItsOnlyReaderUnmounts_Then_ItStopsWaitingOutTheDelay()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_takesToken = false;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);
            Hide(mounted);
            await Pass(TimeSpan.Zero);
            var readsAfterTheFirstFrames = s_clockReads;

            // Act
            await Pass(TimeSpan.Zero);

            // Assert
            Assert.That(s_clockReads, Is.EqualTo(readsAfterTheFirstFrames),
                "The wait is over once nothing will retry, whatever is left of its delay");
        });

        // GREEN_ON_BASE(characterization): the base runs the same wait; this change only gives the fixture's client a gcTime that never expires.
        [UnityTest]
        public IEnumerator Given_AFailedRequestWaitingToRetry_When_ItsOnlyReaderLeaves_Then_TheFailureCountIsBackToZero()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);
            await Pass(TimeSpan.Zero);

            // Act
            Hide(mounted);

            // Assert
            Assert.That(s_client.PeekById<int>(new QueryKey("todos"))!.FailureCount, Is.EqualTo(0),
                "Cancelling during the wait puts back the count the request started from");
        });

        // GREEN_ON_BASE(characterization): the base runs the same wait; this change only gives the fixture's client a gcTime that never expires.
        [UnityTest]
        public IEnumerator Given_AFailedRequestWaitingToRetry_When_ItsOnlyReaderLeaves_Then_NoFailureReasonIsHeld()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);
            await Pass(TimeSpan.Zero);

            // Act
            Hide(mounted);

            // Assert
            Assert.That(s_client.PeekById<int>(new QueryKey("todos"))!.FailureReason, Is.Null,
                "Cancelling during the wait puts back the reason the request started from");
        });

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [Test]
        public void Given_ARetryDelayThatThrows_When_TheRequestFails_Then_TheQueryReportsThatErrorAndStopsFetching_Status()
        {
            // Arrange
            s_retryDelay = (_, _) => throw new InvalidOperationException("delay-failed");
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().Status, Is.EqualTo(QueryStatus.Error),
                "A delay function that fails ends the request rather than leaving it in flight");
        }

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [Test]
        public void Given_ARetryDelayThatThrows_When_TheRequestFails_Then_TheQueryReportsThatErrorAndStopsFetching_ErrorMessage()
        {
            // Arrange
            s_retryDelay = (_, _) => throw new InvalidOperationException("delay-failed");
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().Error?.Message, Is.EqualTo("delay-failed"),
                "A delay function that fails ends the request rather than leaving it in flight");
        }

        // GREEN_ON_BASE(refactor): one fact of a case that asserted them together; the base runs the same arrangement.
        [Test]
        public void Given_ARetryDelayThatThrows_When_TheRequestFails_Then_TheQueryReportsThatErrorAndStopsFetching_IsFetching()
        {
            // Arrange
            s_retryDelay = (_, _) => throw new InvalidOperationException("delay-failed");
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            Fail(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last().IsFetching, Is.EqualTo(false),
                "A delay function that fails ends the request rather than leaving it in flight");
        }

        // GREEN_ON_BASE(characterization): the base runs the same wait; this change only gives the fixture's client a gcTime that never expires.
        [UnityTest]
        public IEnumerator Given_AFailedRequestWaitingToRetry_When_AReaderMountsBeforeTheDelayPasses_Then_TheRetryRuns()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_takesToken = false;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            Fail(0);
            Hide(mounted);
            Show(mounted);

            // Act
            await Pass(Second);

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "A reader joining the request lets its retries go on");
        });

        private static QueryClient NewClient(int retry)
            => new(new QueryClientOptions
            {
                Retry = retry,
                // No entry expires, so the client's garbage-collection polling reads no clock to be counted beside the waits.
                GcTime = TimeSpan.MaxValue,
                Clock = () =>
                {
                    s_clockReads++;
                    return s_now;
                },
            });

        private static QueryResult<int> Last() => s_renders[s_renders.Count - 1];

        private static void Fail(int call)
            => s_sources[call].TrySetException(new InvalidOperationException("fail-" + (call + 1)));

        private static async VelvetTask Pass(TimeSpan time)
        {
            s_now += time;
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            await VelvetTask.Yield();
        }

        private static void Hide(MountedTree mounted)
        {
            s_setShow.Invoke(false);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static void Show(MountedTree mounted)
        {
            s_setShow.Invoke(true);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            s_calls++;
            if (s_throwsSynchronously) throw new InvalidOperationException("threw-" + s_calls);
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }

        [Component]
        private static VNode Reader()
        {
            var key = new QueryKey("todos");
            var options = s_takesToken
                ? new QueryOptions<int>(key, (QueryFunctionContext context) => Fetch(context.CancellationToken))
                : new QueryOptions<int>(key, () => Fetch(default));
            s_renders.Add(Hooks.UseQuery(
                options with
                {
                    Retry = s_retry,
                    RetryDelay = s_retryDelay,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "reader");
        }

        [Component]
        private static VNode Host()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShow = setShow;
            return V.Div(children: new VNode[]
            {
                show ? V.Component(Reader, key: "reader") : V.Fragment(Array.Empty<VNode>()),
            });
        }
    }
}
