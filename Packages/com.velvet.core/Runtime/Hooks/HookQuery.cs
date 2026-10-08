#nullable enable
using System;
using System.Threading;

namespace Velvet
{
    /// <summary>
    /// Whether a query has data, TanStack Query's <c>status</c>. Whether a request is in flight is the
    /// separate <see cref="QueryResult{T}.IsFetching"/>, so a query refetching over data it already holds
    /// stays <see cref="Success"/>.
    /// </summary>
    public enum QueryStatus
    {
        /// <summary>No data yet, and no failure since the latest request started.</summary>
        Pending,
        /// <summary>The last request failed. Data an earlier request produced is still available.</summary>
        Error,
        /// <summary>The last request succeeded.</summary>
        Success,
    }

    /// <summary>
    /// Options passed to <see cref="Hooks.UseQuery{T}"/>: TanStack Query's <c>{ queryKey, queryFn }</c> with
    /// its <c>staleTime</c> and <c>gcTime</c>.
    /// </summary>
    /// <param name="QueryKey">Names the cache entry. Queries presenting equal keys share one entry.</param>
    /// <param name="QueryFn">Fetches the entry's data. The token is cancelled when a refetch starts over this
    /// request and when the entry is removed, by garbage collection or <see cref="QueryClient.Clear"/>.</param>
    public sealed record QueryOptions<T>(QueryKey QueryKey, Func<CancellationToken, VelvetTask<T>> QueryFn)
    {
        /// <summary>Overrides <see cref="QueryClient.DefaultStaleTime"/> for this query.</summary>
        public TimeSpan? StaleTime { get; init; }

        /// <summary>Overrides <see cref="QueryClient.DefaultGcTime"/> for this query. An entry keeps the
        /// longest time any query subscribing to it asked for.</summary>
        public TimeSpan? GcTime { get; init; }
    }

    /// <summary>
    /// What <see cref="Hooks.UseQuery{T}"/> returns: the entry's state as this render reads it, TanStack
    /// Query's <c>UseQueryResult</c>.
    /// </summary>
    public sealed class QueryResult<T>
    {
        private readonly Action _refetch;

        internal QueryResult(QueryStatus status, T? data, Exception? error, bool isFetching, bool isStale, Action refetch)
        {
            Status = status;
            Data = data;
            Error = error;
            IsFetching = isFetching;
            IsStale = isStale;
            _refetch = refetch;
        }

        /// <summary>Whether the query has data; see <see cref="QueryStatus"/>.</summary>
        public QueryStatus Status { get; }
        /// <summary>True under <see cref="QueryStatus.Pending"/>.</summary>
        public bool IsPending => Status == QueryStatus.Pending;
        /// <summary>True under <see cref="QueryStatus.Success"/>.</summary>
        public bool IsSuccess => Status == QueryStatus.Success;
        /// <summary>True under <see cref="QueryStatus.Error"/>.</summary>
        public bool IsError => Status == QueryStatus.Error;
        /// <summary>The last successful result, kept through a later failure and through a refetch; default
        /// while the entry has none.</summary>
        public T? Data { get; }
        /// <summary>The last request's failure, under <see cref="QueryStatus.Error"/>; null otherwise.</summary>
        public Exception? Error { get; }
        /// <summary>True while a request for the entry is in flight, or is about to start because this
        /// component is mounting over, or has just changed its key to, an entry with no data or stale
        /// data.</summary>
        public bool IsFetching { get; }
        /// <summary>True when the entry has no data, has been invalidated, or holds data at least as old as the
        /// query's stale time.</summary>
        public bool IsStale { get; }

        /// <summary>
        /// Fetches the entry again, TanStack Query's <c>refetch()</c>: a request in flight over data the entry
        /// holds is cancelled and started over, and one for an entry still waiting on its first result is
        /// joined. Does nothing before the component's effects have run, and after it unmounts.
        /// </summary>
        public void Refetch() => _refetch();
    }

    // The per-component half of UseQuery, TanStack's QueryObserver: the options of the latest committed render,
    // and the entry that render subscribed to.
    internal sealed class QueryObserver<T>
    {
        private readonly Action _unsubscribe;

