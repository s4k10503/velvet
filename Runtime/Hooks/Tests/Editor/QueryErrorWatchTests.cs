#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a query throwing its errors re-renders for an error whatever properties the component reads, as
    /// v5's shouldNotifyListeners adds <c>error</c> where <c>throwOnError</c> is set, through
    /// <see cref="Hooks.UseQuery{TQueryFnData, TData}"/> and <see cref="Hooks.UseQueries{TQueryFnData, TData}"/>; and
    /// that UseQueries announces <see cref="QueryCacheEventType.ObserverResultsUpdated"/> for a query's own result.
    /// </summary>
    /// <remarks>
    /// The query function hands back a completion source the case settles itself; nothing retries.
    /// </remarks>
    [TestFixture]
    internal sealed class QueryErrorWatchTests
    {
        private static readonly QueryKey Todos = new("todos");

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<QueryResult<int>> s_renders = new();
        private static readonly List<QueryCacheEvent> s_events = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue, Retry = 0, Clock = () => TimeSpan.Zero });
            s_sources.Clear();
            s_renders.Clear();
            s_events.Clear();
        }

        [Test]
        public void Given_AUseQueryThatThrowsAndReadsOnlyData_When_ARefetchFails_Then_TheErrorBoundaryShowsIt()
        {
            // Arrange
            using var mounted = MountResolved(QueryReader);

            // Act
            s_renders[s_renders.Count - 1].Refetch();
            Settle(mounted, () => s_sources[s_sources.Count - 1].TrySetException(new InvalidOperationException("offline")));

            // Assert
            Assert.That(Texts(), Is.EqualTo("failed:offline"), "v5 watches error where throwOnError is set");
        }

        [Test]
        public void Given_UseQueriesOverAQueryThatThrowsAndReadsOnlyData_When_ARefetchFails_Then_TheErrorBoundaryShowsIt()
        {
            // Arrange
            using var mounted = MountResolved(QueriesReader);

            // Act
            s_renders[s_renders.Count - 1].Refetch();
            Settle(mounted, () => s_sources[s_sources.Count - 1].TrySetException(new InvalidOperationException("offline")));

            // Assert
            Assert.That(Texts(), Is.EqualTo("failed:offline"), "v5 watches error where throwOnError is set");
        }

        [Test]
        public void Given_UseQueriesSelectingTheParity_When_DataOfTheSameParityLands_Then_NoResultsUpdateIsAnnounced()
        {
            // Arrange
            using var mounted = MountResolved(ParityReader);
            s_client.GetQueryCache().Subscribe(s_events.Add);

            // Act
            Settle(mounted, () => s_client.SetQueryData(Todos, 3));

            // Assert
            Assert.That(s_events.Exists(e => e.Type == QueryCacheEventType.ObserverResultsUpdated), Is.False,
                "v5 announces a result only when the observer's selected result changed");
        }

        private MountedTree MountResolved(Func<VNode> reader)
        {
            s_reader = reader;
            var mounted = V.Mount(_root, V.Component(Host, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            Settle(mounted, () => s_sources[0].TrySetResult(1));
            return mounted;
        }

        private static Func<VNode> s_reader = null!;

        private static void Settle(MountedTree mounted, Action action)
        {
            action();
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            mounted.FlushEffectsForTest();
        }

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        private static QueryOptions<int> Options() => new(Todos, () =>
        {
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        })
        {
            ThrowOnError = true,
            StaleTime = TimeSpan.MaxValue,
        };

        private static readonly Func<int, bool> s_odd = data => data % 2 == 1;

        [Component]
        private static VNode QueryReader()
        {
            var result = Hooks.UseQuery(Options(), s_client);
            s_renders.Add(result);
            return V.Label(text: "data:" + result.Data);
        }

        [Component]
        private static VNode QueriesReader()
        {
            var results = Hooks.UseQueries(new[] { Options() }, s_client);
            s_renders.Add(results[0]);
            return V.Label(text: "data:" + results[0].Data);
        }

        [Component]
        private static VNode ParityReader()
        {
            var results = Hooks.UseQueries(new[] { new QueryOptions<int, bool>(Todos, Options().QueryFn) { Select = s_odd, StaleTime = TimeSpan.MaxValue } }, s_client);
            return V.Label(text: "odd:" + results[0].Data);
        }

        [Component]
        private static VNode Host()
            => V.ErrorBoundary(
                error => V.Label(text: "failed:" + error.Message),
                new VNode?[] { V.Component(s_reader, key: "reader") },
                key: "boundary");
    }
}
