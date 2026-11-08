#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="Hooks.UseQueries{TQueryFnData, TData}"/>, its combine form and
    /// <see cref="Hooks.UseSuspenseQueries{TQueryFnData, TData}"/>, TanStack Query's <c>useQueries</c> and
    /// <c>useSuspenseQueries</c>.
    /// <list type="bullet">
    /// <item>Each query is read as <see cref="Hooks.UseQuery{TQueryFnData, TData}"/> reads it, the results in the
    /// list's order; a query keeps its observer while some query reads its entry, whatever its position, and one
    /// that leaves the list leaves its entry.</item>
    /// <item>A property read from one result is tracked for every result.</item>
    /// <item>Combine runs again only when a result changed, and its output keeps its instance where it is deeply
    /// equal, so the component does not re-render for a change the combine does not see.</item>
    /// <item>The first error <c>ThrowOnError</c> accepts is thrown; the suspending form waits for every pending
    /// query.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Each key's request is a completion source the case settles itself; nothing retries and data stays fresh.
    /// </remarks>
    [TestFixture]
    internal sealed class UseQueriesTests
    {
        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static readonly Dictionary<string, List<VelvetTaskCompletionSource<int>>> s_sources = new();
        private static string[] s_keys = Array.Empty<string>();
        private static readonly List<IReadOnlyList<QueryResult<int>>> s_renders = new();
        private static readonly List<object?> s_combined = new();
        private static int s_combineCalls;
        private static bool s_throwOnError;
        private static StateUpdater<int> s_setTick;
        private static int s_warnings;
        private static string s_typed = string.Empty;
        private static readonly List<VelvetTaskCompletionSource<string>> s_textSources = new();


        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue, Retry = 0, Clock = () => TimeSpan.Zero });
            s_sources.Clear();
            s_keys = new[] { "a", "b" };
            s_renders.Clear();
            s_combined.Clear();
            s_combineCalls = 0;
            s_warnings = 0;
            s_typed = string.Empty;
            s_textSources.Clear();
            s_throwOnError = false;
            s_setTick = default;
        }

        #region Results

        [Test]
        public void Given_TwoQueries_When_TheirRequestsLand_Then_TheResultsFollowTheListsOrder()
        {
            // Arrange
            using var mounted = Mount(Reader);

            // Act
            Settle(mounted, () =>
            {
                Source("b").TrySetResult(2);
                Source("a").TrySetResult(1);
            });

            // Assert
            Assert.That(string.Join(",", Last().Select(result => result.Data)), Is.EqualTo("1,2"));
        }

        [Test]
        public void Given_TwoQueries_When_TheComponentMounts_Then_EachStartsItsOwnRequest()
        {
            // Act
            using var mounted = Mount(Reader);

            // Assert
            Assert.That(string.Join(",", s_sources.Keys.OrderBy(key => key)), Is.EqualTo("a,b"));
        }

        [Test]
        public void Given_MountedQueries_When_AQueryJoinsTheList_Then_OnlyItIsFetched()
        {
            // Arrange
            using var mounted = Mount(Reader);
            Settle(mounted, () =>
            {
                Source("a").TrySetResult(1);
                Source("b").TrySetResult(2);
            });

            // Act
            s_keys = new[] { "a", "b", "c" };
            Rerender(mounted);

            // Assert
            Assert.That(s_sources.Values.Sum(list => list.Count), Is.EqualTo(3), "The queries already held keep their observers");
        }

        [Test]
        public void Given_MountedQueries_When_TheListIsReordered_Then_NothingIsFetchedAgain()
        {
            // Arrange
            using var mounted = Mount(Reader);
            Settle(mounted, () =>
            {
                Source("a").TrySetResult(1);
                Source("b").TrySetResult(2);
            });

            // Act
            s_keys = new[] { "b", "a" };
            Rerender(mounted);

            // Assert
            Assert.That(s_sources.Values.Sum(list => list.Count), Is.EqualTo(2),
                "v5 matches an observer by the entry it reads, not by its position");
        }

        [Test]
        public void Given_MountedQueries_When_AQueryLeavesTheList_Then_ItsEntryLosesItsReader()
        {
            // Arrange
            using var mounted = Mount(Reader);

            // Act
            s_keys = new[] { "a" };
            Rerender(mounted);

            // Assert
            Assert.That(s_client.GetQueryCache().Find(new QueryFilters { QueryKey = new QueryKey("b") })!.GetObserversCount(),
                Is.EqualTo(0));
        }

        [Test]
        public void Given_MountedQueries_When_TheComponentUnmounts_Then_EveryEntryLosesItsReader()
        {
            // Arrange
            var mounted = Mount(Reader);

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(s_client.GetQueryCache().FindAll().Sum(query => query.GetObserversCount()), Is.EqualTo(0));
        }

        [Test]
        public void Given_AComponentReadingDataOnlyFromTheFirstResult_When_OnlyTheSecondsDataChanges_Then_ItRendersAgain()
        {
            // Arrange
            using var mounted = Mount(FirstDataReader);
            Settle(mounted, () =>
            {
                Source("a").TrySetResult(1);
                Source("b").TrySetResult(2);
            });
            var renders = s_renders.Count;

            // Act
            Settle(mounted, () => s_client.SetQueryData(new QueryKey("b"), 9));

            // Assert
            Assert.That(s_renders.Count, Is.GreaterThan(renders),
                "A property read from one result is tracked for all of them, as v5's trackResult shares one set");
        }

        [Test]
        public void Given_AQueryWhoseErrorIsThrown_When_ItsRequestFails_Then_TheErrorBoundaryShowsIt()
        {
            // Arrange
            s_throwOnError = true;
            using var mounted = Mount(Reader);

            // Act
            Settle(mounted, () => Source("b").TrySetException(new InvalidOperationException("offline")));

            // Assert
            Assert.That(Texts(), Is.EqualTo("failed:offline"));
        }

        #endregion

        #region Combine

        [Test]
        public void Given_ACombine_When_TheRequestsLand_Then_TheHookReturnsWhatItMakesOfTheResults()
        {
            // Arrange
            using var mounted = Mount(SumReader);

            // Act
            Settle(mounted, () =>
            {
                Source("a").TrySetResult(1);
                Source("b").TrySetResult(2);
            });

            // Assert
            Assert.That(s_combined[s_combined.Count - 1], Is.EqualTo(3));
        }

        [Test]
        public void Given_ACombine_When_TheComponentRendersWithNoResultChanged_Then_ItDoesNotRunAgain()
        {
            // Arrange
            using var mounted = Mount(SumReader);
            Settle(mounted, () =>
            {
                Source("a").TrySetResult(1);
                Source("b").TrySetResult(2);
            });
            var calls = s_combineCalls;

            // Act
            Rerender(mounted);

            // Assert
            Assert.That(s_combineCalls, Is.EqualTo(calls), "v5 runs combine again only for new results, keys or function");
        }

        [Test]
        public void Given_ACombineOfWhetherEachHasData_When_AQuerysDataChangesButStillExists_Then_TheComponentDoesNotRenderAgain()
        {
            // Arrange
            using var mounted = Mount(StatusReader);
            Settle(mounted, () =>
            {
                Source("a").TrySetResult(1);
                Source("b").TrySetResult(2);
            });
            var renders = s_combined.Count;

            // Act
            Settle(mounted, () => s_client.SetQueryData(new QueryKey("a"), 5));

            // Assert
            Assert.That(s_combined.Count, Is.EqualTo(renders),
                "What combine returns is shared with what it returned before, and an equal value re-renders nothing");
        }

        #endregion

        #region Queries of their own types

        [Test]
        public void Given_QueriesOfTwoDataTypes_When_TheirRequestsLand_Then_EachResultKeepsItsType()
        {
            // Arrange
            using var mounted = Mount(TypedReader);

            // Act
            Settle(mounted, () =>
            {
                Source("a").TrySetResult(1);
                s_textSources[0].TrySetResult("b");
            });

            // Assert
            Assert.That(s_typed, Is.EqualTo("1:b"), "v5 types each query of the array by its own options");
        }

        [Test]
        public void Given_QueriesOfTwoDataTypes_When_ACombineReadsThem_Then_ItIsHandedEachResultTyped()
        {
            // Arrange
            using var mounted = Mount(TypedCombineReader);

            // Act
            Settle(mounted, () =>
            {
                Source("a").TrySetResult(1);
                s_textSources[0].TrySetResult("b");
            });

            // Assert
            Assert.That(s_typed, Is.EqualTo("1+b"));
        }

        #endregion

        #region Duplicates

        [Test]
        public void Given_TwoQueriesOverOneEntry_When_TheListCommitsTwice_Then_ItWarnsAtEachCommit()
        {
            // Arrange
            s_keys = new[] { "a", "a" };
            UnityEngine.Application.logMessageReceived += CountDuplicateWarnings;
            try
            {
                using var mounted = Mount(Reader);

                // Act
                Rerender(mounted);
            }
            finally
            {
                UnityEngine.Application.logMessageReceived -= CountDuplicateWarnings;
            }

            // Assert
            Assert.That(s_warnings, Is.EqualTo(2), "v5's setQueries warns each time it runs over duplicate queries");
        }

        #endregion

        #region Suspense

        [Test]
        public void Given_TwoSuspendingQueries_When_OnlyOneHasLanded_Then_TheSuspenseShowsItsFallback()
        {
            // Arrange
            using var mounted = Mount(SuspendingReader);

            // Act
            Settle(mounted, () => Source("a").TrySetResult(1));

            // Assert
            Assert.That(Texts(), Is.EqualTo("loading"), "v5 suspends on every pending query at once");
        }

        [Test]
        public void Given_ACombinedSuspendingQueryThatLosesItsData_When_ItsObserverTellsOfIt_Then_CombineDoesNotRun()
        {
            // Arrange
            using var mounted = Mount(SuspendingSumReader);
            Settle(mounted, () => Source("a").TrySetResult(1));
            Settle(mounted, () => Source("b").TrySetResult(2));
            var calls = s_combineCalls;

            // Act
            Settle(mounted, () => s_client.ResetQueries(new QueryFilters { QueryKey = new QueryKey("a") }).Forget());

            // Assert
            Assert.That(s_combineCalls, Is.EqualTo(calls), "v5's shouldSkipCombine passes over combine while a suspense query has no data");
        }

        [Test]
        public void Given_TwoSuspendingQueries_When_BothHaveLanded_Then_TheComponentRendersTheirData()
        {
            // Arrange
            using var mounted = Mount(SuspendingReader);

            // Act
            Settle(mounted, () => Source("a").TrySetResult(1));
            Settle(mounted, () => Source("b").TrySetResult(2));

            // Assert
            Assert.That(Texts(), Is.EqualTo("data:1,2"));
        }

        #endregion

        #region Components and helpers

        private MountedTree Mount(Func<VNode> reader)
        {
            s_reader = reader;
            var mounted = V.Mount(_root, V.Component(Host, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            return mounted;
        }

        private static Func<VNode> s_reader = null!;

        private static void Rerender(MountedTree mounted)
        {
            s_setTick.Invoke(tick => tick + 1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
        }

        private static void Settle(MountedTree mounted, Action action)
        {
            action();
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            mounted.FlushEffectsForTest();
        }

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        private static IReadOnlyList<QueryResult<int>> Last() => s_renders[s_renders.Count - 1];

        private static VelvetTaskCompletionSource<int> Source(string key) => s_sources[key][s_sources[key].Count - 1];

        private static QueryOptions<int> Options(string key) => new(new QueryKey(key), _ =>
        {
            if (!s_sources.TryGetValue(key, out var list)) s_sources.Add(key, list = new List<VelvetTaskCompletionSource<int>>());
            var source = new VelvetTaskCompletionSource<int>();
            list.Add(source);
            return source.Task;
        })
        {
            StaleTime = TimeSpan.MaxValue,
            ThrowOnError = s_throwOnError,
        };

        private static QueryOptions<int>[] Queries() => s_keys.Select(Options).ToArray();

        private static readonly Func<IReadOnlyList<QueryResult<int>>, int> s_sum = results =>
        {
            s_combineCalls++;
            return results.Sum(result => result.Data);
        };

        // Reads each result's data, so a change to it reaches the combine, and returns a value that change leaves equal.
        private static readonly Func<IReadOnlyList<QueryResult<int>>, List<bool>> s_landed
            = results => results.Select(result => result.Data > 0).ToList();

        [Component]
        private static VNode Reader()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var results = Hooks.UseQueries(Queries(), s_client);
            s_renders.Add(results);
            return V.Label(text: "reader");
        }

        [Component]
        private static VNode FirstDataReader()
        {
            var results = Hooks.UseQueries(Queries(), s_client);
            s_renders.Add(results);
            return V.Label(text: "first:" + results[0].Data + ":" + results[1].Status);
        }

        [Component]
        private static VNode SumReader()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            s_combined.Add(Hooks.UseQueries(Queries(), s_sum, s_client));
            return V.Label(text: "sum");
        }

        [Component]
        private static VNode StatusReader()
        {
            s_combined.Add(Hooks.UseQueries(Queries(), s_landed, s_client));

            return V.Label(text: "statuses");
        }

        private static void CountDuplicateWarnings(string message, string stackTrace, UnityEngine.LogType type)
        {
            if (type == UnityEngine.LogType.Warning && message.Contains("Duplicate Queries found")) s_warnings++;
        }

        private static QueryOptions<string> TextOptions() => new(new QueryKey("text"), _ =>
        {
            var source = new VelvetTaskCompletionSource<string>();
            s_textSources.Add(source);
            return source.Task;
        })
        {
            StaleTime = TimeSpan.MaxValue,
        };

        private static readonly Func<QueryResult<int>, QueryResult<string>, string> s_typedCombine
            = (number, text) => number.Data + "+" + text.Data;

        [Component]
        private static VNode TypedReader()
        {
            var (number, text) = Hooks.UseQueries(Options("a"), TextOptions(), s_client);
            s_typed = number.Data + ":" + text.Data;
            return V.Label(text: "typed");
        }

        [Component]
        private static VNode TypedCombineReader()
        {
            s_typed = Hooks.UseQueries(Options("a"), TextOptions(), s_typedCombine, s_client);
            return V.Label(text: "typed");
        }

        [Component]
        private static VNode SuspendingSumReader()
        {
            var sum = Hooks.UseSuspenseQueries(Queries(), s_sum, s_client);
            return V.Label(text: "sum:" + sum);
        }

        [Component]
        private static VNode SuspendingReader()
        {
            var results = Hooks.UseSuspenseQueries(Queries(), s_client);
            return V.Label(text: "data:" + string.Join(",", results.Select(result => result.Data)));
        }

        [Component]
        private static VNode Host()
            => V.ErrorBoundary(
                error => V.Label(text: "failed:" + error.Message),
                new VNode?[]
                {
                    V.Suspense(V.Label(text: "loading"), new VNode?[] { V.Component(s_reader, key: "reader") }, key: "suspense"),
                },
                key: "boundary");

        #endregion
    }
}
