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
        /// <summary>The last request succeeded or data was written by hand, or the result shows placeholder
        /// data.</summary>
        Success,
    }

    /// <summary>
    /// The properties of a <see cref="QueryResult{T}"/>, for <see cref="QueryOptions{TQueryFnData, TData}.NotifyOnChangeProps"/>:
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
        /// <summary><see cref="QueryResult{T}.IsPlaceholderData"/>.</summary>
        IsPlaceholderData = 1 << 10,
        /// <summary>Every property, TanStack Query's <c>'all'</c>.</summary>
        All = Status | IsPending | IsSuccess | IsError | Data | Error | IsFetching | IsStale | FailureCount | FailureReason
            | IsPlaceholderData,
    }

    /// <summary>
    /// Data a query shows while it has none of its own, or nothing: what
    /// <see cref="QueryOptions{TQueryFnData, TData}.PlaceholderData"/> returns, where TanStack Query's function
    /// returns the data or <c>undefined</c>. A value converts to it implicitly, and a null value is no data.
    /// </summary>
    /// <typeparam name="T">The query function's data type.</typeparam>
    public readonly struct QueryPlaceholder<T>
    {
        /// <summary>Wraps <paramref name="value"/>; a null value is no data.</summary>
        public QueryPlaceholder(T value)
        {
            Value = value;
            HasValue = value is not null;
        }

        /// <summary>Whether there is placeholder data.</summary>
        public bool HasValue { get; }

        /// <summary>The placeholder data; default when there is none.</summary>
        public T Value { get; }

        /// <summary>Wraps <paramref name="value"/>; a null value is no data.</summary>
        public static implicit operator QueryPlaceholder<T>(T value) => new(value);
    }

    /// <summary>Ready-made <see cref="QueryOptions{TQueryFnData, TData}.PlaceholderData"/> functions.</summary>
    public static class QueryPlaceholder
    {
        /// <summary>
        /// TanStack Query's <c>keepPreviousData</c>: the data of the last entry this component read that had
        /// some, so a component changing its key keeps showing the old key's data until the new key's lands.
        /// Pass it as <c>PlaceholderData = QueryPlaceholder.KeepPreviousData</c>.
        /// </summary>
        /// <param name="previousData">That entry's data.</param>
        /// <param name="previousQueryKey">That entry's key; null when there is no such entry.</param>
        /// <returns>The previous data, or no data when there is none.</returns>
        public static QueryPlaceholder<T> KeepPreviousData<T>(T? previousData, QueryKey? previousQueryKey)
            => previousQueryKey == null ? default : new QueryPlaceholder<T>(previousData!);
    }

    /// <summary>
    /// Options passed to <see cref="Hooks.UseQuery{TQueryFnData, TData}"/>: TanStack Query's
    /// <c>{ queryKey, queryFn }</c> with its <c>enabled</c>, <c>select</c>, <c>placeholderData</c>,
    /// <c>staleTime</c>, <c>gcTime</c>, <c>retry</c>, <c>retryDelay</c>, <c>structuralSharing</c> and
    /// <c>notifyOnChangeProps</c>. <see cref="QueryOptions{T}"/> is the form without a <see cref="Select"/> that
    /// changes the data's type.
    /// </summary>
    /// <typeparam name="TQueryFnData">What the query function returns and the entry holds.</typeparam>
    /// <typeparam name="TData">What the result's <see cref="QueryResult{T}.Data"/> holds; the query function's
    /// data unless <see cref="Select"/> turns it into something else.</typeparam>
    /// <param name="QueryKey">Names the cache entry. Queries presenting equal keys share one entry.</param>
    /// <param name="QueryFn">Fetches the entry's data. The token is cancelled when a refetch starts over this
    /// request and when the entry is removed, by garbage collection or <see cref="QueryClient.Clear"/>.</param>
    public record QueryOptions<TQueryFnData, TData>(
        QueryKey QueryKey, Func<CancellationToken, VelvetTask<TQueryFnData>> QueryFn)
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
        /// off. The same choice is made between a result's data and what <see cref="Select"/> or
        /// <see cref="PlaceholderData"/> produces next, where a function given here applies when
        /// <typeparamref name="TData"/> is <typeparamref name="TQueryFnData"/>. What counts as deeply equal is
        /// described in react-migration.md §1-2b.
        /// </summary>
        public Func<TQueryFnData?, TQueryFnData, TQueryFnData>? StructuralSharing { get; init; }

        /// <summary>
        /// The result properties whose change re-renders the component, TanStack Query's
        /// <c>notifyOnChangeProps</c>. When null, a change re-renders it only if it is to a property the
        /// component has read from a result, as <c>useQuery</c> tracks them by default, or at any change to
        /// the result while none has been read; <see cref="QueryProperties.All"/> re-renders at every change
        /// to the result.
        /// </summary>
        public QueryProperties? NotifyOnChangeProps { get; init; }

        /// <summary>
        /// TanStack Query's <c>enabled</c>. False fetches nothing on its own — not on mount, not on
        /// invalidation — and reports the result not stale; <see cref="QueryResult{T}.Refetch"/> still fetches.
        /// Turning it true over stale data fetches.
        /// </summary>
        public bool Enabled { get; init; } = true;

        /// <summary>
        /// TanStack Query's <c>select</c>: turns the entry's data, and placeholder data, into the result's. Over
        /// the entry's data it runs again only when that data's instance or this function's changes, so pass a
        /// stable instance, such as one held in a field, to keep from running it each render. One that throws makes the result an
        /// error. Required when <typeparamref name="TData"/> is not <typeparamref name="TQueryFnData"/>.
        /// </summary>
        public Func<TQueryFnData, TData>? Select { get; init; }

        /// <summary>
        /// TanStack Query's <c>placeholderData</c> given as a function: data the result shows while the entry
        /// has none and is pending, handed the data and key of the last entry this component read that had
        /// data — default and null when there is none. The result is then <see cref="QueryStatus.Success"/>
        /// with <see cref="QueryResult{T}.IsPlaceholderData"/> true, and the entry is left without data.
        /// <see cref="QueryPlaceholder.KeepPreviousData{T}"/> keeps the previous key's data; <c>(_, _) =&gt; value</c>
        /// is the value form.
        /// </summary>
        public Func<TQueryFnData?, QueryKey?, QueryPlaceholder<TQueryFnData>>? PlaceholderData { get; init; }
    }

    /// <summary>
    /// <see cref="QueryOptions{TQueryFnData, TData}"/> for a query whose result holds the data its query
    /// function returns.
    /// </summary>
    /// <param name="QueryKey">Names the cache entry. Queries presenting equal keys share one entry.</param>
    /// <param name="QueryFn">Fetches the entry's data; see <see cref="QueryOptions{TQueryFnData, TData}.QueryFn"/>.</param>
    public sealed record QueryOptions<T>(QueryKey QueryKey, Func<CancellationToken, VelvetTask<T>> QueryFn)
        : QueryOptions<T, T>(QueryKey, QueryFn);

    /// <summary>
    /// What <see cref="Hooks.UseQuery{TQueryFnData, TData}"/> returns: the entry's state as this render reads it,
    /// TanStack Query's <c>UseQueryResult</c>. Reading a property marks it as one the component depends on,
    /// which is what <see cref="QueryOptions{TQueryFnData, TData}.NotifyOnChangeProps"/> leaves to decide when the
    /// component re-renders.
    /// </summary>
    public sealed class QueryResult<T>
    {
        private readonly QuerySnapshot<T> _snapshot;
        private readonly IQueryResultSource _observer;

        internal QueryResult(QuerySnapshot<T> snapshot, IQueryResultSource observer)
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
        /// <summary>The last successful result, kept through a later failure and through a refetch, or the
        /// placeholder data; default while there is neither.</summary>
        public T? Data => Read(QueryProperties.Data, _snapshot.Data);
        /// <summary>The last request's failure, under <see cref="QueryStatus.Error"/>; null otherwise.</summary>
        public Exception? Error => Read(QueryProperties.Error, _snapshot.Error);
        /// <summary>True while a request for the entry is in flight, retries waiting out their delay
        /// included, or is about to start because this component is mounting over, or has just changed its
        /// key to, an entry with no data or stale data.</summary>
        public bool IsFetching => Read(QueryProperties.IsFetching, _snapshot.IsFetching);
        /// <summary>True when the query is enabled and the entry has no data, has been invalidated, or holds
        /// data at least as old as the query's stale time.</summary>
        public bool IsStale => Read(QueryProperties.IsStale, _snapshot.IsStale);
        /// <summary>How many times the request in flight or the last one has failed, TanStack Query's
        /// <c>failureCount</c>: zero once a request lands, and one more than the retries made when the last
        /// request failed for good.</summary>
        public int FailureCount => Read(QueryProperties.FailureCount, _snapshot.FailureCount);
        /// <summary>The exception of the latest failure of the request in flight or the last one, TanStack
        /// Query's <c>failureReason</c>; null once a request lands.</summary>
        public Exception? FailureReason => Read(QueryProperties.FailureReason, _snapshot.FailureReason);
        /// <summary>True when <see cref="Data"/> is
        /// <see cref="QueryOptions{TQueryFnData, TData}.PlaceholderData"/>'s rather than the entry's, TanStack
        /// Query's <c>isPlaceholderData</c>.</summary>
        public bool IsPlaceholderData => Read(QueryProperties.IsPlaceholderData, _snapshot.IsPlaceholderData);

        /// <summary>
        /// Fetches the entry again, TanStack Query's <c>refetch()</c>, whether or not the query is enabled: a
        /// request in flight over data the entry holds is cancelled and started over, and one for an entry
        /// still waiting on its first result is joined. Does nothing before the component's effects have run,
        /// and after it unmounts.
        /// </summary>
        public void Refetch() => _observer.Refetch();

        private TValue Read<TValue>(QueryProperties property, TValue value)
        {
            _observer.Track(property);
            return value;
        }
    }

    // What a QueryResult reaches back into, whatever the type its observer reads the entry as.
    internal interface IQueryResultSource
    {
        void Refetch();
        void Track(QueryProperties property);
    }

    // One reading of an entry: the properties of a QueryResult, which the observer compares to tell what changed.
    internal readonly struct QuerySnapshot<T>
    {
        internal QueryStatus Status { get; init; }
        internal bool HasData { get; init; }
        internal T? Data { get; init; }
        internal Exception? Error { get; init; }
        internal bool IsFetching { get; init; }
        internal bool IsStale { get; init; }
        internal int FailureCount { get; init; }
        internal Exception? FailureReason { get; init; }
        internal bool IsPlaceholderData { get; init; }

        internal QueryProperties DifferencesFrom(in QuerySnapshot<T> other)
        {
            var changed = QueryProperties.None;
            if (Status != other.Status) changed |= QueryProperties.Status;
            if ((Status == QueryStatus.Pending) != (other.Status == QueryStatus.Pending)) changed |= QueryProperties.IsPending;
            if ((Status == QueryStatus.Success) != (other.Status == QueryStatus.Success)) changed |= QueryProperties.IsSuccess;
            if ((Status == QueryStatus.Error) != (other.Status == QueryStatus.Error)) changed |= QueryProperties.IsError;
            if (!QueryData.Same(Data, other.Data)) changed |= QueryProperties.Data;
            if (!ReferenceEquals(Error, other.Error)) changed |= QueryProperties.Error;
            if (IsFetching != other.IsFetching) changed |= QueryProperties.IsFetching;
            if (IsStale != other.IsStale) changed |= QueryProperties.IsStale;
            if (FailureCount != other.FailureCount) changed |= QueryProperties.FailureCount;
            if (!ReferenceEquals(FailureReason, other.FailureReason)) changed |= QueryProperties.FailureReason;
            if (IsPlaceholderData != other.IsPlaceholderData) changed |= QueryProperties.IsPlaceholderData;
            return changed;
        }
    }

    internal static class QueryData
    {
        // v5 compares result properties with !==, so data is compared by instance where it is a class and
        // by value where it is a struct or a string.
        internal static bool Same<T>(T? left, T? right)
        {
            if (typeof(T).IsValueType) return System.Collections.Generic.EqualityComparer<T>.Default.Equals(left!, right!);
            return ReferenceEquals(left, right) || (left is string leftText && right is string rightText && leftText == rightText);
        }
    }

    // A render's options with the client's defaults filled in: what the render reads the entry with, and what
    // its commit hands the observer.
    internal readonly struct QuerySettings<TQueryFnData, TData>
    {
        internal QuerySettings(QueryOptions<TQueryFnData, TData> options, QueryClient client)
        {
            Options = options;
            StaleTime = QueryClient.RequireNonNegative(
                options.StaleTime ?? client.DefaultStaleTime, nameof(QueryOptions<TQueryFnData, TData>.StaleTime));
            GcTime = QueryClient.RequireNonNegative(
                options.GcTime ?? client.DefaultGcTime, nameof(QueryOptions<TQueryFnData, TData>.GcTime));
            Enabled = options.Enabled;
            Placeholder = options.PlaceholderData;
        }

        internal QueryOptions<TQueryFnData, TData> Options { get; }
        internal TimeSpan StaleTime { get; }
        internal TimeSpan GcTime { get; }
        internal bool Enabled { get; }
        internal Func<TQueryFnData?, QueryKey?, QueryPlaceholder<TQueryFnData>>? Placeholder { get; }
        internal Func<TQueryFnData, TData>? Select => Options.Select;
    }

    // The options a request runs with. An observer keeps one and rewrites it each commit; a request reads it
    // when it starts, so a rewrite reaches only the requests after it.
    internal sealed class QueryFetchOptions<T>
    {
        internal Func<CancellationToken, VelvetTask<T>> QueryFn { get; set; } = null!;
        internal int MaxRetries { get; set; }
        internal Func<int, Exception, TimeSpan> RetryDelay { get; set; } = null!;
        internal Func<T?, T, T>? StructuralSharing { get; set; }
    }

    // What an entry knows of the observers reading it, whatever the type they read its data as.
    internal abstract class QueryEntryObserver<T>
    {
        internal QueryFetchOptions<T> FetchOptions { get; } = new();
        internal TimeSpan StaleTime { get; private protected set; }
        internal bool Enabled { get; private protected set; }

        // TanStack's onQueryUpdate: the entry's state changed.
        internal abstract void OnQueryUpdate();

        // The entry this observer reads was removed, which no snapshot carries.
        internal abstract void OnRemoved();
    }

    // The per-component half of UseQuery, TanStack's QueryObserver: the options of the latest committed render,
    // and the entry that render subscribed to.
    internal sealed class QueryObserver<TQueryFnData, TData> : QueryEntryObserver<TQueryFnData>, IQueryResultSource
    {
        // Where the query has no select, the result holds the entry's data as it is.
        private static readonly Func<TQueryFnData, TData>? s_identity = typeof(TQueryFnData) == typeof(TData)
            ? (Func<TQueryFnData, TData>)(object)new Func<TQueryFnData, TQueryFnData>(data => data)
            : null;

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

        internal QueryEntry<TQueryFnData>? Entry { get; private set; }

        private QuerySettings<TQueryFnData, TData> _settings;
        private CancellationTokenSource? _staleWait;
        private (TimeSpan UpdatedAt, TimeSpan StaleTime)? _staleFor;
        private QuerySnapshot<TData> _current;
        private bool _hasCurrent;
        private QueryProperties _tracked;

        // TanStack's #selectFn / #selectResult / #selectError, and the data the select last ran on.
        private Func<TQueryFnData, TData>? _selectFn;
        private TQueryFnData _selectedFrom = default!;
        private bool _hasSelectResult;
        private TData _selectResult = default!;
        private Exception? _selectError;

        // The placeholder function the latest result was made with, and TanStack's #lastQueryWithDefinedData.
        private Func<TQueryFnData?, QueryKey?, QueryPlaceholder<TQueryFnData>>? _currentPlaceholder;
        private QueryEntry<TQueryFnData>? _lastWithData;

        public void Refetch() => Entry?.Fetch(FetchOptions, cancelRefetch: true);

        public void Track(QueryProperties property) => _tracked |= property;

        // Runs after every commit rather than on a key dependency: QueryKey compares by content, and an
        // effect's dependency array compares a reference type by instance, so a key rebuilt each render
        // would resubscribe — and refetch a stale entry — on every commit.
        // The options are taken here rather than at render, as v5's setOptions runs in an effect: until this
        // commit's subscription moves to the new key, a refetch or an invalidation reaching the old entry has
        // to run the old entry's query function.
        internal void Sync(QueryClient client, in QuerySettings<TQueryFnData, TData> settings)
        {
            var wasEnabled = Enabled;
            var options = settings.Options;
            _settings = settings;
            StaleTime = settings.StaleTime;
            Enabled = settings.Enabled;
            FetchOptions.QueryFn = options.QueryFn;
            FetchOptions.MaxRetries = options.Retry ?? client.DefaultRetry;
            FetchOptions.RetryDelay = options.RetryDelay ?? client.DefaultRetryDelay;
            FetchOptions.StructuralSharing = options.StructuralSharing;
            var key = options.QueryKey;
            var current = Entry;
            if (current != null && !current.IsRemoved && ReferenceEquals(current.Client, client) && current.Key.Equals(key))
            {
#if UNITY_EDITOR
                _reprintedKeyCommits = 0;
#endif
                // v5's setOptions hands the entry this commit's options at every commit.
                current.SetOptions(FetchOptions);
                // TanStack's shouldFetchOptionally: a query turned on over stale data fetches it.
                if (!wasEnabled && Enabled && current.IsStaleFor(StaleTime))
                {
                    current.Fetch(FetchOptions, cancelRefetch: false);
                }
                ScheduleStale();
                return;
            }
#if UNITY_EDITOR
            WarnOnReprintedKey(current, key);
#endif

            Unsubscribe();
            var entry = client.Build<TQueryFnData>(key, settings.GcTime);
            Entry = entry;
            entry.SetOptions(FetchOptions);
            entry.Subscribe(this);
            // A change that landed between the render and this subscription was delivered to no one here.
            Update();
        }

#if UNITY_EDITOR
        private int _reprintedKeyCommits;
        private bool _warnedReprintedKey;

        // One change to a key that prints the same can be a real change between two values that print alike;
        // the second commit running such a change is what a key rebuilt unequal on every render produces.
        private void WarnOnReprintedKey(QueryEntry<TQueryFnData>? previous, QueryKey key)
        {
            if (previous == null || previous.Key.Equals(key) || previous.Key.ToString() != key.ToString())
            {
                _reprintedKeyCommits = 0;
                return;
            }
            if (++_reprintedKeyCommits < 2 || _warnedReprintedKey) return;
            _warnedReprintedKey = true;
            FiberLogger.LogWarning("UseQuery",
                $"UseQuery<{typeof(TQueryFnData).Name}>: query key {key} is not equal to the previous commit's though it prints " +
                "the same, so each commit reads a new entry and fetches it. A record or tuple compares a collection " +
                "it holds by reference; give the collection a key part of its own (react-migration.md §1-2b).");
        }
#endif

        private void Unsubscribe()
        {
            CancelStaleWait();
            var entry = Entry;
            if (entry == null) return;
            Entry = null;
            entry.Unsubscribe(this);
        }

        internal override void OnRemoved()
        {
            CancelStaleWait();
            OnChange();
        }

        internal override void OnQueryUpdate() => Update();

        // TanStack's updateResult: the entry changed, and the component re-renders if a property it has read
        // is among what differs from the reading its latest render took, or if it has read none.
        private void Update()
        {
            var entry = Entry;
            var next = CreateResult(entry, _settings);
            if (entry is { HasData: true }) _lastWithData = entry;
            var changed = _hasCurrent ? _current.DifferencesFrom(next) : QueryProperties.All;
            if (changed != QueryProperties.None)
            {
                var first = !_hasCurrent;
                _current = next;
                _hasCurrent = true;
                if (first || Notifies(changed)) OnChange();
            }

            ScheduleStale();
        }

        // TanStack's #updateStaleTimeout: fresh data turns stale when its age reaches the stale time, and a
        // component watching IsStale re-renders then. The wait is polled once a frame on the client's clock,
        // as a retry's is, and is scheduled only for a component that would be told, since Update decides
        // that again when the wait ends. A disabled query never reports stale, so it has nothing to wait for.
        private void ScheduleStale()
        {
            var entry = Entry;
            // MUTANT_SURVIVES(equivalent, clause removed): a disabled query reports IsStale false, so the wait would end in no change to report.
            if (entry == null || !Enabled || StaleTime == TimeSpan.MaxValue || entry.IsStaleFor(StaleTime)
                || !Notifies(QueryProperties.IsStale))
            {
                CancelStaleWait();
                return;
            }

            // MUTANT_SURVIVES(equivalent, guard removed): a wait started again for the same data ends at the same moment.
            if (Equals(_staleFor, (entry.DataUpdatedAt, StaleTime))) return;
            CancelStaleWait();
            var wait = new CancellationTokenSource();
            _staleWait = wait;
            _staleFor = (entry.DataUpdatedAt, StaleTime);
            WaitStale(entry.Client, wait, entry.DataUpdatedAt, StaleTime).Forget();
        }

        private async VelvetTask WaitStale(QueryClient client, CancellationTokenSource wait, TimeSpan updatedAt, TimeSpan staleTime)
        {
            do
            {
                await VelvetTask.Yield();
            }
            while (!wait.IsCancellationRequested && client.Now - updatedAt < staleTime);
            if (wait.IsCancellationRequested) return;
            _staleWait = null;
            _staleFor = null;
            wait.Dispose();
            Update();
        }

        private void CancelStaleWait()
        {
            var wait = _staleWait;
            if (wait == null) return;
            _staleWait = null;
            _staleFor = null;
            wait.Cancel();
            wait.Dispose();
        }

        private bool Notifies(QueryProperties changed)
        {
            var listed = _settings.Options?.NotifyOnChangeProps;
            if (listed == null && _tracked == QueryProperties.None) return true;
            return (changed & (listed ?? _tracked)) != QueryProperties.None;
        }

        internal QueryResult<TData> Read(QueryEntry<TQueryFnData>? entry, in QuerySettings<TQueryFnData, TData> settings)
        {
            _current = CreateResult(entry, settings);
            _hasCurrent = true;
            return new QueryResult<TData>(_current, this);
        }

        // TanStack's createResult: the entry's state, with the fetch a subscription is about to start, then the
        // selected data, then the placeholder data, then a failure of the select.
        private QuerySnapshot<TData> CreateResult(QueryEntry<TQueryFnData>? entry, in QuerySettings<TQueryFnData, TData> settings)
        {
            var status = QueryStatus.Pending;
            var hasSource = false;
            TQueryFnData source = default!;
            Exception? error = null;
            var isFetching = false;
            var failureCount = 0;
            Exception? failureReason = null;
            var staleByTime = true;
            if (entry != null)
            {
                status = entry.Status;
                hasSource = entry.HasData;
                source = entry.HasData ? entry.Data : default!;
                error = entry.Error;
                isFetching = entry.IsFetching;
                failureCount = entry.FailureCount;
                failureReason = entry.FailureReason;
                staleByTime = entry.IsStaleFor(settings.StaleTime);
            }

            // TanStack's getOptimisticResult: a render whose subscription will fetch — a mount, a key change, or
            // the query turned on — already reports that fetch, rather than the render before the subscription's
            // own reporting a settled entry.
            var willFetch = settings.Enabled && staleByTime && (!ReferenceEquals(entry, Entry) || !Enabled);
            if (willFetch)
            {
                isFetching = true;
                failureCount = 0;
                failureReason = null;
                if (!hasSource)
                {
                    status = QueryStatus.Pending;
                    error = null;
                }
            }

            var hasData = false;
            TData data = default!;
            if (hasSource) hasData = SelectData(source, settings, out data);

            var isPlaceholderData = false;
            TData placeholder = default!;
            if (settings.Placeholder != null && status == QueryStatus.Pending && TryPlaceholder(settings, out placeholder))
            {
                status = QueryStatus.Success;
                data = Share(_hasCurrent ? _current.Data : default, placeholder, settings.Options.StructuralSharing);
                hasData = true;
                isPlaceholderData = true;
                _currentPlaceholder = settings.Placeholder;
            }

            if (_selectError != null)
            {
                error = _selectError;
                data = _selectResult;
                hasData = _hasSelectResult;
                status = QueryStatus.Error;
            }

            return new QuerySnapshot<TData>
            {
                Status = status,
                HasData = hasData,
                Data = hasData ? data : default,
                Error = error,
                IsFetching = isFetching,
                IsStale = settings.Enabled && staleByTime,
                FailureCount = failureCount,
                FailureReason = failureReason,
                IsPlaceholderData = isPlaceholderData,
            };
        }

        // The select runs again only when the entry's data or the select's instance changed since it last ran,
        // so a component comparing the result's data by reference sees no change where neither did.
        private bool SelectData(TQueryFnData source, in QuerySettings<TQueryFnData, TData> settings, out TData data)
        {
            var select = settings.Select;
            if (select == null)
            {
                data = s_identity!(source);
                return true;
            }

            if (ReferenceEquals(select, _selectFn) && QueryData.Same(source, _selectedFrom))
            {
                data = _selectResult;
                return _hasSelectResult;
            }

            _selectFn = select;
            _selectedFrom = source;
            try
            {
                data = Share(_hasCurrent ? _current.Data : default, select(source), settings.Options.StructuralSharing);
                _selectResult = data;
                _hasSelectResult = true;
                _selectError = null;
                return true;
            }
            catch (Exception selectError)
            {
                _selectError = selectError;
                data = default!;
                // MUTANT_SURVIVES(equivalent, literal): a select error replaces the data and its presence with the last good selection's.
                return false;
            }
        }

        // The placeholder of the result before is kept while it was made by the same function; otherwise the
        // function is asked again, with the data of the last entry this observer read that had some.
        private bool TryPlaceholder(in QuerySettings<TQueryFnData, TData> settings, out TData placeholder)
        {
            if (_hasCurrent && _current.IsPlaceholderData && ReferenceEquals(settings.Placeholder, _currentPlaceholder))
            {
                placeholder = _current.Data!;
                return true;
            }

            var previous = _lastWithData;
            var produced = settings.Placeholder!(
                previous != null ? previous.Data : default, previous?.Key);
            placeholder = default!;
            if (!produced.HasValue) return false;
            var select = settings.Select;
            if (select == null)
            {
                placeholder = s_identity!(produced.Value);
                return true;
            }

            try
            {
                placeholder = select(produced.Value);
                _selectError = null;
            }
            catch (Exception selectError)
            {
                _selectError = selectError;
            }
            return true;
        }

        // TanStack's replaceData: a function given as structuralSharing replaces the default comparison, and
        // can only be handed data of the type it was written for.
        private static TData Share(TData? previous, TData next, Func<TQueryFnData?, TQueryFnData, TQueryFnData>? sharing)
        {
            if (sharing == null) return QueryStructuralSharing.Replace(previous, next);
            return sharing is Func<TData?, TData, TData> sameType ? sameType(previous, next) : next;
        }
    }
}
