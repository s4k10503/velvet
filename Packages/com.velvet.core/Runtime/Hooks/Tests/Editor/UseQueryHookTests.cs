#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Specifies <see cref="Hooks.UseQuery{T}"/> over a <see cref="QueryClient"/>, TanStack Query's
    /// <c>useQuery</c> over its <c>QueryClient</c>.
    /// <list type="bullet">
    /// <item>Components reading one key issue one request and both render its result; unmounting one leaves
    /// the request running for the other, and a component mounting while a refetch is in flight joins it.</item>
    /// <item>What a render reports: pending and fetching before any data, the data and the outcome flags once
    /// it lands, a refetch in flight beside the data, a failure beside the data an earlier request produced,
    /// which it leaves stale, and the outcome of a query function that completes or throws before returning,
    /// which arrives a frame later so that readers mounting in one commit share the request.</item>
    /// <item>An entry keeps its result after its last reader unmounts, so a reader mounting again renders it
    /// on its first render, and refetches only once the result is as old as the stale time or the entry was
    /// invalidated. A result landing between a reader's render and its subscription re-renders it.</item>
    /// <item>An entry nothing reads is removed once it has gone unread for the longest garbage-collection time
    /// a query asked for, by the next sweep — an invalidation or a subscription — unless a request is still in
    /// flight for it: then it stays, and readable, until a whole gcTime has passed since the request settled, a
    /// gcTime of zero included. An entry something reads again, or still reads, is not removed, and a new entry for a key
    /// whose old entry was removed is not swept with it. An expired entry reads as absent before the sweep,
    /// and a reader mounting over it subscribes to a new entry, while one left by a reader in the commit that
    /// brings the next reader is handed over, whatever its gcTime — a keyed remount, and StrictMode's extra
    /// cleanup and setup of a mounting reader's effects, which joins the request the first setup started.</item>
    /// <item>A key change requests the new key and releases the old key's entry, and the old key's request
    /// landing is never shown as the new key's data. Until the commit's effect moves the subscription, the
    /// old entry keeps the old key's query function. A change of client moves the query to the new one.</item>
    /// <item><c>InvalidateQueries</c> refetches the entries its key leads, starts over a refetch already in
    /// flight and joins a first request, and fetches nothing for an entry nobody reads; <c>Refetch</c> fetches again and starts over a refetch in flight;
    /// <c>Clear</c> makes a mounted query fetch into a new entry; a request started over that completes on
    /// another thread does not land; a cancellation callback that throws is logged rather than raised.</item>
    /// <item>The Editor warns once when a key that prints the same is unequal on two commits running, as one
    /// holding an array inside a record is, and not after a single such commit, nor for keys that print
    /// differently or stay equal.</item>
    /// <item>The client is read from <see cref="QueryClientContext.Ref"/> when none is passed, and a query
    /// with neither, or with null options, key or function, throws.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The query function records the key it was asked for and the token it was handed, and by default hands
    /// back a completion source the case settles itself, so a case reads which requests ran off
    /// <see cref="s_fetched"/>. The client's clock is <see cref="s_now"/>, advanced by hand. EditMode runs neither
    /// passive effects nor scheduled re-renders on its own, so each case flushes them. A query here retries
    /// nothing and re-renders at every change, which <c>UseQueryRetryTests</c> and
    /// <c>UseQueryTrackedPropsTests</c> specify.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryHookTests
    {
        private static readonly TimeSpan GcTime = TimeSpan.FromMinutes(5);
        private static readonly QueryKey Todos = new("todos");
        private static readonly QueryKey Unrelated = new("unrelated");

        private enum FetchMode
        {
            Pending,
            CompletedResult,
            ThrowsSynchronously,
            CompletedFault,
        }

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static TimeSpan? s_staleTime;
        private static TimeSpan? s_gcTimeB;
        private static FetchMode s_mode;
        private static Action<CancellationToken>? s_onToken;
        private static QueryOptions<int>? s_badOptions;
        private static readonly List<string> s_fetched = new();
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<CancellationToken> s_tokens = new();
        private static readonly List<QueryResult<int>> s_rendersA = new();
        private static readonly List<QueryResult<int>> s_rendersB = new();
        private static StateUpdater<bool> s_setShowA;
        private static StateUpdater<int> s_setPage;
        private static StateUpdater<QueryClient> s_setClient;
        private static StateUpdater<int> s_setGeneration;
        private static StateUpdater<int> s_setTick;

        private sealed record Filter(string Status, string[] Ids);

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.Zero;
            s_staleTime = null;
            s_gcTimeB = null;
            s_mode = FetchMode.Pending;
            s_onToken = null;
            s_badOptions = null;
            s_client = NewClient();
            s_fetched.Clear();
            s_sources.Clear();
            s_tokens.Clear();
            s_rendersA.Clear();
            s_rendersB.Clear();
            s_setShowA = default;
            s_setPage = default;
            s_setClient = default;
            s_setGeneration = default;
            s_setTick = default;
        }

        [TearDown]
        public void TearDown()
        {
            FiberStrictMode.Enabled = false;
        }

        #region Sharing one entry

        [Test]
        public void Given_TwoComponentsReadingOneKey_When_BothMount_Then_OneRequestRuns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Pair, key: "pair"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("[todos]"),
                "Two components reading one key share the request the first one started");
        }

        [Test]
        public void Given_TwoComponentsReadingOneKey_When_TheRequestResolves_Then_BothRenderItsData()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Pair, key: "pair"));
            mounted.FlushEffectsForTest();

            // Act
            s_sources[0].TrySetResult(7);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last(s_rendersA).Data, Last(s_rendersB).Data), Is.EqualTo((7, 7)),
                "Every component reading the entry re-renders with the result");
        }

        [Test]
        public void Given_TwoComponentsAwaitingOneRequest_When_OneUnmounts_Then_TheOtherStillReceivesTheResult()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Pair, key: "pair"));
            mounted.FlushEffectsForTest();
            var token = s_tokens[0];

            // Act
            Hide(mounted);
            s_sources[0].TrySetResult(7);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((token.IsCancellationRequested, Last(s_rendersB).Data), Is.EqualTo((false, 7)),
                "The request belongs to the entry, so a reader leaving neither cancels it nor keeps it from the other");
        }

        [Test]
        public void Given_ARefetchInFlightOverData_When_AnotherReaderMounts_Then_ItJoinsTheRefetch()
        {
            // Arrange
            using var mounted = MountResolvedSolo(1);
            Last(s_rendersA).Refetch();

            // Act
            using var other = V.Mount(new VisualElement(), V.Component(ReaderB, key: "b"));
            other.FlushEffectsForTest();

            // Assert
            Assert.That((s_fetched.Count, s_tokens[1].IsCancellationRequested), Is.EqualTo((2, false)),
                "Mounting is not an explicit refetch, so it joins the request in flight rather than starting over");
        }

        #endregion

        #region What a render reports

        [Test]
        public void Given_NothingCached_When_TheQueryFirstRenders_Then_ItIsPendingFetchingAndStale()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));

            // Assert
            Assert.That((s_rendersA[0].Status, s_rendersA[0].IsFetching, s_rendersA[0].IsStale),
                Is.EqualTo((QueryStatus.Pending, true, true)),
                "The render before the subscription reports the fetch the subscription is about to start");
        }

        [Test]
        public void Given_AResolvedQuery_When_ItRenders_Then_ItsFlagsReadSuccessAndIdle()
        {
            // Act
            using var mounted = MountResolvedSolo(7);
            var result = Last(s_rendersA);

            // Assert
            Assert.That((result.IsPending, result.IsSuccess, result.IsError, result.IsFetching),
                Is.EqualTo((false, true, false, false)),
                "A subscribed query with nothing in flight reports no fetch, even with its result stale");
        }

        [Test]
        public void Given_AResolvedQuery_When_ARefetchIsInFlight_Then_ItRendersTheDataWhileFetching()
        {
            // Arrange
            using var mounted = MountResolvedSolo(1);

            // Act
            Last(s_rendersA).Refetch();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last(s_rendersA).Status, Last(s_rendersA).Data, Last(s_rendersA).IsFetching),
                Is.EqualTo((QueryStatus.Success, 1, true)),
                "A refetch keeps the status and the data, and reports the request in flight");
        }

        [Test]
        public void Given_AResolvedQuery_When_ItsRefetchFails_Then_TheErrorStandsBesideTheEarlierData()
        {
            // Arrange
            using var mounted = MountResolvedSolo(1);
            Last(s_rendersA).Refetch();

            // Act
            s_sources[1].TrySetException(new InvalidOperationException("refetch-failed"));
            mounted.FlushStateForTest();
            var result = Last(s_rendersA);

            // Assert
            Assert.That((result.Status, result.IsError, result.Data, result.Error?.Message),
                Is.EqualTo((QueryStatus.Error, true, 1, "refetch-failed")),
                "A failure keeps the last good data, as TanStack does");
        }

        [Test]
        public void Given_AFreshResolvedQuery_When_ItsRefetchFails_Then_TheDataIsStale()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            using var mounted = MountResolvedSolo(1);
            Last(s_rendersA).Refetch();

            // Act
            s_sources[1].TrySetException(new InvalidOperationException("refetch-failed"));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last(s_rendersA).IsStale, Is.True,
                "A failure flags the data it leaves in place as stale, so the next reader asks again");
        }

        [Test]
        public void Given_AFirstRequest_When_ItFails_Then_TheQueryRendersTheError()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));
            mounted.FlushEffectsForTest();

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("first-failed"));
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last(s_rendersA).Status, Last(s_rendersA).Error?.Message),
                Is.EqualTo((QueryStatus.Error, "first-failed")),
                "A subscribed query with no data reports its failure rather than a pending fetch");
        }

        [UnityTest]
        public IEnumerator Given_AQueryFunctionReturningACompletedResult_When_AFrameAdvances_Then_ItRendersTheResult()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mode = FetchMode.CompletedResult;
            using var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));
            mounted.FlushEffectsForTest();

            // Act
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last(s_rendersA).Status, Last(s_rendersA).Data), Is.EqualTo((QueryStatus.Success, 5)),
                "A result the query function hands back already completed settles the entry a frame later");
        });

        [UnityTest]
        public IEnumerator Given_AQueryFunctionThatThrowsBeforeReturning_When_AFrameAdvances_Then_ItRendersTheError()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mode = FetchMode.ThrowsSynchronously;
            using var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));
            mounted.FlushEffectsForTest();

            // Act
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last(s_rendersA).Status, Last(s_rendersA).Error?.Message),
                Is.EqualTo((QueryStatus.Error, "threw-synchronously")),
                "A query function that throws is a failed request, not a crash");
        });

        [UnityTest]
        public IEnumerator Given_AQueryFunctionReturningACompletedFault_When_AFrameAdvances_Then_ItRendersTheError()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            s_mode = FetchMode.CompletedFault;
            using var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));
            mounted.FlushEffectsForTest();

            // Act
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last(s_rendersA).Status, Last(s_rendersA).Error?.Message),
                Is.EqualTo((QueryStatus.Error, "completed-fault")),
                "A fault the query function hands back already completed settles the entry a frame later");
        });

        [Test]
        public void Given_TwoComponentsReadingOneKeyWhoseFunctionReturnsACompletedResult_When_BothMount_Then_OneRequestRuns()
        {
            // Arrange
            s_mode = FetchMode.CompletedResult;
            using var mounted = V.Mount(_root, V.Component(Pair, key: "pair"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("[todos]"),
                "The result arrives after every subscription of the commit, so the second reader joins the request "
                + "rather than finding a stale result and asking again, as v5's does");
        }

        [Test]
        public void Given_TwoComponentsReadingOneKeyWhoseFunctionThrowsBeforeReturning_When_BothMount_Then_OneRequestRuns()
        {
            // Arrange
            s_mode = FetchMode.ThrowsSynchronously;
            using var mounted = V.Mount(_root, V.Component(Pair, key: "pair"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("[todos]"),
                "A failure arrives after the commit's subscriptions as a result does, so the second reader joins the request");
        }

        #endregion

        #region Retaining a result

        [Test]
        public void Given_AResolvedEntryWhoseOnlyReaderUnmounted_When_AReaderMountsAgain_Then_ItsFirstRenderShowsTheData()
        {
            // Arrange
            using var mounted = MountResolvedSoloThenHide(7);
            s_rendersA.Clear();

            // Act
            s_setShowA.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_rendersA[0].Status, s_rendersA[0].Data), Is.EqualTo((QueryStatus.Success, 7)),
                "The entry outlives its readers, so a reader mounting over it renders the result at once");
        }

        [Test]
        public void Given_AResultYoungerThanTheStaleTime_When_AReaderMountsAgain_Then_NoRequestRuns()
        {
            // Arrange — the result lands at one minute, so its age is the clock minus that, not the clock.
            s_staleTime = TimeSpan.FromMinutes(1);
            s_now = TimeSpan.FromMinutes(1);
            using var mounted = MountResolvedSoloThenHide(7);
            s_now = TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(59);

            // Act
            Show(mounted);

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("[todos]"),
                "A fresh result is served from the cache without a request");
        }

        [Test]
        public void Given_AResultExactlyAsOldAsTheStaleTime_When_AReaderMountsAgain_Then_ItIsFetchedAgain()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            s_now = TimeSpan.FromMinutes(1);
            using var mounted = MountResolvedSoloThenHide(7);
            s_now = TimeSpan.FromMinutes(2);

            // Act
            Show(mounted);

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("[todos] [todos]"),
                "A result is stale from the moment its age reaches the stale time, as TanStack's timeUntilStale reaches zero");
        }

        [Test]
        public void Given_AFreshResult_When_AReaderMountsAgain_Then_ItsFirstRenderReportsNoFetch()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            using var mounted = MountResolvedSoloThenHide(7);
            s_rendersA.Clear();

            // Act
            s_setShowA.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_rendersA[0].IsFetching, Is.False,
                "Only a subscription that will fetch is reported ahead of itself");
        }

        [Test]
        public void Given_AFreshEntryInvalidatedWhileUnread_When_AReaderMountsAgain_Then_ItIsFetchedAgain()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            using var mounted = MountResolvedSoloThenHide(7);
            s_client.InvalidateQueries();

            // Act
            Show(mounted);

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("[todos] [todos]"),
                "An invalidated entry is stale whatever its age, and the next reader fetches it");
        }

        [Test]
        public void Given_AReaderRenderedOverARequestInFlight_When_ItLandsBeforeTheSubscription_Then_TheReaderRendersIt()
        {
            // Arrange — the refetch outlives the reader that started it, a new reader renders while it is in
            // flight, and it lands before that reader's subscription runs.
            s_staleTime = TimeSpan.FromMinutes(1);
            using var mounted = MountResolvedSolo(1);
            Last(s_rendersA).Refetch();
            Hide(mounted);
            s_setShowA.Invoke(true);
            mounted.FlushStateForTest();

            // Act
            s_sources[1].TrySetResult(2);
            mounted.FlushEffectsForTest();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last(s_rendersA).Data, Is.EqualTo(2),
                "A change no subscription carried to the reader is caught when it subscribes");
        }

        #endregion

        #region Garbage collection

        [Test]
        public void Given_AnUnreadEntry_When_TheClientSweepsAtAndJustBeforeItsGcTime_Then_ItIsRemovedOnlyAtIt()
        {
            // Arrange — the reader leaves at one minute, so the entry's time runs from there.
            s_now = TimeSpan.FromMinutes(1);
            using var mounted = MountResolvedSoloThenHide(7);

            // Act
            s_now = TimeSpan.FromMinutes(1) + GcTime - TimeSpan.FromTicks(1);
            s_client.InvalidateQueries(Unrelated);
            var keptJustBefore = s_client.Peek<int>(Todos) != null;
            s_now = TimeSpan.FromMinutes(1) + GcTime;
            s_client.InvalidateQueries(Unrelated);
            var removedAt = s_client.Peek<int>(Todos) == null;

            // Assert
            Assert.That((keptJustBefore, removedAt), Is.EqualTo((true, true)),
                "An entry nothing reads is kept until it has gone unread for its gcTime, and removed from then on");
        }

        [Test]
        public void Given_AnExpiredUnreadEntry_When_AnotherQuerySubscribes_Then_TheEntryIsRemoved()
        {
            // Arrange
            using var mounted = MountResolvedSoloThenHide(7);
            s_now = GcTime;

            // Act
            using var other = V.Mount(new VisualElement(), V.Component(ReaderUsers, key: "users"));
            other.FlushEffectsForTest();

            // Assert
            Assert.That(s_client.Peek<int>(Todos), Is.Null, "A subscription sweeps the client");
        }

        [Test]
        public void Given_TwoExpiredUnreadEntries_When_ReadersOfBothMountInOneCommit_Then_TheSecondRendersNoData()
        {
            // Arrange — whichever reader subscribes first sweeps the other's expired entry, so that reader's
            // render has to have read it as absent already.
            using var mounted = MountExpiredBoard();

            // Act
            s_setShowA.Invoke(true);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That((s_rendersB[0].Status, s_rendersB[0].Data), Is.EqualTo((QueryStatus.Pending, 0)),
                "An expired entry reads as absent before a sweep removes it");
        }

        [Test]
        public void Given_AnExpiredUnreadEntry_When_ItsReaderMountsAgain_Then_ItSubscribesToAFreshEntry()
        {
            // Arrange
            using var mounted = MountExpiredBoard();

            // Act
            s_setShowA.Invoke(true);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last(s_rendersA).Status, Last(s_rendersA).Data), Is.EqualTo((QueryStatus.Pending, 0)),
                "Subscribing sweeps before it looks the key up, so the expired entry is not handed back");
        }

        [Test]
        public void Given_AZeroGcTime_When_ItsReaderIsRemountedUnderANewKey_Then_TheNewReaderTakesTheEntryOver()
        {
            // Arrange — the old reader leaves and the new one arrives in the effects of one commit.
            s_client = NewClient(TimeSpan.Zero);
            s_staleTime = TimeSpan.FromMinutes(1);
            using var mounted = V.Mount(_root, V.Component(Remounter, key: "remounter"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(7);
            mounted.FlushStateForTest();

            // Act
            s_setGeneration.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_fetched.Count, Last(s_rendersA).Status, Last(s_rendersA).Data),
                Is.EqualTo((1, QueryStatus.Success, 7)),
                "An entry left in the commit that brings its next reader is kept, as v5's addObserver clears the gc timeout");
        }

        [Test]
        public void Given_StrictModeAndAZeroGcTime_When_AReaderMounts_Then_ItsSecondSubscriptionJoinsTheRequest()
        {
            // Arrange — StrictMode runs a mounting component's effects, cleans them up and runs them again,
            // so the reader subscribes, leaves the entry with nobody reading it, and subscribes again.
            s_client = NewClient(TimeSpan.Zero);
            FiberStrictMode.Enabled = true;
            using var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));

            // Act
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(7);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_fetched.Count, s_tokens[0].IsCancellationRequested, Last(s_rendersA).Data),
                Is.EqualTo((1, false, 7)),
                "The resubscription takes the entry back, however short its gcTime, and joins its request");
        }

        [Test]
        public void Given_StrictModeAndTheDefaultGcTime_When_AReaderMounts_Then_ItsSecondSubscriptionJoinsTheRequest()
        {
            // Arrange
            FiberStrictMode.Enabled = true;
            using var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));

            // Act
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(7);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_fetched.Count, s_tokens[0].IsCancellationRequested, Last(s_rendersA).Data),
                Is.EqualTo((1, false, 7)),
                "The last reader leaving does not cancel the request, so the resubscription joins it");
        }

        [Test]
        public void Given_AnUnreadEntry_When_ItIsInvalidated_Then_NoRequestRuns()
        {
            // Arrange
            using var mounted = MountResolvedSoloThenHide(7);

            // Act
            s_client.InvalidateQueries();

            // Assert
            Assert.That(s_fetched.Count, Is.EqualTo(1), "An entry nothing reads is only marked stale");
        }

        [Test]
        public void Given_AnUnreadEntryWithARequestInFlight_When_ASweepRunsPastItsGcTime_Then_TheRequestIsNotCancelled()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));
            mounted.FlushEffectsForTest();
            Hide(mounted);

            // Act
            s_now = GcTime;
            s_client.InvalidateQueries(Unrelated);

            // Assert
            Assert.That(s_tokens[0].IsCancellationRequested, Is.False,
                "An entry still fetching is not removed, as v5's optionalRemove reschedules the removal instead");
        }

        [Test]
        public void Given_AnUnreadEntryWithARequestInFlight_When_ItIsReadPastItsGcTime_Then_ItIsStillThere()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));
            mounted.FlushEffectsForTest();
            Hide(mounted);

            // Act
            s_now = GcTime;
            var entry = s_client.Peek<int>(Todos);

            // Assert
            Assert.That(entry, Is.Not.Null, "An entry fetching does not read as expired");
        }

        [Test]
        public void Given_AnUnreadEntryWhoseRequestLandedPastItsGcTime_When_ReadJustBeforeAGcTimeSinceItLanded_Then_ItHoldsTheData()
        {
            // Arrange — unread from three minutes, a gcTime of five, and the request lands at nine.
            using var mounted = MountUnreadAtThreeMinutesWhileFetching();
            s_now = TimeSpan.FromMinutes(9);
            s_sources[0].TrySetResult(7);

            // Act
            s_now = TimeSpan.FromMinutes(14) - TimeSpan.FromTicks(1);
            var entry = s_client.Peek<int>(Todos);

            // Assert
            Assert.That(entry?.Data, Is.EqualTo(7),
                "The request settling starts the removal over, a whole gcTime from then, as v5's scheduleGc does");
        }

        // GREEN_ON_BASE(characterization): the base has collected the entry since its first gcTime; this case pins that the removal a settled request starts over still comes, a gcTime after it settled.
        [Test]
        public void Given_AnUnreadEntryWhoseRequestLandedPastItsGcTime_When_AGcTimeHasPassedSinceItLanded_Then_ItIsCollected()
        {
            // Arrange
            using var mounted = MountUnreadAtThreeMinutesWhileFetching();
            s_now = TimeSpan.FromMinutes(9);
            s_sources[0].TrySetResult(7);

            // Act
            s_now = TimeSpan.FromMinutes(14);
            var entry = s_client.Peek<int>(Todos);

            // Assert
            Assert.That(entry, Is.Null, "A gcTime after the request settled, the entry is removed");
        }

        [Test]
        public void Given_AnEntryUnreadFromZeroWhoseRequestLandsAtFour_When_ReadJustBeforeNine_Then_ItHoldsTheData()
        {
            // Arrange
            using var mounted = MountUnreadAtZeroLandingAtFour();

            // Act
            s_now = TimeSpan.FromMinutes(9) - TimeSpan.FromTicks(1);
            var entry = s_client.Peek<int>(Todos);

            // Assert
            Assert.That(entry?.Data, Is.EqualTo(7),
                "The removal runs a gcTime from the settle, not at the gcTime since the entry went unread");
        }

        // GREEN_ON_BASE(characterization): the base has collected the entry since five; this case pins that the removal started over at the settle still comes at nine.
        [Test]
        public void Given_AnEntryUnreadFromZeroWhoseRequestLandsAtFour_When_ReadAtNine_Then_ItIsCollected()
        {
            // Arrange
            using var mounted = MountUnreadAtZeroLandingAtFour();

            // Act
            s_now = TimeSpan.FromMinutes(9);
            var entry = s_client.Peek<int>(Todos);

            // Assert
            Assert.That(entry, Is.Null, "A gcTime after the request settled, the entry is removed");
        }

        [Test]
        public void Given_AnUnreadEntryWhoseRequestLandsExactlyAtItsGcTime_When_ReadAMinuteLater_Then_ItHoldsTheData()
        {
            // Arrange
            using var mounted = MountUnreadAtThreeMinutesWhileFetching();
            s_now = TimeSpan.FromMinutes(8);
            s_sources[0].TrySetResult(7);

            // Act
            s_now = TimeSpan.FromMinutes(9);
            var entry = s_client.Peek<int>(Todos);

            // Assert
            Assert.That(entry?.Data, Is.EqualTo(7), "A request in flight at the gcTime itself puts the removal off");
        }

        // GREEN_ON_BASE(characterization): with a gcTime of zero the base reads the entry as expired at once too; this case pins that a zero gcTime restarted at the settle still removes the entry at once.
        [Test]
        public void Given_AZeroGcTimeAndARequestInFlight_When_ItLandsAfterTheReaderLeft_Then_TheEntryIsCollected()
        {
            // Arrange
            s_client = NewClient(TimeSpan.Zero);
            using var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));
            mounted.FlushEffectsForTest();
            Hide(mounted);
            s_now = TimeSpan.FromSeconds(1);

            // Act
            s_sources[0].TrySetResult(7);

            // Assert
            Assert.That(s_client.Peek<int>(Todos), Is.Null, "With a gcTime of zero the entry goes as soon as its request settles");
        }

        [Test]
        public void Given_OneOfTwoReadersUnmounted_When_ItsGcTimePasses_Then_TheEntryTheOtherReadsStays()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Pair, key: "pair"));
            mounted.FlushEffectsForTest();
            Hide(mounted);

            // Act
            s_now = GcTime;
            s_client.InvalidateQueries(Unrelated);

            // Assert
            Assert.That(s_client.Peek<int>(Todos), Is.Not.Null, "An entry something still reads is never collected");
        }

        [Test]
        public void Given_AnEntryReadAgainAfterGoingUnread_When_ItsOldGcTimePasses_Then_ItStays()
        {
            // Arrange
            using var mounted = MountResolvedSoloThenHide(7);
            s_now = TimeSpan.FromMinutes(1);
            Show(mounted);

            // Act
            s_now = TimeSpan.FromMinutes(10);
            s_client.InvalidateQueries(Unrelated);

            // Assert
            Assert.That(s_client.Peek<int>(Todos), Is.Not.Null,
                "Subscribing again takes the entry off the collection list");
        }

        [Test]
        public void Given_AnEntryALaterQueryAskedToKeepLonger_When_TheFirstQuerysGcTimePasses_Then_ItStays()
        {
            // Arrange — the first reader builds the entry with the client's five minutes, and a reader in a
            // second tree asks for ten.
            s_gcTimeB = TimeSpan.FromMinutes(10);
            using var mounted = MountResolvedSolo(7);
            var other = V.Mount(new VisualElement(), V.Component(ReaderB, key: "b"));
            other.FlushEffectsForTest();
            Hide(mounted);
            other.Dispose();

            // Act
            s_now = GcTime;
            s_client.InvalidateQueries(Unrelated);

            // Assert
            Assert.That(s_client.Peek<int>(Todos), Is.Not.Null, "An entry keeps the longest gcTime asked of it");
        }

        [Test]
        public void Given_AnEntryCollectedEarlier_When_ItsKeyIsReadAgain_Then_TheNewEntryIsNotSweptWithIt()
        {
            // Arrange
            using var mounted = MountResolvedSoloThenHide(7);
            s_now = GcTime;
            s_client.InvalidateQueries(Unrelated);

            // Act
            Show(mounted);

            // Assert
            Assert.That(s_client.Peek<int>(Todos), Is.Not.Null,
                "A collected entry leaves the collection list, so a sweep cannot take its key's next entry");
        }

        [Test]
        public void Given_AClearedUnreadEntry_When_ItsKeysNextEntryOutlivesItsGcTime_Then_TheNextEntryStays()
        {
            // Arrange
            using var mounted = MountResolvedSoloThenHide(7);
            s_client.Clear();
            Show(mounted);

            // Act
            s_now = GcTime;
            s_client.InvalidateQueries(Unrelated);

            // Assert
            Assert.That(s_client.Peek<int>(Todos), Is.Not.Null,
                "Clear empties the collection list, so a sweep cannot take the key's next entry");
        }

        [Test]
        public void Given_AClearedEntryAReaderLeft_When_TheReadersNextEntryOutlivesTheGcTime_Then_TheNextEntryStays()
        {
            // Arrange — the reader moves off the removed entry onto a new one for the same key.
            using var mounted = MountResolvedSolo(7);
            s_client.Clear();
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Act
            s_now = GcTime;
            s_client.InvalidateQueries(Unrelated);

            // Assert
            Assert.That(s_client.Peek<int>(Todos), Is.Not.Null,
                "Leaving a removed entry does not put it on the collection list, where it would take its key with it");
        }

        #endregion

        #region Key and client changes

        [Test]
        public void Given_AMountedQuery_When_ItsKeyChanges_Then_TheNewKeyIsRequested()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Pager, key: "pager"));
            mounted.FlushEffectsForTest();

            // Act
            TurnPage(mounted);

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("[page, 1] [page, 2]"),
                "A new key moves the query to that key's entry, which it fetches");
        }

        [Test]
        public void Given_AMountedQuery_When_ItsKeyChangesAndTheOldGcTimePasses_Then_TheOldEntryIsCollected()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Pager, key: "pager"));
            mounted.FlushEffectsForTest();
            TurnPage(mounted);
            s_sources[0].TrySetResult(1);

            // Act
            s_now = GcTime;
            s_client.InvalidateQueries(Unrelated);

            // Assert
            Assert.That(s_client.Peek<int>(new QueryKey("page", 1)), Is.Null,
                "Moving to a new key stops reading the old one");
        }

        [Test]
        public void Given_AKeyChangeNotYetSubscribed_When_TheOldKeysRequestResolves_Then_TheQueryShowsNoData()
        {
            // Arrange — the render for page 2 has committed, and the effect moving the subscription has not
            // run, so the component is still subscribed to page 1's entry when its request lands.
            using var mounted = V.Mount(_root, V.Component(Pager, key: "pager"));
            mounted.FlushEffectsForTest();
            s_setPage.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_sources[0].TrySetResult(10);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last(s_rendersA).Status, Last(s_rendersA).Data), Is.EqualTo((QueryStatus.Pending, 0)),
                "A render reads the entry its own key names, so page 1's result is never shown as page 2's");
        }

        [Test]
        public void Given_AKeyChangeNotYetSubscribed_When_TheOldEntryIsInvalidated_Then_ItRunsTheOldKeysFunction()
        {
            // Arrange — page 1 holds data, so the invalidation starts a request rather than joining one, and
            // the effect moving the subscription to page 2 has not run.
            using var mounted = V.Mount(_root, V.Component(Pager, key: "pager"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(10);
            mounted.FlushStateForTest();
            s_setPage.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_client.InvalidateQueries();

            // Assert
            Assert.That(Last(s_fetched), Is.EqualTo("[page, 1]"),
                "A render's options reach the subscription only with the commit's effect, as v5's setOptions does");
        }

        [Test]
        public void Given_AQueryOnAProvidedClient_When_TheProviderSwitchesClients_Then_TheQueryReadsTheNewOne()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ClientSwitch, key: "switch"));
            mounted.FlushEffectsForTest();
            var next = NewClient();

            // Act
            s_setClient.Invoke(next);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(next.Peek<int>(Todos), Is.Not.Null, "The query subscribes to the client it now reads");
        }

        #endregion

        #region Invalidation, refetch and clear

        [Test]
        public void Given_EntriesUnderTwoKeys_When_OneKeysPrefixIsInvalidated_Then_OnlyItsEntryIsFetchedAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Board, key: "board"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(1);
            s_sources[1].TrySetResult(2);
            mounted.FlushStateForTest();
            s_fetched.Clear();

            // Act
            s_client.InvalidateQueries(new QueryKey("todos"));

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("[todos, 1]"),
                "A filter key matches the keys it leads, and only those are fetched again");
        }

        [Test]
        public void Given_ARefetchInFlightOverData_When_TheEntryIsInvalidated_Then_TheRefetchIsCancelled()
        {
            // Arrange
            using var mounted = MountResolvedSolo(1);
            Last(s_rendersA).Refetch();

            // Act
            s_client.InvalidateQueries();

            // Assert
            Assert.That(s_tokens[1].IsCancellationRequested, Is.True,
                "A request that may predate the change behind the invalidation is started over");
        }

        [Test]
        public void Given_AFirstRequestInFlight_When_TheEntryIsInvalidated_Then_ItIsJoined()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));
            mounted.FlushEffectsForTest();

            // Act
            s_client.InvalidateQueries();

            // Assert
            Assert.That((s_fetched.Count, s_tokens[0].IsCancellationRequested), Is.EqualTo((1, false)),
                "With no data to protect, the request in flight is joined, as TanStack's cancelRefetch does");
        }

        [Test]
        public void Given_AResolvedQuery_When_RefetchIsCalled_Then_ItIsFetchedAgain()
        {
            // Arrange
            using var mounted = MountResolvedSolo(1);

            // Act
            Last(s_rendersA).Refetch();

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("[todos] [todos]"), "Refetch runs the query function again");
        }

        [Test]
        public void Given_ARefetchInFlightOverData_When_RefetchIsCalledAgain_Then_TheFirstRefetchIsCancelled()
        {
            // Arrange
            using var mounted = MountResolvedSolo(1);
            Last(s_rendersA).Refetch();

            // Act
            Last(s_rendersA).Refetch();

            // Assert
            Assert.That(s_tokens[1].IsCancellationRequested, Is.True, "An explicit refetch starts over");
        }

        [Test]
        public void Given_ARefetchWhoseTokenCallbackThrows_When_ItIsStartedOver_Then_TheThrowIsLoggedAndTheNewRequestRuns()
        {
            // Arrange
            using var mounted = MountResolvedSolo(1);
            s_onToken = token => token.Register(() => throw new InvalidOperationException("query-cancellation-threw"));
            Last(s_rendersA).Refetch();
            ContainedFailureLog.Expect<InvalidOperationException>("QueryClient", "query-cancellation-threw");

            // Act
            Last(s_rendersA).Refetch();

            // Assert
            Assert.That(s_fetched.Count, Is.EqualTo(3),
                "A query function's cancellation callback is reported, and the refetch that cancelled it still starts");
        }

        [Test]
        public void Given_AMountedQuery_When_TheClientIsCleared_Then_ItFetchesIntoANewEntry()
        {
            // Arrange
            using var mounted = MountResolvedSolo(1);

            // Act
            s_client.Clear();
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(string.Join(" ", s_fetched), Is.EqualTo("[todos] [todos]"),
                "Clear removes the entry, and the query moves to a new one, which it fetches");
        }

        [Test]
        public void Given_AClearedClient_When_RefetchIsCalledBeforeTheQueryMovesOn_Then_NoRequestRuns()
        {
            // Arrange
            using var mounted = MountResolvedSolo(1);
            var rendered = Last(s_rendersA);
            s_client.Clear();

            // Act
            rendered.Refetch();

            // Assert
            Assert.That(s_fetched.Count, Is.EqualTo(1), "A removed entry fetches nothing");
        }

        [UnityTest]
        public IEnumerator Given_ARefetchThatCompletedOnAnotherThread_When_ItIsStartedOverBeforeReachingTheMainThread_Then_ItsResultDoesNotLand()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the completion settles on a pool thread, so the continuation carrying it is posted to
            // the main thread rather than run there, and the refetch below cancels a request already complete.
            using var mounted = MountResolvedSolo(1);
            Last(s_rendersA).Refetch();
            var superseded = s_sources[1];
            System.Threading.Tasks.Task.Run(() => superseded.TrySetResult(2)).Wait();

            // Act
            Last(s_rendersA).Refetch();
            s_sources[2].TrySetResult(3);
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            await VelvetTask.Yield();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Last(s_rendersA).Data, Is.EqualTo(3),
                "Only the request in flight settles the entry, whenever another one's completion arrives");
        });

        #endregion

        #region A key rebuilt unequal on every render

        [Test]
        public void Given_AKeyHoldingAnArrayInARecord_When_TwoCommitsRebuildIt_Then_TheEditorWarns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RecordKeyReader, key: "record"));
            mounted.FlushEffectsForTest();
            LogAssert.Expect(LogType.Warning, new Regex("though it prints the same"));

            // Act
            Rerender(mounted);
            Rerender(mounted);

            // Assert — LogAssert.Expect verifies the warning was logged
        }

        [Test]
        public void Given_AKeyHoldingAnArrayInARecord_When_OneCommitRebuildsIt_Then_NothingIsLogged()
        {
            // Arrange — one change between keys printing alike can be a real change.
            using var mounted = V.Mount(_root, V.Component(RecordKeyReader, key: "record"));
            mounted.FlushEffectsForTest();

            // Act
            Rerender(mounted);

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Given_AWarnedRebuiltKey_When_MoreCommitsRebuildIt_Then_TheWarningIsNotRepeated()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RecordKeyReader, key: "record"));
            mounted.FlushEffectsForTest();
            LogAssert.Expect(LogType.Warning, new Regex("though it prints the same"));

            // Act
            Rerender(mounted);
            Rerender(mounted);
            Rerender(mounted);

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Given_AKeyChangingBetweenKeysThatPrintDifferently_When_TwoCommitsChangeIt_Then_NothingIsLogged()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Pager, key: "pager"));
            mounted.FlushEffectsForTest();

            // Act
            TurnPage(mounted);
            s_setPage.Invoke(3);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Given_AnEqualKeyMovedToANewEntry_When_TwoClearsRunInARow_Then_NothingIsLogged()
        {
            // Arrange
            using var mounted = MountResolvedSolo(1);

            // Act
            s_client.Clear();
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
            s_client.Clear();
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        #endregion

        #region Finding the client and checking the options

        [Test]
        public void Given_AClientProvidedThroughTheContext_When_AQueryPassesNone_Then_ItReadsTheProvidedOne()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Provider(QueryClientContext.Ref, s_client,
                children: new VNode[] { V.Component(ProvidedReader, key: "provided") }));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_client.Peek<int>(Todos), Is.Not.Null,
                "The query subscribed to the client its Provider supplied");
        }

        [Test]
        public void Given_NoProviderAndNoClient_When_AQueryRenders_Then_ItThrows()
        {
            // Arrange — with no boundary, a render error reaches the root, which logs it.
            LogAssert.Expect(LogType.Exception, new Regex("UseQuery found no QueryClient"));

            // Act
            using var mounted = V.Mount(_root, V.Component(ProvidedReader, key: "unprovided"));

            // Assert — LogAssert.Expect verifies the exception was logged
        }

        [Test]
        public void Given_NullOptions_When_AQueryRenders_Then_ItThrowsArgumentNull()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, new Regex("ArgumentNullException"));

            // Act
            using var mounted = V.Mount(_root, V.Component(BadOptionsReader, key: "bad"));

            // Assert — LogAssert.Expect verifies the exception was logged
        }

        [Test]
        public void Given_OptionsWithoutAKey_When_AQueryRenders_Then_ItThrowsNamingTheKey()
        {
            // Arrange
            s_badOptions = new QueryOptions<int>(null!, _ => VelvetTask.FromResult(0));
            LogAssert.Expect(LogType.Exception, new Regex("QueryOptions.QueryKey must not be null"));

            // Act
            using var mounted = V.Mount(_root, V.Component(BadOptionsReader, key: "bad"));

            // Assert — LogAssert.Expect verifies the exception was logged
        }

        [Test]
        public void Given_OptionsWithoutAFunction_When_AQueryRenders_Then_ItThrowsNamingTheFunction()
        {
            // Arrange
            s_badOptions = new QueryOptions<int>(Todos, null!);
            LogAssert.Expect(LogType.Exception, new Regex("QueryOptions.QueryFn must not be null"));

            // Act
            using var mounted = V.Mount(_root, V.Component(BadOptionsReader, key: "bad"));

            // Assert — LogAssert.Expect verifies the exception was logged
        }

        #endregion

        #region Components and helpers

        private static QueryClient NewClient() => NewClient(GcTime);

        private static QueryClient NewClient(TimeSpan gcTime)
            => new(new QueryClientOptions { GcTime = gcTime, Clock = () => s_now });

        private static void Rerender(MountedTree mounted)
        {
            s_setTick.Invoke(tick => tick + 1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static QueryOptions<int> Options(params object[] parts)
        {
            var key = new QueryKey(parts);
            return new QueryOptions<int>(key, token => Fetch(key, token))
            {
                StaleTime = s_staleTime,
                Retry = 0,
                NotifyOnChangeProps = QueryProperties.All,
            };
        }

        private static VelvetTask<int> Fetch(QueryKey key, CancellationToken token)
        {
            s_fetched.Add(key.ToString());
            s_tokens.Add(token);
            s_onToken?.Invoke(token);
            switch (s_mode)
            {
                case FetchMode.CompletedResult:
                    return VelvetTask.FromResult(5);
                case FetchMode.ThrowsSynchronously:
                    throw new InvalidOperationException("threw-synchronously");
                case FetchMode.CompletedFault:
                    return VelvetTask.FromException<int>(new InvalidOperationException("completed-fault"));
                default:
                    var source = new VelvetTaskCompletionSource<int>();
                    s_sources.Add(source);
                    return source.Task;
            }
        }

        private static T Last<T>(List<T> renders) => renders[renders.Count - 1];

        private MountedTree MountResolvedSolo(int data)
        {
            var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(data);
            mounted.FlushStateForTest();
            return mounted;
        }

        // Both readers resolved, then hidden at zero, then the clock moved to their gcTime.
        private MountedTree MountExpiredBoard()
        {
            var mounted = V.Mount(_root, V.Component(Board, key: "board"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(1);
            s_sources[1].TrySetResult(2);
            mounted.FlushStateForTest();
            Hide(mounted);
            s_now = GcTime;
            s_rendersA.Clear();
            s_rendersB.Clear();
            return mounted;
        }

        private MountedTree MountUnreadAtZeroLandingAtFour()
        {
            var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));
            mounted.FlushEffectsForTest();
            Hide(mounted);
            s_now = TimeSpan.FromMinutes(4);
            s_sources[0].TrySetResult(7);
            return mounted;
        }

        private MountedTree MountUnreadAtThreeMinutesWhileFetching()
        {
            var mounted = V.Mount(_root, V.Component(Solo, key: "solo"));
            mounted.FlushEffectsForTest();
            s_now = TimeSpan.FromMinutes(3);
            Hide(mounted);
            return mounted;
        }

        private MountedTree MountResolvedSoloThenHide(int data)
        {
            var mounted = MountResolvedSolo(data);
            Hide(mounted);
            return mounted;
        }

        private static void Hide(MountedTree mounted)
        {
            s_setShowA.Invoke(false);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static void Show(MountedTree mounted)
        {
            s_setShowA.Invoke(true);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static void TurnPage(MountedTree mounted)
        {
            s_setPage.Invoke(2);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        [Component]
        private static VNode ReaderA()
        {
            s_rendersA.Add(Hooks.UseQuery(Options("todos"), s_client));
            return V.Label(text: "a");
        }

        [Component]
        private static VNode ReaderB()
        {
            s_rendersB.Add(Hooks.UseQuery(Options("todos") with { GcTime = s_gcTimeB }, s_client));
            return V.Label(text: "b");
        }

        [Component]
        private static VNode ReaderTodo()
        {
            s_rendersA.Add(Hooks.UseQuery(Options("todos", 1), s_client));
            return V.Label(text: "todo");
        }

        [Component]
        private static VNode ReaderUsers()
        {
            s_rendersB.Add(Hooks.UseQuery(Options("users"), s_client));
            return V.Label(text: "users");
        }

        [Component]
        private static VNode ProvidedReader()
        {
            s_rendersA.Add(Hooks.UseQuery(Options("todos")));
            return V.Label(text: "provided");
        }

        [Component]
        private static VNode BadOptionsReader()
        {
            s_rendersA.Add(Hooks.UseQuery(s_badOptions!, s_client));
            return V.Label(text: "bad");
        }

        [Component]
        private static VNode Remounter()
        {
            var (generation, setGeneration) = Hooks.UseState(0);
            s_setGeneration = setGeneration;
            return V.Div(children: new VNode[] { V.Component(ReaderA, key: "a" + generation) });
        }

        [Component]
        private static VNode RecordKeyReader()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            s_rendersA.Add(Hooks.UseQuery(Options("todos", new Filter("open", new[] { "a" })), s_client));
            return V.Label(text: "record");
        }

        [Component]
        private static VNode Pager()
        {
            var (page, setPage) = Hooks.UseState(1);
            s_setPage = setPage;
            s_rendersA.Add(Hooks.UseQuery(Options("page", page), s_client));
            return V.Label(text: "pager");
        }

        [Component]
        private static VNode ClientSwitch()
        {
            var (client, setClient) = Hooks.UseState(s_client);
            s_setClient = setClient;
            return V.Provider(QueryClientContext.Ref, client,
                children: new VNode[] { V.Component(ProvidedReader, key: "provided") });
        }

        [Component]
        private static VNode Pair()
        {
            var (showA, setShowA) = Hooks.UseState(true);
            s_setShowA = setShowA;
            return V.Div(children: new VNode[]
            {
                showA ? V.Component(ReaderA, key: "a") : V.Fragment(Array.Empty<VNode>()),
                V.Component(ReaderB, key: "b"),
            });
        }

        [Component]
        private static VNode Solo()
        {
            var (showA, setShowA) = Hooks.UseState(true);
            s_setShowA = setShowA;
            return V.Div(children: new VNode[]
            {
                showA ? V.Component(ReaderA, key: "a") : V.Fragment(Array.Empty<VNode>()),
            });
        }

        [Component]
        private static VNode Board()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShowA = setShow;
            return V.Div(children: show
                ? new VNode[] { V.Component(ReaderTodo, key: "todo"), V.Component(ReaderUsers, key: "users") }
                : Array.Empty<VNode>());
        }

        #endregion
    }
}
