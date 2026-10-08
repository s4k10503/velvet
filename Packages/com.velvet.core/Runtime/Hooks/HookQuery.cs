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
    /// The properties of a <see cref="QueryResult{T}"/>, for <see cref="QueryOptions{T}.NotifyOnChangeProps"/>:
    /// TanStack Query's list of result property names.
    /// </summary>
    [Flags]
    public enum QueryProperties
    {
        /// <summary>No property.</summary>
        None = 0,
        /// <summary><see cref="QueryResult{T}.Status"/>.</summary>
        Status = 1 << 0,
        /// <summary><see cref="QueryResult{T}.IsPending"/>.</summary>
        IsPending = 1 << 1,
        /// <summary><see cref="QueryResult{T}.IsSuccess"/>.</summary>
        IsSuccess = 1 << 2,
        /// <summary><see cref="QueryResult{T}.IsError"/>.</summary>
        IsError = 1 << 3,
        /// <summary><see cref="QueryResult{T}.Data"/>.</summary>
        Data = 1 << 4,
        /// <summary><see cref="QueryResult{T}.Error"/>.</summary>
        Error = 1 << 5,
        /// <summary><see cref="QueryResult{T}.IsFetching"/>.</summary>
        IsFetching = 1 << 6,
        /// <summary><see cref="QueryResult{T}.IsStale"/>.</summary>
        IsStale = 1 << 7,
        /// <summary><see cref="QueryResult{T}.FailureCount"/>.</summary>
        FailureCount = 1 << 8,
        /// <summary><see cref="QueryResult{T}.FailureReason"/>.</summary>
        FailureReason = 1 << 9,
        /// <summary>Every property, TanStack Query's <c>'all'</c>.</summary>
        All = Status | IsPending | IsSuccess | IsError | Data | Error | IsFetching | IsStale | FailureCount | FailureReason,
    }

    /// <summary>
    /// Options passed to <see cref="Hooks.UseQuery{T}"/>: TanStack Query's <c>{ queryKey, queryFn }</c> with
    /// its <c>staleTime</c>, <c>gcTime</c>, <c>retry</c>, <c>retryDelay</c>, <c>structuralSharing</c> and
    /// <c>notifyOnChangeProps</c>.
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

        /// <summary>Overrides <see cref="QueryClient.DefaultRetry"/> for this query: how many times a failed
        /// request runs again before the failure is the entry's error, TanStack Query's <c>retry</c> given as
        /// a number. Zero or less retries nothing.</summary>
        public int? Retry { get; init; }

        /// <summary>Overrides <see cref="QueryClient.DefaultRetryDelay"/> for this query: the wait before the
        /// next request, from the number of failures before this one and this failure's exception, TanStack
        /// Query's <c>retryDelay</c>.</summary>
        public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

        /// <summary>
        /// Chooses the data an entry holds when a request lands, from the data it held, default when it held
        /// none, and the data that arrived: TanStack Query's <c>structuralSharing</c> given as a function.
        /// When null, the data that arrived keeps the instance the entry held where the two are deeply equal
        /// and keeps the equal parts of it where they are not; <c>(_, arrived) =&gt; arrived</c> turns that
        /// off. What counts as deeply equal is described in react-migration.md §1-2b.
        /// </summary>
        public Func<T?, T, T>? StructuralSharing { get; init; }

        /// <summary>
        /// The result properties whose change re-renders the component, TanStack Query's
        /// <c>notifyOnChangeProps</c>. When null, a change re-renders it only if it is to a property the
        /// component has read from a result, as <c>useQuery</c> tracks them by default, or at any change to
        /// the result while none has been read; <see cref="QueryProperties.All"/> re-renders at every change
        /// to the result.
        /// </summary>
        public QueryProperties? NotifyOnChangeProps { get; init; }
    }

    /// <summary>
    /// What <see cref="Hooks.UseQuery{T}"/> returns: the entry's state as this render reads it, TanStack
    /// Query's <c>UseQueryResult</c>. Reading a property marks it as one the component depends on, which is
    /// what <see cref="QueryOptions{T}.NotifyOnChangeProps"/> leaves to decide when the component re-renders.
    /// </summary>
    public sealed class QueryResult<T>
    {
        private readonly QuerySnapshot<T> _snapshot;
        private readonly QueryObserver<T> _observer;

        internal QueryResult(QuerySnapshot<T> snapshot, QueryObserver<T> observer)
        {
            _snapshot = snapshot;
            _observer = observer;
        }

        /// <summary>Whether the query has data; see <see cref="QueryStatus"/>.</summary>
        public QueryStatus Status => Read(QueryProperties.Status, _snapshot.Status);
        /// <summary>True under <see cref="QueryStatus.Pending"/>.</summary>
        public bool IsPending => Read(QueryProperties.IsPending, _snapshot.Status == QueryStatus.Pending);
        /// <summary>True under <see cref="QueryStatus.Success"/>.</summary>
        public bool IsSuccess => Read(QueryProperties.IsSuccess, _snapshot.Status == QueryStatus.Success);
        /// <summary>True under <see cref="QueryStatus.Error"/>.</summary>
        public bool IsError => Read(QueryProperties.IsError, _snapshot.Status == QueryStatus.Error);
        /// <summary>The last successful result, kept through a later failure and through a refetch; default
        /// while the entry has none.</summary>
        public T? Data => Read(QueryProperties.Data, _snapshot.Data);
        /// <summary>The last request's failure, under <see cref="QueryStatus.Error"/>; null otherwise.</summary>
        public Exception? Error => Read(QueryProperties.Error, _snapshot.Error);
        /// <summary>True while a request for the entry is in flight, retries waiting out their delay
        /// included, or is about to start because this component is mounting over, or has just changed its
        /// key to, an entry with no data or stale data.</summary>
        public bool IsFetching => Read(QueryProperties.IsFetching, _snapshot.IsFetching);
        /// <summary>True when the entry has no data, has been invalidated, or holds data at least as old as the
        /// query's stale time.</summary>
        public bool IsStale => Read(QueryProperties.IsStale, _snapshot.IsStale);
        /// <summary>How many times the request in flight or the last one has failed, TanStack Query's
        /// <c>failureCount</c>: zero once a request lands, and one more than the retries made when the last
        /// request failed for good.</summary>
        public int FailureCount => Read(QueryProperties.FailureCount, _snapshot.FailureCount);
        /// <summary>The exception of the latest failure of the request in flight or the last one, TanStack
        /// Query's <c>failureReason</c>; null once a request lands.</summary>
        public Exception? FailureReason => Read(QueryProperties.FailureReason, _snapshot.FailureReason);

        /// <summary>
        /// Fetches the entry again, TanStack Query's <c>refetch()</c>: a request in flight over data the entry
        /// holds is cancelled and started over, and one for an entry still waiting on its first result is
        /// joined. Does nothing before the component's effects have run, and after it unmounts.
        /// </summary>
        public void Refetch() => _observer.Refetch();

        private TValue Read<TValue>(QueryProperties property, TValue value)
        {
            _observer.Track(property);
            return value;
        }
    }

    // One reading of an entry: the properties of a QueryResult, which the observer compares to tell what changed.
    internal readonly struct QuerySnapshot<T>
    {
        internal QueryStatus Status { get; init; }
        internal T? Data { get; init; }
        internal Exception? Error { get; init; }
        internal bool IsFetching { get; init; }
        internal bool IsStale { get; init; }
        internal int FailureCount { get; init; }
        internal Exception? FailureReason { get; init; }

        // v5 compares result properties with !==, so data is compared by instance where it is a class and
        // by value where it is a struct.
        internal QueryProperties DifferencesFrom(in QuerySnapshot<T> other)
        {
            var changed = QueryProperties.None;
            if (Status != other.Status) changed |= QueryProperties.Status;
            if ((Status == QueryStatus.Pending) != (other.Status == QueryStatus.Pending)) changed |= QueryProperties.IsPending;
            if ((Status == QueryStatus.Success) != (other.Status == QueryStatus.Success)) changed |= QueryProperties.IsSuccess;
            if ((Status == QueryStatus.Error) != (other.Status == QueryStatus.Error)) changed |= QueryProperties.IsError;
            if (!SameData(Data, other.Data)) changed |= QueryProperties.Data;
            if (!ReferenceEquals(Error, other.Error)) changed |= QueryProperties.Error;
            if (IsFetching != other.IsFetching) changed |= QueryProperties.IsFetching;
            if (IsStale != other.IsStale) changed |= QueryProperties.IsStale;
            if (FailureCount != other.FailureCount) changed |= QueryProperties.FailureCount;
            if (!ReferenceEquals(FailureReason, other.FailureReason)) changed |= QueryProperties.FailureReason;
            return changed;
        }

        private static bool SameData(T? left, T? right)
            => typeof(T).IsValueType ? System.Collections.Generic.EqualityComparer<T>.Default.Equals(left!, right!) : ReferenceEquals(left, right);
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
            UnmountEffect = () => _unsubscribe;
        }

        // Re-renders the component whatever the properties it reads, for a change no snapshot carries.
        internal Action OnChange { get; }
        internal Func<Action?> UnmountEffect { get; }

        internal Func<CancellationToken, VelvetTask<T>> QueryFn { get; private set; } = null!;
        internal TimeSpan StaleTime { get; private set; }
        internal int MaxRetries { get; private set; }
        internal Func<int, Exception, TimeSpan> RetryDelay { get; private set; } = null!;
        internal Func<T?, T, T>? StructuralSharing { get; private set; }
        private QueryProperties? NotifyOnChangeProps { get; set; }

        internal QueryEntry<T>? Entry { get; private set; }

        private QuerySnapshot<T> _current;
        private bool _hasCurrent;
        private QueryProperties _tracked;

        internal void Refetch() => Entry?.Fetch(this, cancelRefetch: true);

        internal void Track(QueryProperties property) => _tracked |= property;

        // Runs after every commit rather than on a key dependency: QueryKey compares by content, and an
        // effect's dependency array compares a reference type by instance, so a key rebuilt each render
        // would resubscribe — and refetch a stale entry — on every commit.
        // The options are taken here rather than at render, as v5's setOptions runs in an effect: until this
        // commit's subscription moves to the new key, a refetch or an invalidation reaching the old entry has
        // to run the old entry's query function.
        internal void Sync(QueryClient client, QueryOptions<T> options, TimeSpan staleTime, TimeSpan gcTime)
        {
            QueryFn = options.QueryFn;
            StaleTime = staleTime;
            MaxRetries = options.Retry ?? client.DefaultRetry;
            RetryDelay = options.RetryDelay ?? client.DefaultRetryDelay;
            StructuralSharing = options.StructuralSharing;
            NotifyOnChangeProps = options.NotifyOnChangeProps;
            var key = options.QueryKey;
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
            Update();
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

        // TanStack's updateResult: the entry changed, and the component re-renders if a property it has read
        // is among what differs from the reading its latest render took, or if it has read none.
        internal void Update()
        {
            var next = Snapshot(Entry, StaleTime);
            var changed = _hasCurrent ? _current.DifferencesFrom(next) : QueryProperties.All;
            if (changed == QueryProperties.None) return;
            var first = !_hasCurrent;
            _current = next;
            _hasCurrent = true;
            if (first || Notifies(changed)) OnChange();
        }

        private bool Notifies(QueryProperties changed)
        {
            if (NotifyOnChangeProps == null && _tracked == QueryProperties.None) return true;
            return (changed & (NotifyOnChangeProps ?? _tracked)) != QueryProperties.None;
        }

        internal QueryResult<T> Read(QueryEntry<T>? entry, TimeSpan staleTime)
        {
            var snapshot = Snapshot(entry, staleTime);
            _current = snapshot;
            _hasCurrent = true;
            return new QueryResult<T>(snapshot, this);
        }

        private QuerySnapshot<T> Snapshot(QueryEntry<T>? entry, TimeSpan staleTime)
        {
            if (entry == null)
            {
                return new QuerySnapshot<T> { Status = QueryStatus.Pending, IsFetching = true, IsStale = true };
            }

            var isStale = entry.IsStaleFor(staleTime);
            // TanStack's getOptimisticResult: a render whose subscription will fetch already reports that
            // fetch, rather than the render before the subscription's own reporting a settled entry.
            var willFetch = !ReferenceEquals(entry, Entry) && isStale;
            if (willFetch && !entry.HasData)
            {
                return new QuerySnapshot<T> { Status = QueryStatus.Pending, IsFetching = true, IsStale = isStale };
            }

            return new QuerySnapshot<T>
            {
                Status = entry.Status,
                Data = entry.HasData ? entry.Data : default,
                Error = entry.Error,
                IsFetching = entry.IsFetching || willFetch,
                IsStale = isStale,
                FailureCount = willFetch ? 0 : entry.FailureCount,
                FailureReason = willFetch ? null : entry.FailureReason,
            };
        }
    }
}
