#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="QueryOptions{TQueryFnData, TData}.InitialData"/> and its updated-at forms, TanStack
    /// Query's <c>initialData</c> and <c>initialDataUpdatedAt</c>.
    /// <list type="bullet">
    /// <item>A render that finds no entry shows the initial data as a success, and the function is asked once.</item>
    /// <item>The data dates from the updated-at value, or from its function where both are set, and the stale
    /// time judges it as it judges fetched data.</item>
    /// <item>An entry with no data takes the initial data when a query hands it its options, and
    /// <c>FetchQuery</c> returns initial data that is fresh without fetching.</item>
    /// <item><c>ResetQueries</c> returns the entry to its initial data, and an entry nothing reads that holds only
    /// initial data counts as never fetched, so <c>RefetchQueries</c> leaves it alone.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The client's clock is <see cref="s_now"/>, advanced by hand. The initial data is 3 and the query function's
    /// data is whatever the case lands; <see cref="s_calls"/> counts the requests that ran.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueryInitialDataTests
    {
        private static readonly QueryKey Todos = new("todos");
        private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static int s_calls;
        private static int s_initialDataCalls;
        private static TimeSpan s_staleTime;
        private static TimeSpan? s_updatedAt;
        private static Func<TimeSpan?>? s_updatedAtFn;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<QueryResult<int>> s_renders = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.FromMinutes(10);
            s_calls = 0;
            s_initialDataCalls = 0;
            s_staleTime = Minute;
            s_updatedAt = null;
            s_updatedAtFn = null;
            s_sources.Clear();
            s_renders.Clear();
            s_client = new QueryClient(new QueryClientOptions
            {
                GcTime = TimeSpan.MaxValue,
                Retry = 0,
                Clock = () => s_now,
            });
        }

        [Test]
        public void Given_InitialData_When_TheFirstRenderFindsNoEntry_Then_TheResultIsASuccess()
        {
            // Arrange — nothing else has written the key.

            // Act
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));

            // Assert
            Assert.That(s_renders[0].Status, Is.EqualTo(QueryStatus.Success),
                "The render builds the entry with the initial data, as v5's getOptimisticResult builds the query");
        }

        [Test]
        public void Given_InitialData_When_TheFirstRenderFindsNoEntry_Then_TheResultHoldsIt()
        {
            // Arrange — nothing else has written the key.

            // Act
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));

            // Assert
            Assert.That(s_renders[0].Data, Is.EqualTo(3), "The initial data is the entry's data from the first render");
        }

        [Test]
        public void Given_InitialData_When_TheQueryMountsAndCommits_Then_ItIsAskedOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_initialDataCalls, Is.EqualTo(1), "An entry that holds the initial data does not ask for it again");
        }

        [Test]
        public void Given_InitialDataDatedNow_When_TheQueryMounts_Then_NothingIsFetched()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(0), "Initial data with no updated-at dates from the moment it is taken");
        }

        [Test]
        public void Given_InitialDataUpdatedLongerAgoThanTheStaleTime_When_TheQueryMounts_Then_ItIsFetched()
        {
            // Arrange
            s_updatedAt = s_now - Minute - Minute;
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "Initial data is as old as InitialDataUpdatedAt says");
        }

        [Test]
        public void Given_BothUpdatedAtForms_When_TheQueryMounts_Then_TheFunctionDatesTheData()
        {
            // Arrange — the value is fresh and the function's reading is stale.
            s_updatedAt = s_now;
            s_updatedAtFn = () => s_now - Minute - Minute;
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "InitialDataUpdatedAtFn replaces InitialDataUpdatedAt");
        }

        [Test]
        public void Given_AnEntryThatFailedWithNoData_When_AQueryWithInitialDataMounts_Then_TheEntryTakesIt()
        {
            // Arrange
            s_client.PrefetchQuery(new QueryOptions<int>(Todos, Fetch)).Forget();
            s_sources[0].TrySetException(new InvalidOperationException("offline"));
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_client.GetQueryData<int>(Todos), Is.EqualTo(3),
                "Handing an entry with no data the options gives it their initial data, as v5's setOptions does");
        }

        [Test]
        public void Given_FreshInitialData_When_FetchQueryRuns_Then_NothingIsFetched()
        {
            // Arrange — no entry exists yet.

            // Act
            s_client.FetchQuery(Options()).Forget();

            // Assert
            Assert.That(s_calls, Is.EqualTo(0), "FetchQuery builds the entry with its initial data and finds it fresh");
        }

        [Test]
        public void Given_AnEntryThatTookInitialDataAndThenFetched_When_ResetQueriesRuns_Then_ItHoldsTheInitialDataAgain()
        {
            // Arrange
            s_staleTime = TimeSpan.Zero;
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            s_sources[0].TrySetResult(9);

            // Act
            s_client.ResetQueries().Forget();

            // Assert
            Assert.That(s_client.GetQueryData<int>(Todos), Is.EqualTo(3), "A reset returns the entry to the state it was made in");
        }

        [Test]
        public void Given_AnUnreadEntryHoldingOnlyInitialData_When_RefetchQueriesRuns_Then_NothingIsFetched()
        {
            // Arrange
            s_client.PrefetchQuery(Options()).Forget();

            // Act
            s_client.RefetchQueries().Forget();

            // Assert
            Assert.That(s_calls, Is.EqualTo(0),
                "Initial data is not a fetch, so an entry nothing reads that holds only it is never-fetched to v5's isDisabled");
        }

        private static QueryOptions<int> Options()
            => new(Todos, Fetch)
            {
                InitialData = InitialData,
                InitialDataUpdatedAt = s_updatedAt,
                InitialDataUpdatedAtFn = s_updatedAtFn,
                StaleTime = s_staleTime,
                NotifyOnChangeProps = QueryProperties.All,
            };

        private static int InitialData()
        {
            s_initialDataCalls++;
            return 3;
        }

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            s_calls++;
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }

        [Component]
        private static VNode Reader()
        {
            s_renders.Add(Hooks.UseQuery(Options(), s_client));
            return V.Label(text: "reader");
        }
    }
}
