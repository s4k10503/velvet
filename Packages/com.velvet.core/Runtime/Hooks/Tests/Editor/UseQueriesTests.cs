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
    /// list's order; the queries over one entry are handed the observers held over it in the list's order, whatever
    /// their positions, and a query that leaves the list leaves its entry.</item>
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
        private static readonly QueryKey Lists = new("lists");
        private static readonly List<QueryResult<int>> s_lengths = new();
        private static Func<int[], int> s_select = SelectLength;
        private static readonly List<QueryCacheEvent> s_events = new();
        private static int s_warnings;
        private static string s_typed = string.Empty;
        private static readonly List<VelvetTaskCompletionSource<string>> s_textSources = new();
        private static TimeSpan s_now;


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
            s_lengths.Clear();
            s_select = SelectLength;
            s_events.Clear();
            s_throwOnError = false;
            s_setTick = default;
            s_now = TimeSpan.Zero;
        }

        [TearDown]
        public void TearDown()
        {
            FiberStrictMode.Enabled = false;
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

        // GREEN_ON_BASE(characterization): the base already shows it once a render has caught up with the mount; deleting `_observer.Read(_entry, _shared, source);` from QueriesItem.Read reddens it.
        [Test]
        public void Given_ACombineOfStaleness_When_TheDataTurnsStaleAndTheComponentRendersTwice_Then_TheCombineSeesIt()
        {
            // Arrange — the render before the data turns stale runs the combine over the results the mount's commit
            // recorded, so the first render after it has nothing new to combine.
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue, Retry = 0, Clock = () => s_now });
            s_client.SetQueryData(new QueryKey("a"), 1);
            using var mounted = Mount(StaleReader);
            Rerender(mounted);
            s_now = TimeSpan.FromSeconds(2);

            // Act
            Rerender(mounted);
            Rerender(mounted);

            // Assert
            Assert.That(s_combined[s_combined.Count - 1], Is.EqualTo(true));
        }

        // GREEN_ON_BASE(characterization): the base never notified from a commit; deleting `_committed = false;` from QueriesObserver.LeaveAll reddens it.
        [Test]
        public void Given_StrictModeAndCachedData_When_TheMountsEffectsReplay_Then_TheComponentDoesNotRenderAgain()
        {
            // Arrange
            FiberStrictMode.Enabled = true;
            s_client.SetQueryData(new QueryKey("a"), 1);
            s_client.SetQueryData(new QueryKey("b"), 2);
            using var mounted = Mount(Reader);
            var renders = s_renders.Count;

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renders.Count, Is.EqualTo(renders), "v5's StrictMode replay hands setQueries the observers it kept, so nothing is replaced");
        }

        [Test]
        public void Given_ARenderedCombineOfStaleness_When_TheDataTurnsStaleAndTheComponentRendersOnce_Then_ItsCommitRendersWhatTheCombineMakesOfIt()
        {
            // Arrange
            s_client = new QueryClient(new QueryClientOptions { GcTime = TimeSpan.MaxValue, Retry = 0, Clock = () => s_now });
            s_client.SetQueryData(new QueryKey("a"), 1);
            using var mounted = Mount(StaleReader);
            Rerender(mounted);
            s_now = TimeSpan.FromSeconds(2);

            // Act
            Rerender(mounted);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_combined[s_combined.Count - 1], Is.EqualTo(true), "v5's setQueries runs combine over the result it replaced and re-renders for what changed");
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

        #region Each query's own result

        [Test]
        public void Given_ASelectThatThrowsOverEmptyData_When_EmptyDataLandsUnderAStatusRead_Then_TheComponentRendersTheError()
        {
            // Arrange
            s_client.SetQueryData(Lists, new[] { 1 });
            using var mounted = Mount(StatusOfLengthReader);

            // Act
            Settle(mounted, () => s_client.SetQueryData(Lists, Array.Empty<int>()));

            // Assert
            Assert.That(s_lengths[s_lengths.Count - 1].Status, Is.EqualTo(QueryStatus.Error),
                "v5's observer compares its selected result, which the throwing select turns into an error");
        }

        [Test]
        public void Given_ASelectedLength_When_DataOfTheSameLengthLandsUnderADataRead_Then_TheComponentDoesNotRenderAgain()
        {
            // Arrange
            s_client.SetQueryData(Lists, new[] { 1, 2 });
            using var mounted = Mount(LengthReader);
            var renders = s_lengths.Count;

            // Act
            Settle(mounted, () => s_client.SetQueryData(Lists, new[] { 3, 4 }));

            // Assert
            Assert.That(s_lengths.Count, Is.EqualTo(renders), "v5 compares the selected data, which the landing leaves equal");
        }

        [Test]
        public void Given_AQueryWithASelect_When_ItsEntryIsRead_Then_ItsOptionsAreTheQuerys()
        {
            // Arrange
            s_client.SetQueryData(Lists, new[] { 1 });

            // Act
            using var mounted = Mount(LengthReader);

            // Assert
            Assert.That(s_client.GetQueryCache().Find(new QueryFilters { QueryKey = Lists })!.Options, Is.InstanceOf<QueryOptions<int[], int>>(),
                "v5's query.options are the observer's options, select included");
        }

        [Test]
        public void Given_AMountedQuery_When_ACommitChangesOnlyItsSelect_Then_TheCacheAnnouncesItsOptionsUpdated()
        {
            // Arrange
            s_client.SetQueryData(Lists, new[] { 1 });
            s_client.GetQueryCache().Subscribe(s_events.Add);
            using var mounted = Mount(LengthReader);
            s_events.Clear();

            // Act
            s_select = data => data.Length + 1;
            Rerender(mounted);

            // Assert
            Assert.That(s_events.Exists(e => e.Type == QueryCacheEventType.ObserverOptionsUpdated), Is.True,
                "v5's shallow comparison of the options sees the new select");
        }

        private static int SelectLength(int[] data) => data.Length == 0 ? throw new InvalidOperationException("empty") : data.Length;

        private static QueryOptions<int[], int> LengthOptions() => new(Lists, () => VelvetTask.FromResult(new[] { 9 }))
        {
            Select = s_select,
            StaleTime = TimeSpan.MaxValue,
        };

        [Component]
        private static VNode StatusOfLengthReader()
        {
            var results = Hooks.UseQueries(new[] { LengthOptions() }, s_client);
            s_lengths.Add(results[0]);
            return V.Label(text: "status:" + results[0].Status);
        }

        [Component]
        private static VNode LengthReader()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var results = Hooks.UseQueries(new[] { LengthOptions() }, s_client);
            s_lengths.Add(results[0]);
            return V.Label(text: "length:" + results[0].Data);
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

        [Test]
        public void Given_TwoQueriesOverOneEntry_When_TheListCommitsAgain_Then_EachKeepsAnObserverOfItsOwn()
        {
            // Arrange
            s_keys = new[] { "a", "a" };
            using var mounted = Mount(Reader);

            // Act
            Rerender(mounted);

            // Assert
            Assert.That(s_client.GetQueryCache().Find(new QueryFilters { QueryKey = new QueryKey("a") })!.GetObserversCount(),
                Is.EqualTo(2), "v5 keeps a list of the observers of each hash and hands them out in order");
        }

        [Test]
        public void Given_TwoQueriesOverOneEntry_When_TheListCommitsAgain_Then_TheComponentDoesNotRenderAgain()
        {
            // Arrange
            s_keys = new[] { "a", "a" };
            using var mounted = Mount(Reader);
            var renders = s_renders.Count;

            // Act
            Rerender(mounted);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renders.Count, Is.EqualTo(renders + 1),
                "Each query takes the observer it held, so v5's setQueries finds no structural change to notify of");
        }

        // GREEN_ON_BASE(characterization): the base held one observer over the entry, so a change told one observer and
        // rendered once; an observer for each query must still render once, which recording every observer at the
        // first one's change does not.
        [Test]
        public void Given_TwoQueriesOverOneEntry_When_ItsDataLands_Then_TheComponentRendersOnce()
        {
            // Arrange
            s_keys = new[] { "a", "a" };
            using var mounted = Mount(Reader);
            Rerender(mounted);
            mounted.FlushStateForTest();
            var renders = s_renders.Count;

            // Act
            Settle(mounted, () => Source("a").TrySetResult(1));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renders.Count, Is.EqualTo(renders + 1),
                "v5's #onUpdate records each observer's own result, so the commit after the change finds nothing new");
        }

        // GREEN_ON_BASE(characterization): the base held one observer over the entry, so a change told one observer and
        // rendered once; a combine whose output is never shared must still render once, which refreshing the second
        // query's result at the first observer's change does not.
        [Test]
        public void Given_TwoQueriesOverOneEntryAndACombineOfANewSet_When_TheirDataLands_Then_TheComponentRendersOnce()
        {
            // Arrange
            s_keys = new[] { "a", "a" };
            using var mounted = Mount(SetReader);
            Rerender(mounted);
            mounted.FlushStateForTest();
            var renders = s_combined.Count;

            // Act
            Settle(mounted, () => Source("a").TrySetResult(1));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_combined.Count, Is.EqualTo(renders + 1),
                "v5's #onUpdate runs combine over each observer's own result, so the commit after the change finds nothing new");
        }

        // GREEN_ON_BASE(characterization): the base announced each query's changed result from one observer's pass;
        // with an observer each, refreshing the second query's result at the first observer's change announces it
        // nowhere.
        [Test]
        public void Given_TwoQueriesOverOneEntryAndACombine_When_TheirDataLands_Then_TheCacheIsToldOfEachResult()
        {
            // Arrange
            s_keys = new[] { "a", "a" };
            using var mounted = Mount(SetReader);
            Rerender(mounted);
            mounted.FlushStateForTest();
            s_client.GetQueryCache().Subscribe(s_events.Add);

            // Act
            Settle(mounted, () => Source("a").TrySetResult(1));

            // Assert
            Assert.That(s_events.Count(e => e.Type == QueryCacheEventType.ObserverResultsUpdated), Is.EqualTo(2),
                "v5's observer tells the cache of its own result that changed");
        }

        [Test]
        public void Given_TwoQueries_When_TheSecondTurnsToTheFirstsEntry_Then_ItTakesAnObserverOfItsOwn()
        {
            // Arrange
            using var mounted = Mount(Reader);
            s_keys = new[] { "a", "a" };

            // Act
            Rerender(mounted);

            // Assert
            Assert.That(s_client.GetQueryCache().Find(new QueryFilters { QueryKey = new QueryKey("a") })!.GetObserversCount(),
                Is.EqualTo(2), "v5 creates an observer for a query no observer held over its entry is left for");
        }

        [Test]
        public void Given_QueriesOfTwoDataTypesOverOneEntry_When_TheListCommitsTwice_Then_NothingIsFetchedAgain()
        {
            // Arrange
            using var mounted = Mount(MixedReader);
            Settle(mounted, () => Source("a").TrySetResult(1));

            // Act
            Rerender(mounted);
            Rerender(mounted);

            // Assert
            Assert.That(s_sources["a"].Count, Is.EqualTo(1),
                "Each data type keeps its observer over the entry, so no commit subscribes a new one that fetches");
        }

        [Test]
        public void Given_QueriesOfTwoDataTypesOverOneEntry_When_TheListCommitsAgain_Then_EachKeepsAnObserverOfItsOwn()
        {
            // Arrange
            using var mounted = Mount(MixedReader);
            Settle(mounted, () => Source("a").TrySetResult(1));

            // Act
            Rerender(mounted);

            // Assert
            Assert.That(s_client.GetQueryCache().Find(new QueryFilters { QueryKey = new QueryKey("a") })!.GetObserversCount(),
                Is.EqualTo(2), "v5 matches an observer by its queryHash alone, whatever each query selects");
        }

        [Test]
        public void Given_QueriesOfTwoDataTypesOverOneEntry_When_TheDataLands_Then_EachResultIsItsOwnSelection()
        {
            // Arrange
            using var mounted = Mount(MixedReader);

            // Act
            Settle(mounted, () => Source("a").TrySetResult(1));

            // Assert
            Assert.That(s_typed, Is.EqualTo("n1:1"), "Each query's result comes from its own select, as v5's getOptimisticResult(options) does");
        }

        [Test]
        public void Given_AQueryListingNoProps_When_ItsDataLands_Then_TheComponentDoesNotRenderAgain()
        {
            // Arrange
            using var mounted = Mount(UnlistedReader);
            var renders = s_renders.Count;

            // Act
            Settle(mounted, () => Source("a").TrySetResult(1));

            // Assert
            Assert.That(s_renders.Count, Is.EqualTo(renders), "v5 reads an empty list as watching nothing, as UseQuery does");
        }

        [Test]
        public void Given_AReadFromAQueryListingItsProps_When_AnotherQuerysDataChanges_Then_TheComponentDoesNotRenderAgain()
        {
            // Arrange
            using var mounted = Mount(ListedReader);
            Settle(mounted, () =>
            {
                Source("a").TrySetResult(1);
                Source("b").TrySetResult(2);
            });
            var renders = s_renders.Count;

            // Act
            Settle(mounted, () => s_client.SetQueryData(new QueryKey("b"), 9));

            // Assert
            Assert.That(s_renders.Count, Is.EqualTo(renders),
                "v5 hands back the result of a query listing notifyOnChangeProps untracked, so its read tracks nothing");
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

        // GREEN_ON_BASE(characterization): the base already suspends; passing `suspense: false` from the four-query combine overload reddens it.
        [Test]
        public void Given_FourSuspendingQueriesWithACombine_When_NoneHasLanded_Then_TheSuspenseShowsItsFallback()
        {
            // Act
            using var mounted = Mount(FourSuspendingCombineReader);

            // Assert
            Assert.That(Texts(), Is.EqualTo("loading"));
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

        // A new set every run, which structural sharing keeps only as the very instance it already holds.
        private static readonly Func<IReadOnlyList<QueryResult<int>>, HashSet<int>> s_dataSet
            = results => new HashSet<int>(results.Select(result => result.Data));

        [Component]
        private static VNode SetReader()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            s_combined.Add(Hooks.UseQueries(Queries(), s_dataSet, s_client));
            return V.Label(text: "set");
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

        private static readonly Func<IReadOnlyList<QueryResult<int>>, bool> s_allStale
            = results => results.All(result => result.IsStale);

        [Component]
        private static VNode StaleReader()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            s_combined.Add(Hooks.UseQueries(new[] { Options("a") with { StaleTime = TimeSpan.FromSeconds(1) } }, s_allStale, s_client));
            return V.Label(text: "stale");
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
        private static VNode MixedReader()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var number = Options("a");
            var (count, raw) = Hooks.UseQueries(
                new QueryOptions<int, string>(number.QueryKey, number.QueryFn) { Select = data => "n" + data, StaleTime = TimeSpan.Zero },
                number with { StaleTime = TimeSpan.Zero },
                s_client);
            s_typed = count.Data + ":" + raw.Data;
            return V.Label(text: "mixed");
        }

        [Component]
        private static VNode UnlistedReader()
        {
            var results = Hooks.UseQueries(new[] { Options("a") with { NotifyOnChangeProps = QueryProperties.None } }, s_client);
            s_renders.Add(results);
            return V.Label(text: "unlisted");
        }

        [Component]
        private static VNode ListedReader()
        {
            var (first, second) = Hooks.UseQueries(
                Options("a") with { NotifyOnChangeProps = QueryProperties.Data }, Options("b"), s_client);
            s_renders.Add(new[] { first, second });
            return V.Label(text: "listed:" + first.Data + ":" + second.Status);
        }

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
        private static VNode FourSuspendingCombineReader()
        {
            var sum = Hooks.UseSuspenseQueries(
                Options("a"), Options("b"), Options("c"), Options("d"),
                (QueryResult<int> a, QueryResult<int> b, QueryResult<int> c, QueryResult<int> d) => a.Data + b.Data + c.Data + d.Data,
                s_client);
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
