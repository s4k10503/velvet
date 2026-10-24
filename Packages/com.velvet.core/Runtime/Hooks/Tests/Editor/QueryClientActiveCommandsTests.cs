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
    /// Specifies the imperative cache methods of <see cref="QueryClient"/> over an entry a mounted query reads,
    /// against TanStack Query v5.
    /// <list type="bullet">
    /// <item><c>invalidateQueries</c> and <c>refetchQueries</c> return a task that completes once the requests they
    /// started have settled, and cancel a request over data or join it as their options say.</item>
    /// <item>The filter types separate the entries a query reads from the rest, and staleness is the stale time of
    /// the query reading the entry.</item>
    /// <item><c>resetQueries</c> empties the entry and fetches it again, <c>removeQueries</c> sends the query to a
    /// new entry, and <c>cancelQueries</c> leaves the query as it stood before the request.</item>
    /// <item><c>fetchQuery</c> joins the request the query started.</item>
    /// </list>
    /// <see cref="QueryClientCommandsTests"/> owns the same methods over entries nothing reads.
    /// </summary>
    /// <remarks>
    /// The query function hands back a completion source the case settles itself and retries nothing.
    /// </remarks>
    [TestFixture]
    internal sealed class QueryClientActiveCommandsTests
    {
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan? s_staleTime;
        private static int s_calls;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<CancellationToken> s_tokens = new();
        private static readonly List<QueryResult<int>> s_renders = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_staleTime = null;
            s_calls = 0;
            s_client = new QueryClient();
            s_sources.Clear();
            s_tokens.Clear();
            s_renders.Clear();
        }

        [Test]
        public void Given_AResolvedMountedQuery_When_InvalidateQueriesRuns_Then_TheTaskIsPendingUntilTheRefetchLands()
        {
            // Arrange
            using var mounted = MountResolved(1);

            // Act
            var task = s_client.InvalidateQueries(Todos);

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Pending), "A mutation callback can return it to wait for the refetch");
        }

        [Test]
        public void Given_AResolvedMountedQuery_When_TheRefetchOfInvalidateQueriesLands_Then_TheTaskCompletes()
        {
            // Arrange
            using var mounted = MountResolved(1);
            var task = s_client.InvalidateQueries(Todos);

            // Act
            s_sources[1].TrySetResult(2);

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Succeeded));
        }

        [Test]
        public void Given_AResolvedMountedQuery_When_TheRefetchOfInvalidateQueriesFails_Then_TheTaskStillCompletes()
        {
            // Arrange
            using var mounted = MountResolved(1);
            var task = s_client.InvalidateQueries(Todos);

            // Act
            s_sources[1].TrySetException(new InvalidOperationException("boom"));

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Succeeded), "v5's invalidateQueries swallows the failure unless asked to throw");
        }

        [Test]
        public void Given_AResolvedMountedQuery_When_InvalidateQueriesThrowsOnError_Then_TheTaskFaultsWithTheFailure()
        {
            // Arrange
            using var mounted = MountResolved(1);
            var task = s_client.InvalidateQueries(new QueryFilters { QueryKey = Todos }, new RefetchOptions { ThrowOnError = true });

            // Act
            s_sources[1].TrySetException(new InvalidOperationException("boom"));

            // Assert
            Assert.That(() => task.GetAwaiter().GetResult(), Throws.InvalidOperationException.With.Message.EqualTo("boom"));
        }

        [Test]
        public void Given_ARefetchInFlight_When_InvalidateQueriesRuns_Then_ItStartsOver()
        {
            // Arrange
            using var mounted = MountResolved(1);
            Last().Refetch();

            // Act
            s_client.InvalidateQueries(Todos);

            // Assert
            Assert.That(s_calls, Is.EqualTo(3));
        }

        [Test]
        public void Given_ARefetchInFlight_When_InvalidateQueriesDoesNotCancel_Then_ItJoinsTheRequest()
        {
            // Arrange
            using var mounted = MountResolved(1);
            Last().Refetch();

            // Act
            s_client.InvalidateQueries(new QueryFilters { QueryKey = Todos }, new RefetchOptions { CancelRefetch = false });

            // Assert
            Assert.That(s_calls, Is.EqualTo(2));
        }

        [Test]
        public void Given_AMountedQuery_When_RefetchQueriesSelectsTheActiveType_Then_ItFetchesAgain()
        {
            // Arrange
            using var mounted = MountResolved(1);

            // Act
            _ = s_client.RefetchQueries(new QueryFilters { Type = QueryTypeFilter.Active });

            // Assert
            Assert.That(s_calls, Is.EqualTo(2));
        }

        [Test]
        public void Given_AMountedQuery_When_RefetchQueriesSelectsTheInactiveType_Then_NothingIsFetched()
        {
            // Arrange
            using var mounted = MountResolved(1);

            // Act
            _ = s_client.RefetchQueries(new QueryFilters { Type = QueryTypeFilter.Inactive });

            // Assert
            Assert.That(s_calls, Is.EqualTo(1));
        }

        [Test]
        public void Given_FreshDataInAMountedQuery_When_RefetchQueriesSelectsStaleEntries_Then_NothingIsFetched()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            using var mounted = MountResolved(1);

            // Act
            _ = s_client.RefetchQueries(new QueryFilters { Stale = true });

            // Assert
            Assert.That(s_calls, Is.EqualTo(1), "Staleness is the stale time of the query reading the entry");
        }

        [Test]
        public void Given_FreshDataInAMountedQuery_When_RefetchQueriesSelectsFreshEntries_Then_ItFetchesAgain()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            using var mounted = MountResolved(1);

            // Act
            _ = s_client.RefetchQueries(new QueryFilters { Stale = false });

            // Assert
            Assert.That(s_calls, Is.EqualTo(2));
        }

        [Test]
        public void Given_AMountedQuery_When_ResetQueriesRuns_Then_ItEmptiesTheEntryAndFetchesAgain()
        {
            // Arrange
            using var mounted = MountResolved(1);

            // Act
            _ = s_client.ResetQueries(new QueryFilters { QueryKey = Todos });
            mounted.FlushStateForTest();

            // Assert
            Assert.That((s_calls, Last().Status, Last().IsFetching), Is.EqualTo((2, QueryStatus.Pending, true)));
        }

        [Test]
        public void Given_AMountedQuery_When_ResetQueriesRuns_Then_TheTaskCompletesOnceTheNewRequestLands()
        {
            // Arrange
            using var mounted = MountResolved(1);
            var task = s_client.ResetQueries();

            // Act
            s_sources[1].TrySetResult(9);

            // Assert
            Assert.That((task.Status, s_client.GetQueryData<int>(Todos)), Is.EqualTo((VelvetTaskStatus.Succeeded, 9)));
        }

        [Test]
        public void Given_AMountedQuery_When_RemoveQueriesRuns_Then_TheEntryIsGone()
        {
            // Arrange
            using var mounted = MountResolved(1);

            // Act
            s_client.RemoveQueries(new QueryFilters { QueryKey = Todos });

            // Assert
            Assert.That(s_client.GetQueryState<int>(Todos), Is.Null);
        }

        [Test]
        public void Given_AMountedQuery_When_RemoveQueriesRuns_Then_ItFetchesIntoANewEntry()
        {
            // Arrange
            using var mounted = MountResolved(1);

            // Act
            s_client.RemoveQueries(new QueryFilters { QueryKey = Todos });
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_calls, Is.EqualTo(2), "The query moves to a new entry, which it fetches, as after Clear");
        }

        [Test]
        public void Given_AMountedQueryWaitingOnItsFirstRequest_When_CancelQueriesRuns_Then_ItIsPendingAndNotFetching()
        {
            // Arrange
            using var mounted = Mount();

            // Act
            s_client.CancelQueries(new QueryFilters { QueryKey = Todos });
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last().Status, Last().IsFetching), Is.EqualTo((QueryStatus.Pending, false)));
        }

        [Test]
        public void Given_AMountedQueryRefetching_When_CancelQueriesRuns_Then_ItKeepsItsDataAndStopsFetching()
        {
            // Arrange
            using var mounted = MountResolved(1);
            Last().Refetch();
            mounted.FlushStateForTest();

            // Act
            s_client.CancelQueries();
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last().Status, Last().Data, Last().IsFetching), Is.EqualTo((QueryStatus.Success, 1, false)));
        }

        [Test]
        public void Given_AMountedQuery_When_GetQueriesDataSelectsTheActiveType_Then_ItsEntryIsSelected()
        {
            // Arrange
            using var mounted = MountResolved(1);

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { Type = QueryTypeFilter.Active });

            // Assert
            Assert.That(selected.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_AMountedQuery_When_GetQueriesDataSelectsTheInactiveType_Then_NoEntryIsSelected()
        {
            // Arrange
            using var mounted = MountResolved(1);

            // Act
            var selected = s_client.GetQueriesData<int>(new QueryFilters { Type = QueryTypeFilter.Inactive });

            // Assert
            Assert.That(selected.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AMountedQueryWaitingOnItsFirstRequest_When_FetchQueryRuns_Then_ItJoinsTheRequestAndTheTaskHoldsItsData()
        {
            // Arrange
            using var mounted = Mount();
            var task = s_client.FetchQuery(new QueryOptions<int>(Todos, Fetch));

            // Act
            s_sources[0].TrySetResult(4);

            // Assert
            Assert.That((s_calls, task.GetAwaiter().GetResult()), Is.EqualTo((1, 4)));
        }

        private MountedTree Mount()
        {
            var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            return mounted;
        }

        private MountedTree MountResolved(int data)
        {
            var mounted = Mount();
            s_sources[0].TrySetResult(data);
            mounted.FlushStateForTest();
            return mounted;
        }

        private static QueryResult<int> Last() => s_renders[s_renders.Count - 1];

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            s_calls++;
            s_tokens.Add(token);
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }

        [Component]
        private static VNode Reader()
        {
            s_renders.Add(Hooks.UseQuery(
                new QueryOptions<int>(Todos, Fetch)
                {
                    StaleTime = s_staleTime,
                    Retry = 0,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "reader");
        }
    }
}
