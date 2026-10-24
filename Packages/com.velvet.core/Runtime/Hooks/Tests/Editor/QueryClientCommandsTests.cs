#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the imperative cache methods of <see cref="QueryClient"/> that work on entries no component reads,
    /// against TanStack Query v5.
    /// <list type="bullet">
    /// <item><c>getQueryState</c>, <c>getQueriesData</c> and <c>setQueriesData</c> read and write entries by key
    /// and filter.</item>
    /// <item><c>fetchQuery</c> returns fresh data without a request, joins a request in flight, retries nothing by
    /// default and faults with the request's failure; <c>prefetchQuery</c> swallows that failure;
    /// <c>ensureQueryData</c> fetches only an entry with no data.</item>
    /// <item><c>cancelQueries</c> cancels the token of a request and reverts the entry, or leaves the
    /// cancellation as its error; <c>removeQueries</c> and <c>resetQueries</c> drop an entry or its state.</item>
    /// <item><c>refetchQueries</c> fetches an entry fetched before with the options of that fetch and leaves
    /// alone an entry written by hand; <c>invalidateQueries</c> marks entries stale.</item>
    /// <item>Filters select by key prefix or exact key, staleness, fetch status, type and predicate.</item>
    /// </list>
    /// <see cref="QueryClientActiveCommandsTests"/> owns the same methods over entries a mounted query reads.
    /// </summary>
    /// <remarks>
    /// The client's clock is <see cref="s_now"/>, advanced by hand. A query function returns a completion source
    /// the case settles itself, so no case waits for a frame.
    /// </remarks>
    [TestFixture]
    internal sealed class QueryClientCommandsTests
    {
        private static readonly TimeSpan GcTime = TimeSpan.FromMinutes(5);
        private static readonly QueryKey Todos = new("todos");
        private static readonly QueryKey TodoOne = new("todos", 1);
        private static readonly QueryKey Users = new("users");

        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static int s_calls;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<CancellationToken> s_tokens = new();

        [SetUp]
        public void SetUp()
        {
            s_now = TimeSpan.Zero;
            s_calls = 0;
            s_client = new QueryClient(new QueryClientOptions { GcTime = GcTime, Clock = () => s_now });
            s_sources.Clear();
            s_tokens.Clear();
        }

        #region getQueryState

        [Test]
        public void Given_NoEntry_When_GetQueryStateReads_Then_ItIsNull()
        {
            // Act
            var state = s_client.GetQueryState<int>(Todos);

            // Assert
            Assert.That(state, Is.Null);
        }

        [Test]
        public void Given_DataWrittenByHand_When_GetQueryStateReads_Then_ItIsASuccessHoldingIt()
        {
            // Arrange
            s_client.SetQueryData(Todos, 7);

            // Act
            var state = s_client.GetQueryState<int>(Todos)!;

            // Assert
            Assert.That((state.Status, state.HasData, state.Data, state.IsFetching), Is.EqualTo((QueryStatus.Success, true, 7, false)));
        }

        [Test]
        public void Given_ARequestInFlight_When_GetQueryStateReads_Then_ItIsFetching()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            var state = s_client.GetQueryState<int>(Todos)!;

            // Assert
            Assert.That((state.Status, state.IsFetching), Is.EqualTo((QueryStatus.Pending, true)));
        }

        [Test]
        public void Given_AFailedRequest_When_GetQueryStateReads_Then_ItCarriesTheErrorAndTheFailureCount()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_sources[0].TrySetException(new InvalidOperationException("boom"));

            // Act
            var state = s_client.GetQueryState<int>(Todos)!;

            // Assert
            Assert.That((state.Status, state.Error?.Message, state.FailureCount), Is.EqualTo((QueryStatus.Error, "boom", 1)));
        }

        [Test]
        public void Given_DataThatLanded_When_GetQueryStateReads_Then_DataUpdatedAtIsTheClockAtLanding()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_now = TimeSpan.FromSeconds(3);
            s_sources[0].TrySetResult(5);

            // Act
            var state = s_client.GetQueryState<int>(Todos)!;

            // Assert
            Assert.That(state.DataUpdatedAt, Is.EqualTo(TimeSpan.FromSeconds(3)));
        }

        [Test]
        public void Given_AnInvalidatedEntry_When_GetQueryStateReads_Then_ItIsInvalidated()
        {
            // Arrange
            s_client.SetQueryData(Todos, 7);
            s_client.InvalidateQueries(Todos);

            // Act
            var state = s_client.GetQueryState<int>(Todos)!;

            // Assert
            Assert.That(state.IsInvalidated, Is.True);
        }

        [Test]
        public void Given_AnEntryUnreadPastItsGcTime_When_GetQueryStateReads_Then_ItIsNull()
        {
            // Arrange
            s_client.SetQueryData(Todos, 7);
            s_now = GcTime;

            // Act
            var state = s_client.GetQueryState<int>(Todos);

            // Assert
            Assert.That(state, Is.Null, "An entry unread for its gcTime reads as absent, as GetQueryData's does");
        }

        [Test]
        public void Given_AnEntryOfAnotherType_When_GetQueryStateReads_Then_ItThrows()
        {
            // Arrange
            s_client.SetQueryData(Todos, "held");

            // Act
            TestDelegate read = () => s_client.GetQueryState<int>(Todos);

            // Assert
            Assert.That(read, Throws.InvalidOperationException);
        }

        #endregion

        #region fetchQuery, prefetchQuery, ensureQueryData

        [Test]
        public void Given_NoEntry_When_FetchQueryRuns_Then_TheQueryFunctionRuns()
        {
            // Act
            _ = s_client.FetchQuery(Options(Todos));

            // Assert
            Assert.That(s_calls, Is.EqualTo(1));
        }

        [Test]
        public void Given_NoEntry_When_FetchQueryRuns_Then_TheTaskIsPendingUntilTheRequestLands()
        {
            // Act
            var task = s_client.FetchQuery(Options(Todos));

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Pending));
        }

        [Test]
        public void Given_ARequestFromFetchQuery_When_ItLands_Then_TheTaskHoldsTheData()
        {
            // Arrange
            var task = s_client.FetchQuery(Options(Todos));

            // Act
            s_sources[0].TrySetResult(5);

            // Assert
            Assert.That((task.Status, task.GetAwaiter().GetResult()), Is.EqualTo((VelvetTaskStatus.Succeeded, 5)));
        }

        [Test]
        public void Given_ARequestFromFetchQuery_When_ItLands_Then_TheEntryHoldsTheData()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            s_sources[0].TrySetResult(5);

            // Assert
            Assert.That(s_client.GetQueryData<int>(Todos), Is.EqualTo(5));
        }

        [Test]
        public void Given_FreshData_When_FetchQueryRuns_Then_ItReturnsItWithoutARequest()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);

            // Act
            var task = s_client.FetchQuery(Options(Todos, staleTime: TimeSpan.FromMinutes(1)));

            // Assert
            Assert.That((s_calls, task.GetAwaiter().GetResult()), Is.EqualTo((0, 5)));
        }

        [Test]
        public void Given_DataOlderThanTheStaleTime_When_FetchQueryRuns_Then_TheQueryFunctionRuns()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);
            s_now = TimeSpan.FromMinutes(2);

            // Act
            _ = s_client.FetchQuery(Options(Todos, staleTime: TimeSpan.FromMinutes(1)));

            // Assert
            Assert.That(s_calls, Is.EqualTo(1));
        }

        [Test]
        public void Given_DataAndNoStaleTime_When_FetchQueryRuns_Then_TheQueryFunctionRuns()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);

            // Act
            _ = s_client.FetchQuery(Options(Todos));

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A stale time of zero, the default, makes every result stale at once");
        }

        [Test]
        public void Given_ARequestInFlight_When_FetchQueryRunsAgain_Then_ItJoinsIt()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            _ = s_client.FetchQuery(Options(Todos));

            // Assert
            Assert.That(s_calls, Is.EqualTo(1));
        }

        [Test]
        public void Given_ARequestInFlightOverData_When_FetchQueryRunsAgain_Then_ItJoinsIt()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            _ = s_client.FetchQuery(Options(Todos));

            // Assert
            Assert.That((s_calls, s_tokens[0].IsCancellationRequested), Is.EqualTo((1, false)),
                "fetchQuery never cancels a request to start over, which only an explicit refetch does");
        }

        [Test]
        public void Given_TwoCallsJoiningOneRequest_When_ItLands_Then_BothTasksHoldTheData()
        {
            // Arrange
            var first = s_client.FetchQuery(Options(Todos));
            var second = s_client.FetchQuery(Options(Todos));

            // Act
            s_sources[0].TrySetResult(5);

            // Assert
            Assert.That((first.GetAwaiter().GetResult(), second.GetAwaiter().GetResult()), Is.EqualTo((5, 5)));
        }

        [Test]
        public void Given_AFailingRequest_When_FetchQueryRuns_Then_TheTaskFaultsWithTheFailure()
        {
            // Arrange
            var task = s_client.FetchQuery(Options(Todos));

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("boom"));

            // Assert
            Assert.That(() => task.GetAwaiter().GetResult(), Throws.InvalidOperationException.With.Message.EqualTo("boom"));
        }

        [Test]
        public void Given_AFailingRequest_When_FetchQueryRuns_Then_ItIsNotRetriedByDefault()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("boom"));

            // Assert
            Assert.That((s_calls, s_client.GetQueryState<int>(Todos)!.Status), Is.EqualTo((1, QueryStatus.Error)),
                "v5's fetchQuery sets retry to false, where a mounted query takes the client's default of three");
        }

        [Test]
        public void Given_AnEntryOfAnotherType_When_FetchQueryRuns_Then_ItThrows()
        {
            // Arrange
            s_client.SetQueryData(Todos, "held");

            // Act
            TestDelegate fetch = () => s_client.FetchQuery(Options(Todos));

            // Assert
            Assert.That(fetch, Throws.InvalidOperationException);
        }

        [Test]
        public void Given_NullOptions_When_FetchQueryRuns_Then_ItThrowsArgumentNull()
        {
            // Act
            TestDelegate fetch = () => s_client.FetchQuery<int>(null!);

            // Assert
            Assert.That(fetch, Throws.ArgumentNullException);
        }

        [Test]
        public void Given_AnEntryNothingReads_When_ItsGcTimePassesAfterFetchQuery_Then_ItIsGone()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_sources[0].TrySetResult(5);
            s_now = GcTime;

            // Act
            var state = s_client.GetQueryState<int>(Todos);

            // Assert
            Assert.That(state, Is.Null, "The entry fetchQuery made is collected like any other nothing reads");
        }

        [Test]
        public void Given_AFailingRequest_When_PrefetchQueryRuns_Then_TheTaskCompletesWithoutFaulting()
        {
            // Arrange
            var task = s_client.PrefetchQuery(Options(Todos));

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("boom"));

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Succeeded));
        }

        [Test]
        public void Given_APrefetchQuery_When_ItLands_Then_TheEntryHoldsTheData()
        {
            // Arrange
            _ = s_client.PrefetchQuery(Options(Todos));

            // Act
            s_sources[0].TrySetResult(5);

            // Assert
            Assert.That(s_client.GetQueryData<int>(Todos), Is.EqualTo(5));
        }

        [Test]
        public void Given_APrefetchQueryInFlight_When_ItIsCancelled_Then_TheTaskCompletesWithoutFaulting()
        {
            // Arrange
            var task = s_client.PrefetchQuery(Options(Todos));

            // Act
            s_client.CancelQueries(new QueryFilters { QueryKey = Todos });

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Succeeded));
        }

        [Test]
        public void Given_DataInTheEntry_When_EnsureQueryDataRuns_Then_ItReturnsItWithoutARequest()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);

            // Act
            var task = s_client.EnsureQueryData(Options(Todos));

            // Assert
            Assert.That((s_calls, task.GetAwaiter().GetResult()), Is.EqualTo((0, 5)));
        }

        [Test]
        public void Given_NoDataInTheEntry_When_EnsureQueryDataRuns_Then_TheQueryFunctionRuns()
        {
            // Act
            _ = s_client.EnsureQueryData(Options(Todos));

            // Assert
            Assert.That(s_calls, Is.EqualTo(1));
        }

        [Test]
        public void Given_StaleDataInTheEntry_When_EnsureQueryDataRuns_Then_ItDoesNotFetchByDefault()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);
            s_now = TimeSpan.FromMinutes(2);

            // Act
            _ = s_client.EnsureQueryData(Options(Todos, staleTime: TimeSpan.FromMinutes(1)));

            // Assert
            Assert.That(s_calls, Is.EqualTo(0));
        }

        [Test]
        public void Given_StaleDataInTheEntry_When_EnsureQueryDataRunsWithRevalidate_Then_ItFetchesAndReturnsTheDataHeld()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);
            s_now = TimeSpan.FromMinutes(2);

            // Act
            var task = s_client.EnsureQueryData(Options(Todos, staleTime: TimeSpan.FromMinutes(1)), revalidateIfStale: true);

            // Assert
            Assert.That((s_calls, task.GetAwaiter().GetResult()), Is.EqualTo((1, 5)));
        }

        [Test]
        public void Given_FreshDataInTheEntry_When_EnsureQueryDataRunsWithRevalidate_Then_ItDoesNotFetch()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);

            // Act
            _ = s_client.EnsureQueryData(Options(Todos, staleTime: TimeSpan.FromMinutes(1)), revalidateIfStale: true);

            // Assert
            Assert.That(s_calls, Is.EqualTo(0));
        }

        #endregion

        #region cancelQueries

        [Test]
        public void Given_ARequestInFlight_When_CancelQueriesRuns_Then_TheTokenOfTheQueryFunctionIsCancelled()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            s_client.CancelQueries();

            // Assert
            Assert.That(s_tokens[0].IsCancellationRequested, Is.True);
        }

        [Test]
        public void Given_ARequestInFlight_When_CancelQueriesRuns_Then_TheTaskOfFetchQueryIsCancelled()
        {
            // Arrange
            var task = s_client.FetchQuery(Options(Todos));

            // Act
            s_client.CancelQueries();

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Canceled));
        }

        [Test]
        public void Given_ARequestFirstFetchingAnEntry_When_CancelQueriesReverts_Then_TheEntryIsPendingAndIdle()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            s_client.CancelQueries();

            // Assert
            var state = s_client.GetQueryState<int>(Todos)!;
            Assert.That((state.Status, state.HasData, state.IsFetching), Is.EqualTo((QueryStatus.Pending, false, false)));
        }

        [Test]
        public void Given_ARefetchOverData_When_CancelQueriesReverts_Then_TheEntryKeepsItsData()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            s_client.CancelQueries();

            // Assert
            var state = s_client.GetQueryState<int>(Todos)!;
            Assert.That((state.Status, state.Data, state.IsFetching), Is.EqualTo((QueryStatus.Success, 5, false)));
        }

        [Test]
        public void Given_ARefetchOverData_When_CancelQueriesRevertsNothing_Then_TheCancellationIsTheEntrysError()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            s_client.CancelQueries(revert: false);

            // Assert
            var state = s_client.GetQueryState<int>(Todos)!;
            Assert.That((state.Status, state.Data, state.Error is OperationCanceledException), Is.EqualTo((QueryStatus.Error, 5, true)));
        }

        [Test]
        public void Given_AFailedEntryRefetchedAndCancelled_When_CancelQueriesReverts_Then_ItIsTheFailedEntryAgain()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_sources[0].TrySetException(new InvalidOperationException("boom"));
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            s_client.CancelQueries();

            // Assert
            var state = s_client.GetQueryState<int>(Todos)!;
            Assert.That((state.Status, state.Error?.Message, state.FailureCount), Is.EqualTo((QueryStatus.Error, "boom", 1)),
                "The state the request found is what comes back, not the one it set when it started");
        }

        [Test]
        public void Given_ARefetchOverData_When_CancelQueriesRevertsNothing_Then_TheEntryIsInvalidated()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            s_client.CancelQueries(revert: false);

            // Assert
            Assert.That(s_client.GetQueryState<int>(Todos)!.IsInvalidated, Is.True, "The next reader fetches it again");
        }

        [Test]
        public void Given_ARefetchOverData_When_CancelQueriesRevertsNothing_Then_TheCancellationIsTheFailureReasonAndCounts()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            s_client.CancelQueries(revert: false);

            // Assert
            var state = s_client.GetQueryState<int>(Todos)!;
            Assert.That((state.FailureReason is OperationCanceledException, state.FailureCount), Is.EqualTo((true, 1)));
        }

        [Test]
        public void Given_ARequestOfAnEntryNothingReads_When_CancelQueriesRunsLater_Then_TheGcTimeRunsFromTheCancellation()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_now = TimeSpan.FromMinutes(4);
            s_client.CancelQueries();
            s_now = GcTime;

            // Act
            var state = s_client.GetQueryState<int>(Todos);

            // Assert
            Assert.That(state, Is.Not.Null, "A request that ends starts the entry's removal timer over, as a request landing does");
        }

        [Test]
        public void Given_TwoRequests_When_CancelQueriesNamesOneKey_Then_TheOtherKeepsRunning()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            _ = s_client.FetchQuery(Options(Users));

            // Act
            s_client.CancelQueries(new QueryFilters { QueryKey = Todos });

            // Assert
            Assert.That((s_tokens[0].IsCancellationRequested, s_tokens[1].IsCancellationRequested), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_NothingInFlight_When_CancelQueriesRuns_Then_TheTaskIsComplete()
        {
            // Act
            var task = s_client.CancelQueries();

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Succeeded), "An OnMutate can await it");
        }

        #endregion

        #region removeQueries and resetQueries

        [Test]
        public void Given_TwoEntries_When_RemoveQueriesNamesOne_Then_OnlyItIsGone()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(Users, 2);

            // Act
            s_client.RemoveQueries(new QueryFilters { QueryKey = Todos });

            // Assert
            Assert.That((s_client.GetQueryState<int>(Todos), s_client.GetQueryData<int>(Users)), Is.EqualTo(((QueryState<int>?)null, 2)));
        }

        [Test]
        public void Given_AKeyAndOneBelowIt_When_RemoveQueriesIsExact_Then_TheOneBelowStays()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(TodoOne, 2);

            // Act
            s_client.RemoveQueries(new QueryFilters { QueryKey = Todos, Exact = true });

            // Assert
            Assert.That((s_client.GetQueryState<int>(Todos), s_client.GetQueryData<int>(TodoOne)), Is.EqualTo(((QueryState<int>?)null, 2)));
        }

        [Test]
        public void Given_AKeyAndOneBelowIt_When_RemoveQueriesIsNotExact_Then_BothAreGone()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(TodoOne, 2);

            // Act
            s_client.RemoveQueries(new QueryFilters { QueryKey = Todos });

            // Assert
            Assert.That(s_client.GetQueriesData<int>().Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_ARequestInFlight_When_RemoveQueriesRuns_Then_ItsTokenIsCancelled()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            s_client.RemoveQueries();

            // Assert
            Assert.That(s_tokens[0].IsCancellationRequested, Is.True);
        }

        [Test]
        public void Given_AnInactiveEntryRemovedByKey_When_ItsKeysNextEntryOutlivesTheRemovedOnesGcTime_Then_TheNextEntryStays()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.RemoveQueries(new QueryFilters { QueryKey = Todos });
            s_now = GcTime - TimeSpan.FromMinutes(1);
            s_client.SetQueryData(Todos, 2);

            // Act
            s_now = GcTime;
            _ = s_client.GetQueriesData<int>();

            // Assert
            Assert.That(s_client.GetQueryData<int>(Todos), Is.EqualTo(2),
                "A removed entry leaves the collection list, so a sweep cannot take its key's next entry");
        }

        [Test]
        public void Given_NoFilters_When_RemoveQueriesRuns_Then_EveryEntryIsGone()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(Users, 2);

            // Act
            s_client.RemoveQueries();

            // Assert
            Assert.That(s_client.GetQueriesData<int>().Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AnEntryWithData_When_ResetQueriesRuns_Then_ItIsPendingWithNoData()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);
            s_client.InvalidateQueries(Todos);

            // Act
            s_client.ResetQueries(new QueryFilters { QueryKey = Todos });

            // Assert
            var state = s_client.GetQueryState<int>(Todos)!;
            Assert.That((state.Status, state.HasData, state.IsInvalidated), Is.EqualTo((QueryStatus.Pending, false, false)));
        }

        [Test]
        public void Given_AFailedEntry_When_ResetQueriesRuns_Then_ItHoldsNoErrorAndNoFailures()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_sources[0].TrySetException(new InvalidOperationException("boom"));

            // Act
            s_client.ResetQueries();

            // Assert
            var state = s_client.GetQueryState<int>(Todos)!;
            Assert.That((state.Status, state.Error == null, state.FailureCount, state.FailureReason == null),
                Is.EqualTo((QueryStatus.Pending, true, 0, true)));
        }

        [Test]
        public void Given_ARequestInFlight_When_ResetQueriesRuns_Then_ItsTokenIsCancelled()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            s_client.ResetQueries();

            // Assert
            Assert.That(s_tokens[0].IsCancellationRequested, Is.True);
        }

        [Test]
        public void Given_AnEntryNothingReads_When_ResetQueriesRuns_Then_TheResetEntryIsNotFetchedAgain()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_sources[0].TrySetResult(5);

            // Act
            s_client.ResetQueries(new QueryFilters { Type = QueryTypeFilter.Inactive });

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "A reset entry is as one never fetched, which nothing refetches until a query reads it");
        }

        #endregion

        #region refetchQueries and invalidateQueries

        [Test]
        public void Given_AnEntryFetchedBeforeAndNothingReadsIt_When_RefetchQueriesRuns_Then_ItIsFetchedWithTheEarlierOptions()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_sources[0].TrySetResult(5);

            // Act
            _ = s_client.RefetchQueries();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2));
        }

        [Test]
        public void Given_AnEntryWrittenByHand_When_RefetchQueriesRuns_Then_ItIsLeftAlone()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);

            // Act
            _ = s_client.RefetchQueries();

            // Assert
            Assert.That(s_calls, Is.EqualTo(0), "It holds no query function to fetch with");
        }

        [Test]
        public void Given_ARefetch_When_ItIsPending_Then_TheTaskOfRefetchQueriesIsPending()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_sources[0].TrySetResult(5);

            // Act
            var task = s_client.RefetchQueries();

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Pending));
        }

        [Test]
        public void Given_ARefetch_When_ItLands_Then_TheTaskOfRefetchQueriesCompletesAndTheEntryHoldsTheData()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_sources[0].TrySetResult(5);
            var task = s_client.RefetchQueries();

            // Act
            s_sources[1].TrySetResult(6);

            // Assert
            Assert.That((task.Status, s_client.GetQueryData<int>(Todos)), Is.EqualTo((VelvetTaskStatus.Succeeded, 6)));
        }

        [Test]
        public void Given_AFailingRefetch_When_RefetchQueriesRuns_Then_TheTaskCompletesWithoutFaulting()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_sources[0].TrySetResult(5);
            var task = s_client.RefetchQueries();

            // Act
            s_sources[1].TrySetException(new InvalidOperationException("boom"));

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Succeeded));
        }

        [Test]
        public void Given_AFailingRefetch_When_RefetchQueriesThrowsOnError_Then_TheTaskFaults()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_sources[0].TrySetResult(5);
            var task = s_client.RefetchQueries(options: new RefetchOptions { ThrowOnError = true });

            // Act
            s_sources[1].TrySetException(new InvalidOperationException("boom"));

            // Assert
            Assert.That(() => task.GetAwaiter().GetResult(), Throws.InvalidOperationException.With.Message.EqualTo("boom"));
        }

        [Test]
        public void Given_ARefetchOverDataInFlight_When_RefetchQueriesRuns_Then_ItCancelsAndStartsOver()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            _ = s_client.RefetchQueries();

            // Assert
            Assert.That((s_calls, s_tokens[0].IsCancellationRequested), Is.EqualTo((2, true)));
        }

        [Test]
        public void Given_ARefetchOverDataInFlight_When_RefetchQueriesDoesNotCancel_Then_ItJoinsTheRequest()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);
            _ = s_client.FetchQuery(Options(Todos));

            // Act
            _ = s_client.RefetchQueries(options: new RefetchOptions { CancelRefetch = false });

            // Assert
            Assert.That((s_calls, s_tokens[0].IsCancellationRequested), Is.EqualTo((1, false)));
        }

        [Test]
        public void Given_TwoEntries_When_RefetchQueriesNamesOneKey_Then_OnlyItIsFetched()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            _ = s_client.FetchQuery(Options(Users));
            s_sources[0].TrySetResult(1);
            s_sources[1].TrySetResult(2);

            // Act
            _ = s_client.RefetchQueries(new QueryFilters { QueryKey = Users });

            // Assert
            Assert.That(s_calls, Is.EqualTo(3));
        }

        [Test]
        public void Given_AnEntryNothingReads_When_InvalidateQueriesRuns_Then_ItIsMarkedStaleAndNotFetched()
        {
            // Arrange
            s_client.SetQueryData(Todos, 5);

            // Act
            var task = s_client.InvalidateQueries(Todos);

            // Assert
            Assert.That((s_client.GetQueryState<int>(Todos)!.IsInvalidated, s_calls, task.Status),
                Is.EqualTo((true, 0, VelvetTaskStatus.Succeeded)));
        }

        [Test]
        public void Given_FiltersNamingAnExactKey_When_InvalidateQueriesRuns_Then_OnlyThatEntryIsInvalidated()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(TodoOne, 2);

            // Act
            s_client.InvalidateQueries(new QueryFilters { QueryKey = Todos, Exact = true });

            // Assert
            Assert.That((s_client.GetQueryState<int>(Todos)!.IsInvalidated, s_client.GetQueryState<int>(TodoOne)!.IsInvalidated),
                Is.EqualTo((true, false)));
        }

        #endregion

        #region Filters

        [Test]
        public void Given_EntriesUnderOnePrefix_When_FiltersNameThePrefix_Then_BothAreSelected()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(TodoOne, 2);
            s_client.SetQueryData(Users, 3);

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { QueryKey = Todos });

            // Assert
            Assert.That(string.Join(",", selected.Select(pair => pair.Value).OrderBy(value => value)), Is.EqualTo("1,2"));
        }

        [Test]
        public void Given_EntriesUnderOnePrefix_When_FiltersNameTheKeyExactly_Then_OnlyItIsSelected()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(TodoOne, 2);

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { QueryKey = Todos, Exact = true });

            // Assert
            Assert.That(string.Join(",", selected.Select(pair => pair.Value)), Is.EqualTo("1"));
        }

        [Test]
        public void Given_AFreshAndAnInvalidatedEntry_When_FiltersSelectStale_Then_OnlyTheInvalidatedOneIsSelected()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(Users, 2);
            s_client.InvalidateQueries(Users);

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { Stale = true });

            // Assert
            Assert.That(string.Join(",", selected.Select(pair => pair.Value)), Is.EqualTo("2"));
        }

        [Test]
        public void Given_AFreshAndAnInvalidatedEntry_When_FiltersSelectFresh_Then_OnlyTheFreshOneIsSelected()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(Users, 2);
            s_client.InvalidateQueries(Users);

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { Stale = false });

            // Assert
            Assert.That(string.Join(",", selected.Select(pair => pair.Value)), Is.EqualTo("1"));
        }

        [Test]
        public void Given_AFetchingAndAnIdleEntry_When_FiltersSelectFetching_Then_OnlyTheFetchingOneIsSelected()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            _ = s_client.FetchQuery(Options(Users));

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { FetchStatus = QueryFetchStatus.Fetching });

            // Assert
            Assert.That(string.Join(",", selected.Select(pair => pair.Key)), Is.EqualTo("[users]"));
        }

        [Test]
        public void Given_AFetchingAndAnIdleEntry_When_FiltersSelectIdle_Then_OnlyTheIdleOneIsSelected()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            _ = s_client.FetchQuery(Options(Users));

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { FetchStatus = QueryFetchStatus.Idle });

            // Assert
            Assert.That(string.Join(",", selected.Select(pair => pair.Key)), Is.EqualTo("[todos]"));
        }

        [Test]
        public void Given_EntriesNothingReads_When_FiltersSelectTheActiveType_Then_NoneIsSelected()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { Type = QueryTypeFilter.Active });

            // Assert
            Assert.That(selected.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_EntriesNothingReads_When_FiltersSelectTheInactiveType_Then_TheyAreSelected()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { Type = QueryTypeFilter.Inactive });

            // Assert
            Assert.That(selected.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_AnEntryUnreadPastItsGcTime_When_FiltersSelect_Then_ItIsNotSelected()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_now = GcTime;

            // Act
            var selected = s_client.GetQueriesData<int>();

            // Assert
            Assert.That(selected.Count, Is.EqualTo(0), "An entry that has run out reads as absent, as GetQueryData reads it");
        }

        [Test]
        public void Given_APredicate_When_FiltersSelect_Then_ItIsHandedEachEntrysInfo()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(Users, 2);

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { Predicate = info => (int)info.Data! > 1 });

            // Assert
            Assert.That(string.Join(",", selected.Select(pair => pair.Value)), Is.EqualTo("2"));
        }

        [Test]
        public void Given_FiltersThatDisagree_When_Selecting_Then_NothingIsSelected()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { QueryKey = Todos, Stale = true });

            // Assert
            Assert.That(selected.Count, Is.EqualTo(0), "Every property that is set has to match");
        }

        #endregion

        #region getQueriesData and setQueriesData

        [Test]
        public void Given_EntriesOfTwoTypes_When_GetQueriesDataReads_Then_ThoseOfAnotherTypeAreLeftOut()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(Users, "named");

            // Act
            var selected = s_client.GetQueriesData<int>();

            // Assert
            Assert.That(string.Join(",", selected.Select(pair => pair.Value)), Is.EqualTo("1"));
        }

        [Test]
        public void Given_TwoEntries_When_SetQueriesDataRuns_Then_EachIsHandedItsDataAndWritten()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(TodoOne, 2);

            // Act
            s_client.SetQueriesData<int>(new QueryFilters { QueryKey = Todos }, old => old + 10);

            // Assert
            Assert.That((s_client.GetQueryData<int>(Todos), s_client.GetQueryData<int>(TodoOne)), Is.EqualTo((11, 12)));
        }

        [Test]
        public void Given_TwoEntries_When_SetQueriesDataRuns_Then_ItReturnsTheKeyAndDataOfEachWrite()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(TodoOne, 2);

            // Act
            var written = s_client.SetQueriesData<int>(new QueryFilters { QueryKey = Todos }, old => old + 10);

            // Assert
            Assert.That(string.Join(",", written.Select(pair => pair.Value).OrderBy(value => value)), Is.EqualTo("11,12"));
        }

        [Test]
        public void Given_AnUpdaterReturningNull_When_SetQueriesDataRuns_Then_TheEntryIsLeftAsItWas()
        {
            // Arrange
            s_client.SetQueryData(Todos, "held");

            // Act
            s_client.SetQueriesData<string>(null, _ => null!);

            // Assert
            Assert.That(s_client.GetQueryData<string>(Todos), Is.EqualTo("held"));
        }

        [Test]
        public void Given_NullUpdater_When_SetQueriesDataRuns_Then_ItThrowsArgumentNull()
        {
            // Act
            TestDelegate write = () => s_client.SetQueriesData<int>(null, null!);

            // Assert
            Assert.That(write, Throws.ArgumentNullException);
        }

        #region Waiting tasks

        [Test]
        public void Given_ARequestThatLanded_When_ItsEntryIsRead_Then_NoTaskIsLeftWaiting()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_sources[0].TrySetResult(5);

            // Act
            var waiting = Waiting(Todos);

            // Assert
            Assert.That(waiting, Is.Null, "A settled request lets go of the tasks that waited for it");
        }

        [Test]
        public void Given_ARequestThatFailed_When_ItsEntryIsRead_Then_NoTaskIsLeftWaiting()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_sources[0].TrySetException(new InvalidOperationException("boom"));

            // Act
            var waiting = Waiting(Todos);

            // Assert
            Assert.That(waiting, Is.Null);
        }

        [Test]
        public void Given_ARequestThatWasCancelled_When_ItsEntryIsRead_Then_NoTaskIsLeftWaiting()
        {
            // Arrange
            _ = s_client.FetchQuery(Options(Todos));
            s_client.CancelQueries();

            // Act
            var waiting = Waiting(Todos);

            // Assert
            Assert.That(waiting, Is.Null);
        }

        // The tasks an entry holds for its request in flight, read where the entry keeps them.
        private static object? Waiting(QueryKey key) =>
            typeof(QueryEntry<int>)
                .GetField("_waiters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(s_client.Peek<int>(key));

        #endregion

        #endregion

        private static QueryOptions<int> Options(QueryKey key, TimeSpan? staleTime = null)
            => new(key, Fetch) { StaleTime = staleTime };

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            s_calls++;
            s_tokens.Add(token);
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }
    }
}