        internal QueryObserver(Action onChange)
        {
            OnChange = onChange;
            _unsubscribe = Unsubscribe;
            Refetch = () => Entry?.Fetch(QueryFn, cancelRefetch: true);
            UnmountEffect = () => _unsubscribe;
        }

        internal Action OnChange { get; }
        internal Action Refetch { get; }
        internal Func<Action?> UnmountEffect { get; }

        internal Func<CancellationToken, VelvetTask<T>> QueryFn { get; private set; } = null!;
        internal TimeSpan StaleTime { get; private set; }

        internal QueryEntry<T>? Entry { get; private set; }

        private QueryEntry<T>? _renderedEntry;
        private int _renderedVersion;

        // Runs after every commit rather than on a key dependency: QueryKey compares by content, and an
        // effect's dependency array compares a reference type by instance, so a key rebuilt each render
        // would resubscribe — and refetch a stale entry — on every commit.
        // The options are taken here rather than at render, as v5's setOptions runs in an effect: until this
        // commit's subscription moves to the new key, a refetch or an invalidation reaching the old entry has
        // to run the old entry's query function.
        internal void Sync(QueryClient client, QueryKey key, Func<CancellationToken, VelvetTask<T>> queryFn,
            TimeSpan staleTime, TimeSpan gcTime)
        {
            QueryFn = queryFn;
            StaleTime = staleTime;
            var current = Entry;
            if (current != null && !current.IsRemoved && ReferenceEquals(current.Client, client) && current.Key.Equals(key))
            {
#if UNITY_EDITOR
                _reprintedKeyCommits = 0;
#endif
                return;
            }
#if UNITY_EDITOR
            WarnOnReprintedKey(current, key);
#endif

            Unsubscribe();
            var entry = client.Build<T>(key, gcTime);
            Entry = entry;
            entry.Subscribe(this);
            // A change that landed between the render and this subscription was delivered to no one here.
            if (!ReferenceEquals(entry, _renderedEntry) || entry.Version != _renderedVersion)
            {
                OnChange();
            }
        }

#if UNITY_EDITOR
        private int _reprintedKeyCommits;
        private bool _warnedReprintedKey;

        // One change to a key that prints the same can be a real change between two values that print alike;
        // the second commit running such a change is what a key rebuilt unequal on every render produces.
        private void WarnOnReprintedKey(QueryEntry<T>? previous, QueryKey key)
        {
            if (previous == null || previous.Key.Equals(key) || previous.Key.ToString() != key.ToString())
            {
                _reprintedKeyCommits = 0;
                return;
            }
            if (++_reprintedKeyCommits < 2 || _warnedReprintedKey) return;
            _warnedReprintedKey = true;
            FiberLogger.LogWarning("UseQuery",
                $"UseQuery<{typeof(T).Name}>: query key {key} is not equal to the previous commit's though it prints " +
                "the same, so each commit reads a new entry and fetches it. A record or tuple compares a collection " +
                "it holds by reference; give the collection a key part of its own (react-migration.md §1-2b).");
        }
#endif

        private void Unsubscribe()
        {
            var entry = Entry;
            if (entry == null) return;
            Entry = null;
            entry.Unsubscribe(this);
        }

        internal QueryResult<T> Read(QueryEntry<T>? entry, TimeSpan staleTime)
        {
            _renderedEntry = entry;
            _renderedVersion = entry?.Version ?? 0;
            if (entry == null)
            {
                return new QueryResult<T>(QueryStatus.Pending, default, null, isFetching: true, isStale: true, Refetch);
            }

            var isStale = entry.IsStaleFor(staleTime);
            // TanStack's getOptimisticResult: a render whose subscription will fetch already reports that
            // fetch, rather than the render before the subscription's own reporting a settled entry.
            var willFetch = !ReferenceEquals(entry, Entry) && isStale;
            var status = entry.Status;
            var error = entry.Error;
            if (willFetch && !entry.HasData)
            {
                status = QueryStatus.Pending;
                error = null;
            }

            return new QueryResult<T>(status, entry.HasData ? entry.Data : default, error,
                isFetching: entry.IsFetching || willFetch, isStale, Refetch);
        }
    }
}
