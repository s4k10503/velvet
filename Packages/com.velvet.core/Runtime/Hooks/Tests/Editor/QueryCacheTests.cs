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
    /// Specifies <see cref="QueryCache"/>, TanStack Query's <c>QueryCache</c>, with <see cref="QueryOptionsBase{TQueryFnData, TData}.Meta"/>,
    /// <see cref="QueryFunctionContext"/>, <see cref="QueryClient.IsFetching"/> and <see cref="Hooks.UseIsFetching"/>.
    /// <list type="bullet">
    /// <item>The cache announces an entry added, removed, updated with the action that changed it, and a mounted
    /// query added, removed, its result changed and its options changed.</item>
    /// <item>Its callbacks run as a request lands or fails for good, and when a cancellation does not revert; a
    /// throw from <see cref="QueryCache.OnSuccess"/> is the request's failure, and one from
    /// <see cref="QueryCache.OnError"/> skips <see cref="QueryCache.OnSettled"/>.</item>
    /// <item><see cref="QueryCache.Find"/> matches exactly unless told otherwise; a predicate is handed the
    /// <see cref="Query"/>, its meta included.</item>
    /// <item>A context-taking query function is handed the meta, and its request is cancelled with its last
    /// reader only once it has read the token.</item>
    /// <item><see cref="QueryClient.IsFetching"/> counts the entries fetching, and <see cref="Hooks.UseIsFetching"/>
    /// re-renders with the count.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The query function hands back a completion source the case settles itself; nothing retries.
    /// </remarks>
    [TestFixture]
    internal sealed class QueryCacheTests
    {
        private static readonly QueryKey Todos = new("todos");
        private static readonly IReadOnlyDictionary<string, object?> s_meta = new Dictionary<string, object?> { ["scope"] = "todos" };

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static TimeSpan s_now;
        private static readonly List<QueryCacheEvent> s_events = new();
        private static readonly List<VelvetTaskCompletionSource<int>> s_sources = new();
        private static readonly List<string> s_callbacks = new();
        private static readonly List<object?> s_handed = new();
        private static Exception? s_successFailure;
        private static Exception? s_errorFailure;
        private static TimeSpan? s_staleTime;
        private static bool s_readsToken;
        private static QueryFunctionContext? s_context;
        private static readonly List<int> s_fetchingCounts = new();
        private static StateUpdater<int> s_setTick;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_now = TimeSpan.Zero;
            s_events.Clear();
            s_sources.Clear();
            s_callbacks.Clear();
            s_handed.Clear();
            s_successFailure = null;
            s_errorFailure = null;
            s_staleTime = null;
            s_readsToken = false;
            s_context = null;
            s_fetchingCounts.Clear();
            s_setTick = default;
            var cache = new QueryCache
            {
                OnSuccess = (data, query) =>
                {
                    s_callbacks.Add("success");
                    s_handed.Add(data);
                    s_handed.Add(query);
                    if (s_successFailure != null) throw s_successFailure;
                },
                OnError = (error, query) =>
                {
                    s_callbacks.Add("error");
                    s_handed.Add(error);
                    s_handed.Add(query);
                    if (s_errorFailure != null) throw s_errorFailure;
                },
                OnSettled = (data, error, query) =>
                {
                    s_callbacks.Add("settled");
                    s_handed.Add(data);
                },
            };
            cache.Subscribe(s_events.Add);
            s_client = new QueryClient(new QueryClientOptions { QueryCache = cache, Clock = () => s_now });
        }

        #region Entry events

        [Test]
        public void Given_NoEntry_When_SetQueryDataMakesOne_Then_TheCacheAnnouncesItAdded()
        {
            // Act
            s_client.SetQueryData(Todos, 1);

            // Assert
            Assert.That(s_events[0].Type, Is.EqualTo(QueryCacheEventType.Added), "v5's build adds the query and announces it");
        }

        [Test]
        public void Given_AnEntry_When_SetQueryDataWritesIt_Then_TheCacheAnnouncesASuccessUpdate()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);

            // Act
            s_client.SetQueryData(Todos, 2);

            // Assert
            Assert.That(s_events[s_events.Count - 1].Action, Is.EqualTo(QueryActionType.Success),
                "v5's setData dispatches a success action, which the cache passes on as updated");
        }

        [Test]
        public void Given_AnEntry_When_RemoveQueriesRemovesIt_Then_TheCacheAnnouncesItRemoved()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);

            // Act
            s_client.RemoveQueries(new QueryFilters { QueryKey = Todos });

            // Assert
            Assert.That(s_events[s_events.Count - 1].Type, Is.EqualTo(QueryCacheEventType.Removed));
        }

        [Test]
        public void Given_AnEntryUnreadPastItsGcTime_When_ACommandSweeps_Then_TheCacheAnnouncesItRemoved()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_now = s_client.DefaultGcTime;

            // Act
            s_client.InvalidateQueries();

            // Assert
            Assert.That(s_events[s_events.Count - 1].Type, Is.EqualTo(QueryCacheEventType.Removed),
                "An entry collected is removed from the cache as one v5's gc timeout removes");
        }

        [Test]
        public void Given_AnEntryHoldingData_When_InvalidateQueriesMarksIt_Then_TheCacheAnnouncesAnInvalidateUpdate()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);

            // Act
            s_client.InvalidateQueries(Todos);

            // Assert
            Assert.That(s_events[s_events.Count - 1].Action, Is.EqualTo(QueryActionType.Invalidate));
        }

        [Test]
        public void Given_InitialData_When_FetchQueryMakesTheEntry_Then_TheCacheAnnouncesItAddedFirst()
        {
            // Act
            _ = s_client.FetchQuery(Options() with { InitialData = () => 1, StaleTime = TimeSpan.MaxValue });

            // Assert
            Assert.That(s_events[0].Type, Is.EqualTo(QueryCacheEventType.Added),
                "v5's build constructs the query with its initial data before adding it, announcing no update first");
        }

        [Test]
        public void Given_NoEntry_When_FetchQueryStartsARequest_Then_TheCacheAnnouncesAFetchUpdate()
        {
            // Act
            _ = s_client.FetchQuery(Options());

            // Assert
            Assert.That(s_events[s_events.Count - 1].Action, Is.EqualTo(QueryActionType.Fetch));
        }

        [Test]
        public void Given_ARequestThatRetries_When_ItsFirstAttemptFails_Then_TheCacheAnnouncesAFailedUpdate()
        {
            // Arrange
            _ = s_client.FetchQuery(Options() with { Retry = 1, RetryDelay = (_, _) => TimeSpan.Zero });

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("failed"));

            // Assert
            Assert.That(s_events[s_events.Count - 1].Action, Is.EqualTo(QueryActionType.Failed));
        }

        [Test]
        public void Given_ARequest_When_ItFailsForGood_Then_TheCacheAnnouncesAnErrorUpdate()
        {
            // Arrange
            _ = s_client.FetchQuery(Options());

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("failed"));

            // Assert
            Assert.That(s_events[s_events.Count - 1].Action, Is.EqualTo(QueryActionType.Error));
        }

        [Test]
        public void Given_AnEntry_When_ResetQueriesResetsIt_Then_TheCacheAnnouncesASetStateUpdate()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);

            // Act
            _ = s_client.ResetQueries();

            // Assert
            Assert.That(s_events[s_events.Count - 1].Action, Is.EqualTo(QueryActionType.SetState));
        }

        [Test]
        public void Given_TwoEntries_When_ClearRuns_Then_TheCacheAnnouncesEachRemoved()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.SetQueryData(new QueryKey("other"), 2);
            s_events.Clear();

            // Act
            s_client.Clear();

            // Assert
            Assert.That(s_events.FindAll(e => e.Type == QueryCacheEventType.Removed).Count, Is.EqualTo(2));
        }

        #endregion

        #region Observer events

        [Test]
        public void Given_AQuery_When_ItMounts_Then_TheCacheAnnouncesAnObserverAdded()
        {
            // Act
            using var mounted = Mount();

            // Assert
            Assert.That(s_events.Exists(e => e.Type == QueryCacheEventType.ObserverAdded && e.Observer != null), Is.True);
        }

        [Test]
        public void Given_AMountedQuery_When_ItUnmounts_Then_TheCacheAnnouncesAnObserverRemoved()
        {
            // Arrange
            var mounted = Mount();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(s_events.Exists(e => e.Type == QueryCacheEventType.ObserverRemoved), Is.True);
        }

        [Test]
        public void Given_AMountedQuery_When_ItsRequestLands_Then_TheCacheAnnouncesItsResultUpdated()
        {
            // Arrange
            using var mounted = Mount();
            s_events.Clear();

            // Act
            s_sources[0].TrySetResult(4);

            // Assert
            Assert.That(s_events.Exists(e => e.Type == QueryCacheEventType.ObserverResultsUpdated), Is.True);
        }

        [Test]
        public void Given_AMountedQuery_When_ACommitChangesItsOptions_Then_TheCacheAnnouncesItsOptionsUpdated()
        {
            // Arrange
            using var mounted = Mount();
            s_events.Clear();

            // Act
            s_staleTime = TimeSpan.FromMinutes(1);
            s_setTick.Invoke(tick => tick + 1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_events.Exists(e => e.Type == QueryCacheEventType.ObserverOptionsUpdated), Is.True);
        }

        [Test]
        public void Given_AMountedQuery_When_ACommitHandsEqualOptions_Then_NoOptionsUpdateIsAnnounced()
        {
            // Arrange
            using var mounted = Mount();
            s_events.Clear();

            // Act
            s_setTick.Invoke(tick => tick + 1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_events.Exists(e => e.Type == QueryCacheEventType.ObserverOptionsUpdated), Is.False,
                "v5 announces options that differ shallowly from the previous commit's");
        }

        #endregion

        #region Callbacks

        [Test]
        public void Given_ARequest_When_ItLands_Then_OnSuccessIsHandedTheDataAndTheEntry()
        {
            // Arrange
            _ = s_client.FetchQuery(Options());

            // Act
            s_sources[0].TrySetResult(4);

            // Assert
            Assert.That(s_handed[1], Is.SameAs(s_client.GetQueryCache().Find(new QueryFilters { QueryKey = Todos })));
        }

        [Test]
        public void Given_ARequest_When_ItLands_Then_OnSettledFollowsOnSuccess()
        {
            // Arrange
            _ = s_client.FetchQuery(Options());

            // Act
            s_sources[0].TrySetResult(4);

            // Assert
            Assert.That(string.Join(",", s_callbacks), Is.EqualTo("success,settled"));
        }

        [Test]
        public void Given_ARequestOverData_When_ItFails_Then_OnSettledIsHandedTheDataTheEntryHolds()
        {
            // Arrange
            s_client.SetQueryData(Todos, 3);
            _ = s_client.FetchQuery(Options() with { StaleTime = TimeSpan.Zero });

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("failed"));

            // Assert
            Assert.That(s_handed[s_handed.Count - 1], Is.EqualTo(3), "v5's onSettled after a failure is handed state.data");
        }

        [Test]
        public void Given_ARequest_When_ItFails_Then_OnErrorIsHandedTheFailure()
        {
            // Arrange
            var failure = new InvalidOperationException("failed");
            _ = s_client.FetchQuery(Options());

            // Act
            s_sources[0].TrySetException(failure);

            // Assert
            Assert.That(s_handed[0], Is.SameAs(failure));
        }

        [Test]
        public void Given_AnOnSuccessThatThrows_When_ARequestLands_Then_TheCallerIsHandedItsException()
        {
            // Arrange
            s_successFailure = new InvalidOperationException("callback");
            var task = s_client.FetchQuery(Options());

            // Act
            s_sources[0].TrySetResult(4);

            // Assert
            Assert.That(ExceptionOf(task), Is.SameAs(s_successFailure),
                "v5 runs the cache's onSuccess inside the fetch, whose catch turns a throw into the fetch's error");
        }

        [Test]
        public void Given_AnOnSuccessThatThrows_When_ARequestLands_Then_TheEntryIsInError()
        {
            // Arrange
            s_successFailure = new InvalidOperationException("callback");
            _ = s_client.FetchQuery(Options());

            // Act
            s_sources[0].TrySetResult(4);

            // Assert
            Assert.That(s_client.GetQueryState<int>(Todos)!.Status, Is.EqualTo(QueryStatus.Error));
        }

        [Test]
        public void Given_AnOnErrorThatThrows_When_ARequestFails_Then_OnSettledDoesNotRun()
        {
            // Arrange
            s_errorFailure = new InvalidOperationException("callback");
            _ = s_client.FetchQuery(Options());

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("failed"));

            // Assert
            Assert.That(s_callbacks.Contains("settled"), Is.False);
        }

        [Test]
        public void Given_ARequestInFlight_When_CancelQueriesDoesNotRevert_Then_OnErrorRuns()
        {
            // Arrange
            _ = s_client.FetchQuery(Options());

            // Act
            s_client.CancelQueries(revert: false);

            // Assert
            Assert.That(s_callbacks.Contains("error"), Is.True,
                "v5's cancellation that neither reverts nor is silent reaches the cache's onError");
        }

        [Test]
        public void Given_ARequestInFlight_When_CancelQueriesReverts_Then_OnErrorDoesNotRun()
        {
            // Arrange
            _ = s_client.FetchQuery(Options());

            // Act
            s_client.CancelQueries();

            // Assert
            Assert.That(s_callbacks.Contains("error"), Is.False);
        }

        [Test]
        public void Given_DataWrittenByHand_When_SetQueryDataRuns_Then_OnSuccessDoesNotRun()
        {
            // Act
            s_client.SetQueryData(Todos, 1);

            // Assert
            Assert.That(s_callbacks, Is.Empty, "v5's cache callbacks belong to a fetch");
        }

        #endregion

        #region The query hash

        [Test]
        public void Given_AKeyHoldingAList_When_GetLooksUpItsHash_Then_TheEntryIsFound()
        {
            // Arrange
            s_client.SetQueryData(new QueryKey("todos", new List<int> { 1, 2 }), 1);

            // Act
            var found = s_client.GetQueryCache().Get("[\"todos\",[1,2]]");

            // Assert
            Assert.That(found?.QueryKey, Is.EqualTo(new QueryKey("todos", new[] { 1, 2 })),
                "v5's hashKey writes the key as JSON, so equal keys hash alike");
        }

        [Test]
        public void Given_AKeyHashFn_When_GetLooksUpItsString_Then_TheEntryIsFound()
        {
            // Arrange
            _ = s_client.FetchQuery(Options() with { QueryKeyHashFn = _ => "custom" });

            // Act
            var found = s_client.GetQueryCache().Get("custom");

            // Assert
            Assert.That(found?.QueryKey, Is.EqualTo(Todos));
        }

        [Test]
        public void Given_TwoKeysHoldingEqualDictionariesBuiltInAnotherOrder_When_TheirHashesAreRead_Then_TheDictionariesHashAlike()
        {
            // Arrange
            s_client.SetQueryData(new QueryKey("first", new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }), 1);
            s_client.SetQueryData(new QueryKey("second", new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 }), 1);

            // Act
            var hash = s_client.GetQueryCache().Find(new QueryFilters { QueryKey = new QueryKey("second"), Exact = false })!.QueryHash;

            // Assert
            Assert.That(hash, Is.EqualTo("[\"second\",#1]"), "Equal parts the hash cannot write by value share one number");
        }

        [Test]
        public void Given_AnEntryThatWasRemoved_When_GetLooksUpItsHash_Then_NothingIsFound()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);
            s_client.RemoveQueries();

            // Act
            var found = s_client.GetQueryCache().Get("[\"todos\"]");

            // Assert
            Assert.That(found, Is.Null);
        }

        [Test]
        public void Given_AMountedQuery_When_ItsEntryIsInvalidated_Then_ItsReaderIsToldBeforeTheCache()
        {
            // Arrange
            s_staleTime = TimeSpan.FromMinutes(1);
            using var mounted = Mount();
            s_sources[0].TrySetResult(4);
            s_events.Clear();

            // Act
            _ = s_client.InvalidateQueries(Todos);

            // Assert
            Assert.That(s_events[0].Type, Is.EqualTo(QueryCacheEventType.ObserverResultsUpdated),
                "v5's invalidate dispatches, which updates the readers' results before the cache hears of it");
        }

        #endregion

        #region The query

        [Test]
        public void Given_DataWrittenByHandAndNoReader_When_IsDisabledIsAsked_Then_ItIsNot()
        {
            // Arrange
            s_client.SetQueryData(Todos, 1);

            // Act
            var disabled = s_client.GetQueryCache().Find(new QueryFilters { QueryKey = Todos })!.IsDisabled();

            // Assert
            Assert.That(disabled, Is.False, "v5's isDisabled counts a write as an update, whatever function the query holds");
        }

        [Test]
        public void Given_AMountedQuery_When_GetObserversCountIsAsked_Then_ItCountsTheReader()
        {
            // Arrange
            using var mounted = Mount();

            // Act
            var count = s_client.GetQueryCache().Find(new QueryFilters { QueryKey = Todos })!.GetObserversCount();

            // Assert
            Assert.That(count, Is.EqualTo(1));
        }

        #endregion

        #region Find and filters

        [Test]
        public void Given_FiltersThatDoNotSetExact_When_FindSearches_Then_ALongerKeyIsNotFound()
        {
            // Arrange
            s_client.SetQueryData(new QueryKey("todos", 1), 1);

            // Act
            var found = s_client.GetQueryCache().Find(new QueryFilters { QueryKey = Todos });

            // Assert
            Assert.That(found, Is.Null, "v5's find defaults exact to true");
        }

        [Test]
        public void Given_FiltersSettingExactFalse_When_FindSearches_Then_ALongerKeyIsFound()
        {
            // Arrange
            s_client.SetQueryData(new QueryKey("todos", 1), 1);

            // Act
            var found = s_client.GetQueryCache().Find(new QueryFilters { QueryKey = Todos, Exact = false });

            // Assert
            Assert.That(found?.QueryKey, Is.EqualTo(new QueryKey("todos", 1)));
        }

        [Test]
        public void Given_EntriesWithAndWithoutMeta_When_APredicateReadsIt_Then_OnlyTheEntryCarryingItIsSelected()
        {
            // Arrange
            s_client.SetQueryData(new QueryKey("plain"), 1);
            _ = s_client.FetchQuery(Options() with { Meta = s_meta });

            // Act
            var found = s_client.GetQueryCache().FindAll(new QueryFilters { Predicate = query => query.Meta != null });

            // Assert
            Assert.That(found.Count == 1 ? found[0].QueryKey : null, Is.EqualTo(Todos));
        }

        #endregion

        #region Meta and the function context

        [Test]
        public void Given_Meta_When_AContextTakingQueryFunctionRuns_Then_ItIsHandedTheMeta()
        {
            // Act
            _ = s_client.FetchQuery(ContextOptions() with { Meta = s_meta });

            // Assert
            Assert.That(s_context?.Meta, Is.SameAs(s_meta));
        }

        [Test]
        public void Given_AContextTakingQueryFunction_When_ItRuns_Then_ItIsHandedTheKey()
        {
            // Act
            _ = s_client.FetchQuery(ContextOptions());

            // Assert
            Assert.That(s_context?.QueryKey, Is.EqualTo(Todos));
        }

        [Test]
        public void Given_AContextTakingQueryFunction_When_ItRuns_Then_ItIsHandedTheClient()
        {
            // Act
            _ = s_client.FetchQuery(ContextOptions());

            // Assert
            Assert.That(s_context?.Client, Is.SameAs(s_client));
        }

        [Test]
        public void Given_Meta_When_OnSuccessRuns_Then_TheEntryItIsHandedCarriesIt()
        {
            // Arrange
            _ = s_client.FetchQuery(Options() with { Meta = s_meta });

            // Act
            s_sources[0].TrySetResult(4);

            // Assert
            Assert.That(((Query)s_handed[1]!).Meta, Is.SameAs(s_meta));
        }

        [Test]
        public void Given_AContextFunctionThatReadsTheToken_When_ItsLastReaderLeaves_Then_TheRequestIsCancelled()
        {
            // Arrange
            s_readsToken = true;
            var mounted = V.Mount(_root, V.Component(ContextReader, key: "reader"));
            mounted.FlushEffectsForTest();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(s_client.GetQueryState<int>(Todos)!.FetchStatus, Is.EqualTo(QueryFetchStatus.Idle),
                "v5 cancels the request of a function that read its signal when its last observer leaves");
        }

        [Test]
        public void Given_AContextFunctionThatDoesNotReadTheToken_When_ItsLastReaderLeaves_Then_TheRequestRunsOn()
        {
            // Arrange
            var mounted = V.Mount(_root, V.Component(ContextReader, key: "reader"));
            mounted.FlushEffectsForTest();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(s_client.GetQueryState<int>(Todos)!.FetchStatus, Is.EqualTo(QueryFetchStatus.Fetching));
        }

        #endregion

        #region IsFetching

        [Test]
        public void Given_ARequestInFlight_When_IsFetchingCounts_Then_TheEntryIsCounted()
        {
            // Arrange
            s_client.SetQueryData(new QueryKey("idle"), 1);
            _ = s_client.FetchQuery(Options());

            // Act
            var fetching = s_client.IsFetching();

            // Assert
            Assert.That(fetching, Is.EqualTo(1));
        }

        [Test]
        public void Given_UseIsFetching_When_ARequestStartsAndLands_Then_TheComponentRendersEachCount()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(FetchingCounter, key: "counter"));
            mounted.FlushEffectsForTest();

            // Act
            _ = s_client.FetchQuery(Options());
            mounted.FlushStateForTest();
            s_sources[0].TrySetResult(4);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(",", s_fetchingCounts), Is.EqualTo("0,1,0"));
        }

        #endregion

        #region Components and helpers

        private MountedTree Mount()
        {
            var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            return mounted;
        }

        private static Exception? ExceptionOf(VelvetTask<int> task)
        {
            try
            {
                task.GetAwaiter().GetResult();
                return null;
            }
            catch (Exception failure)
            {
                return failure;
            }
        }

        private static QueryOptions<int> Options() => new(Todos, Fetch) { Retry = 0 };

        private static QueryOptions<int> ContextOptions() => new(Todos, (QueryFunctionContext context) =>
        {
            s_context = context;
            if (s_readsToken) _ = context.CancellationToken;
            return Fetch(default);
        }) { Retry = 0 };

        private static VelvetTask<int> Fetch(CancellationToken token)
        {
            var source = new VelvetTaskCompletionSource<int>();
            s_sources.Add(source);
            return source.Task;
        }

        [Component]
        private static VNode Reader()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseQuery(Options() with { StaleTime = s_staleTime }, s_client);
            return V.Label(text: "reader");
        }

        [Component]
        private static VNode ContextReader()
        {
            Hooks.UseQuery(ContextOptions(), s_client);
            return V.Label(text: "reader");
        }

        [Component]
        private static VNode FetchingCounter()
        {
            s_fetchingCounts.Add(Hooks.UseIsFetching(client: s_client));
            return V.Label(text: "counter");
        }

        #endregion
    }
}
