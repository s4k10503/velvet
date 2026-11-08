#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Velvet
{
    /// <summary>
    /// One entry of a <see cref="QueryCache"/>, TanStack Query's <c>Query</c>: the state a key's data is held in,
    /// the same instance for as long as the entry stays in the cache. It is what the cache's events and callbacks,
    /// a <see cref="QueryFilters.Predicate"/> and the function forms of the query options are handed. The state
    /// properties read the entry as it stands when they are read, where v5 nests them under <c>query.state</c>.
    /// </summary>
    public sealed class Query
    {
        internal Query(QueryEntry entry) => Entry = entry;

        internal QueryEntry Entry { get; }

        /// <summary>The entry's key.</summary>
        public QueryKey QueryKey => Entry.Key;

        /// <summary>The <see cref="QueryOptionsBase{TQueryFnData, TData}.Meta"/> of the options the entry was last
        /// handed; null while it holds none.</summary>
        public IReadOnlyDictionary<string, object?>? Meta => Entry.Meta;

        /// <summary>Whether the entry has data; see <see cref="QueryStatus"/>.</summary>
        public QueryStatus Status => Entry.Status;

        /// <summary>The data the entry holds, the query function's data and not the result of
        /// <see cref="QueryOptionsBase{TQueryFnData, TData}.Select"/>; null while it holds none.</summary>
        public object? Data => Entry.BoxedData;

        /// <summary>The failure of the entry's last request, under <see cref="QueryStatus.Error"/>; null
        /// otherwise.</summary>
        public Exception? Error => Entry.Error;

        /// <summary>Whether a request for the entry is in flight and running; false while it is paused.</summary>
        public bool IsFetching => FetchStatus == QueryFetchStatus.Fetching;

        /// <summary>Whether a request for the entry is in flight, and whether it is paused.</summary>
        public QueryFetchStatus FetchStatus => Entry.FetchStatus;

        /// <summary>When the entry's data landed, on the client's clock; zero while it holds none.</summary>
        public TimeSpan DataUpdatedAt => Entry.HasData ? Entry.DataUpdatedAt : TimeSpan.Zero;

        /// <summary>How many times the request in flight or the last one has failed.</summary>
        public int FailureCount => Entry.FailureCount;

        /// <summary>The exception of the latest failure of the request in flight or the last one; null once a
        /// request lands.</summary>
        public Exception? FailureReason => Entry.FailureReason;

        /// <summary>Whether the entry was invalidated, or its last request failed, since its data landed.</summary>
        public bool IsInvalidated => Entry.IsInvalidated;

        /// <summary>How many mounted queries read the entry, TanStack Query's <c>getObserversCount</c>.</summary>
        public int GetObserversCount() => Entry.ObserverCount;

        /// <summary>Whether an enabled query reads the entry, TanStack Query's <c>isActive</c>.</summary>
        public bool IsActive() => Entry.IsActive;

        /// <summary>
        /// TanStack Query's <c>isDisabled</c>: with readers, whether none of them is enabled; without, whether no
        /// data has landed in the entry or been written to it and no request for it has failed.
        /// </summary>
        public bool IsDisabled() => Entry.IsDisabled;

        /// <summary>
        /// TanStack Query's <c>isStale</c>: with readers, whether any of them reads the entry as stale; without,
        /// whether it has no data or was invalidated.
        /// </summary>
        public bool IsStale() => Entry.IsStaleForFilter;
    }

    /// <summary>
    /// The half of <see cref="Hooks.UseQuery{TQueryFnData, TData}"/> a component keeps, TanStack Query's
    /// <c>QueryObserver</c>, as the <see cref="QueryCache"/> events name it.
    /// </summary>
    public abstract class QueryObserver
    {
        private protected QueryObserver()
        {
        }

        /// <summary>The entry the observer's latest commit subscribed to; null before its first commit and after
        /// it leaves.</summary>
        public abstract Query? GetCurrentQuery();
    }

    /// <summary>What changed a <see cref="Query"/>: TanStack Query's query action types.</summary>
    public enum QueryActionType
    {
        /// <summary>An attempt failed and another follows.</summary>
        Failed,
        /// <summary>A request started.</summary>
        Fetch,
        /// <summary>Data landed or was written.</summary>
        Success,
        /// <summary>The request failed for good.</summary>
        Error,
        /// <summary>The entry was marked stale.</summary>
        Invalidate,
        /// <summary>The request started waiting.</summary>
        Pause,
        /// <summary>The request stopped waiting.</summary>
        Continue,
        /// <summary>The state was replaced: initial data taken, a cancelled request reverted, or a reset.</summary>
        SetState,
    }

    /// <summary>The kind of a <see cref="QueryCacheEvent"/>, TanStack Query's query cache event types.</summary>
    public enum QueryCacheEventType
    {
        /// <summary>An entry was added to the cache.</summary>
        Added,
        /// <summary>An entry was removed from the cache.</summary>
        Removed,
        /// <summary>An entry's state changed; <see cref="QueryCacheEvent.Action"/> says how.</summary>
        Updated,
        /// <summary>A mounted query started reading the entry.</summary>
        ObserverAdded,
        /// <summary>A mounted query stopped reading the entry.</summary>
        ObserverRemoved,
        /// <summary>A mounted query's result over the entry changed.</summary>
        ObserverResultsUpdated,
        /// <summary>A mounted query committed options unequal to those it committed before; the entry is the
        /// one it reads after that commit.</summary>
        ObserverOptionsUpdated,
    }

    /// <summary>What a <see cref="QueryCache.Subscribe"/> listener is handed.</summary>
    public sealed class QueryCacheEvent
    {
        internal QueryCacheEvent(QueryCacheEventType type, Query query, QueryActionType? action = null, QueryObserver? observer = null)
        {
            Type = type;
            Query = query;
            Action = action;
            Observer = observer;
        }

        /// <summary>What happened.</summary>
        public QueryCacheEventType Type { get; }

        /// <summary>The entry it happened to.</summary>
        public Query Query { get; }

        /// <summary>How the state changed, for <see cref="QueryCacheEventType.Updated"/>; null otherwise.</summary>
        public QueryActionType? Action { get; }

        /// <summary>The observer added, removed or handed options, for
        /// <see cref="QueryCacheEventType.ObserverAdded"/>, <see cref="QueryCacheEventType.ObserverRemoved"/> and
        /// <see cref="QueryCacheEventType.ObserverOptionsUpdated"/>; null otherwise.</summary>
        public QueryObserver? Observer { get; }
    }

    /// <summary>
    /// What a query function built with the context-taking constructor of
    /// <see cref="QueryOptions{TQueryFnData, TData}"/> is handed, TanStack Query's <c>QueryFunctionContext</c>.
    /// </summary>
    public class QueryFunctionContext
    {
        private readonly CancellationToken _token;
        private readonly Action? _onTokenRead;

        internal QueryFunctionContext(
            QueryClient client, QueryKey queryKey, IReadOnlyDictionary<string, object?>? meta, CancellationToken token,
            Action? onTokenRead)
        {
            Client = client;
            QueryKey = queryKey;
            Meta = meta;
            _token = token;
            _onTokenRead = onTokenRead;
        }

        // A page's context, which reading the token marks the request through as the context it came from does.
        private protected QueryFunctionContext(QueryFunctionContext source)
            : this(source.Client, source.QueryKey, source.Meta, source._token, source._onTokenRead)
        {
        }

        // The token without marking the request: the paging between two pages reads it, which is not the function's
        // read.
        internal bool IsCancellationRequested => _token.IsCancellationRequested;

        /// <summary>The client the request runs in.</summary>
        public QueryClient Client { get; }

        /// <summary>The key of the entry the request fills.</summary>
        public QueryKey QueryKey { get; }

        /// <summary>The options' <see cref="QueryOptionsBase{TQueryFnData, TData}.Meta"/>.</summary>
        public IReadOnlyDictionary<string, object?>? Meta { get; }

        /// <summary>
        /// TanStack Query's <c>signal</c>: cancelled in the cases
        /// <see cref="QueryOptions{TQueryFnData, TData}.QueryFn"/> lists. Reading it is what makes the request one
        /// whose last reader leaving cancels it, as reading v5's <c>signal</c> is.
        /// </summary>
        public CancellationToken CancellationToken
        {
            get
            {
                _onTokenRead?.Invoke();
                return _token;
            }
        }
    }

    /// <summary>
    /// TanStack Query's <c>QueryCache</c>: the entries a <see cref="QueryClient"/> holds, the callbacks that run for
    /// every one of them, and the events a listener can follow. Hand one to
    /// <see cref="QueryClientOptions.QueryCache"/>; a client given none makes its own. Main thread only, like the
    /// rest of Velvet.
    /// </summary>
    /// <remarks>
    /// <see cref="OnSuccess"/>, and <see cref="OnSettled"/> after it, run once a request's data has landed and the
    /// entry's readers have been told; a throw from either makes the request's outcome that exception, the data
    /// kept, as v5's does. <see cref="OnError"/>, and <see cref="OnSettled"/> after it, run once a request has failed
    /// for good and when <see cref="QueryClient.CancelQueries"/> cancels one without reverting it; a throw from
    /// <see cref="OnError"/> skips <see cref="OnSettled"/> and is what a caller waiting on the request is handed.
    /// Neither runs for data written with <see cref="QueryClient.SetQueryData{T}(QueryKey, T)"/>.
    /// </remarks>
    public sealed class QueryCache
    {
        // Keyed by QueryEntry.Id, and in insertion order beside it, as TanStack's Map iterates.
        private readonly Dictionary<QueryKey, QueryEntry> _entries = new();
        private readonly List<QueryEntry> _ordered = new();
        private readonly List<Action<QueryCacheEvent>> _listeners = new();

        /// <summary>Creates an empty cache.</summary>
        public QueryCache()
        {
            StoreSubscribe = onStoreChange => Subscribe(_ => onStoreChange());
        }

        /// <summary>Runs when a request fails for good, with its exception and the entry.</summary>
        public Action<Exception, Query>? OnError { get; init; }

        /// <summary>Runs when a request's data lands, with the data the query function returned and the
        /// entry.</summary>
        public Action<object?, Query>? OnSuccess { get; init; }

        /// <summary>Runs after <see cref="OnSuccess"/> or <see cref="OnError"/>, with the data that landed or, after a
        /// failure, the data the entry holds, the exception or null, and the entry.</summary>
        public Action<object?, Exception?, Query>? OnSettled { get; init; }

        // One delegate per cache, so a UseSyncExternalStore over it subscribes again only when the cache changes.
        internal Func<Action, Action> StoreSubscribe { get; }

        /// <summary>
        /// Calls <paramref name="listener"/> with every event from now on, until the returned action is called. A
        /// listener already subscribed is not added twice.
        /// </summary>
        /// <returns>The action that unsubscribes the listener.</returns>
        public Action Subscribe(Action<QueryCacheEvent> listener)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            if (!_listeners.Contains(listener)) _listeners.Add(listener);
            return () => _listeners.Remove(listener);
        }

        /// <summary>Every entry in the cache, in the order they were added, those gone unread past their
        /// garbage-collection time left out.</summary>
        public IReadOnlyList<Query> GetAll() => FindAll();

        /// <summary>The first entry <paramref name="filters"/> selects, with <see cref="QueryFilters.Exact"/> true
        /// unless it is set; null when none is.</summary>
        public Query? Find(QueryFilters filters)
        {
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            var exact = filters.Exact.HasValue ? filters : filters with { Exact = true };
            foreach (var entry in Live())
            {
                if (QueryFilters.Match(exact, entry)) return entry.Query;
            }
            return null;
        }

        /// <summary>The entries <paramref name="filters"/> selects, in the order they were added; null selects
        /// every one.</summary>
        public IReadOnlyList<Query> FindAll(QueryFilters? filters = null)
        {
            var found = new List<Query>();
            foreach (var entry in Live())
            {
                if (QueryFilters.Match(filters, entry)) found.Add(entry.Query);
            }
            return found;
        }

        /// <summary>
        /// Removes <paramref name="query"/> and cancels its request, as TanStack Query's <c>remove</c> does; a
        /// mounted query reading it moves to a new entry, as after <see cref="QueryClient.RemoveQueries"/>.
        /// Nothing happens for an entry no longer in the cache.
        /// </summary>
        public void Remove(Query query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            query.Entry.Client.RemoveEntry(query.Entry);
        }

        /// <summary>Removes every entry, cancelling their requests, and announces each removal.</summary>
        public void Clear()
        {
            foreach (var entry in _ordered.ToArray())
            {
                entry.Client.RemoveEntry(entry);
            }
        }

        internal bool TryGet(QueryKey id, out QueryEntry entry) => _entries.TryGetValue(id, out entry!);

        internal int Count => _entries.Count;

        internal QueryEntry[] Snapshot() => _ordered.ToArray();

        // TanStack's add.
        internal void Add(QueryEntry entry)
        {
            _entries.Add(entry.Id, entry);
            _ordered.Add(entry);
            Notify(new QueryCacheEvent(QueryCacheEventType.Added, entry.Query));
        }

        // The removal itself, which QueryClient.RemoveEntry announces once the entry's readers have moved.
        internal bool Detach(QueryEntry entry)
        {
            if (!_entries.TryGetValue(entry.Id, out var held) || !ReferenceEquals(held, entry)) return false;
            _entries.Remove(entry.Id);
            _ordered.Remove(entry);
            return true;
        }

        internal void Notify(QueryCacheEvent cacheEvent)
        {
            foreach (var listener in _listeners.ToArray())
            {
                listener(cacheEvent);
            }
        }

        // An entry whose time has run out reads as absent before a sweep removes it, as QueryClient's reads do.
        private IEnumerable<QueryEntry> Live()
        {
            foreach (var entry in _ordered.ToArray())
            {
                if (!entry.Client.IsExpiredNow(entry)) yield return entry;
            }
        }
    }
}
