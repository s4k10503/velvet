#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Velvet
{
    /// <summary>
    /// Defaults a <see cref="QueryClient"/> applies to every query that does not set its own, plus the clock
    /// it measures staleness and garbage collection against.
    /// </summary>
    public sealed class QueryClientOptions
    {
        /// <summary>
        /// How long a result stays fresh after it lands. TanStack Query's default, zero, makes every result
        /// stale at once, so each component mounting over it fetches again in the background.
        /// <see cref="TimeSpan.MaxValue"/> keeps a result fresh until it is invalidated.
        /// </summary>
        public TimeSpan StaleTime { get; init; } = TimeSpan.Zero;

        /// <summary>
        /// How long an entry nothing observes is kept; after that the client removes it, as the remarks on
        /// <see cref="QueryClient"/> say. TanStack Query's default is five minutes.
        /// <see cref="TimeSpan.MaxValue"/> keeps it until <see cref="QueryClient.Clear"/>.
        /// </summary>
        public TimeSpan GcTime { get; init; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// How many times a failed request runs again before the failure is the entry's error: TanStack
        /// Query's <c>retry</c> given as a number, whose default is three. Zero or less retries nothing.
        /// </summary>
        public int Retry { get; init; } = 3;

        /// <summary>
        /// The wait before the next request, from the number of failures before this one and this failure's
        /// exception: TanStack Query's <c>retryDelay</c>. When null, one second doubled at each failure and
        /// capped at thirty seconds, TanStack Query's default. The wait is measured on <see cref="Clock"/>
        /// and checked once a frame, and lasts at least one frame.
        /// </summary>
        public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

        /// <summary>
        /// Whether a query fetches again when the application becomes visible, as <see cref="NetworkSignals"/>
        /// reads it: TanStack Query's <c>refetchOnWindowFocus</c>, whose default refetches stale data.
        /// </summary>
        public QueryRefetchMode RefetchOnWindowFocus { get; init; } = QueryRefetchMode.IfStale;

        /// <summary>
        /// <see cref="RefetchOnWindowFocus"/> given as a function of the entry a query reads, for every query that
        /// sets neither form of its own; see <see cref="QueryOptions{TQueryFnData, TData}.RefetchOnWindowFocusFn"/>.
        /// It replaces <see cref="RefetchOnWindowFocus"/> when both are set.
        /// </summary>
        public Func<QueryInfo, QueryRefetchMode>? RefetchOnWindowFocusFn { get; init; }

        /// <summary>
        /// Whether a query fetches again when the device comes back online, as <see cref="NetworkSignals"/>
        /// reads it: TanStack Query's <c>refetchOnReconnect</c>, whose default refetches stale data.
        /// </summary>
        public QueryRefetchMode RefetchOnReconnect { get; init; } = QueryRefetchMode.IfStale;

        /// <summary>
        /// <see cref="RefetchOnReconnect"/> given as a function of the entry a query reads, with the terms of
        /// <see cref="RefetchOnWindowFocusFn"/>.
        /// </summary>
        public Func<QueryInfo, QueryRefetchMode>? RefetchOnReconnectFn { get; init; }

        /// <summary>
        /// Whether a request pauses while the device reads as offline, for every query that does not set its own:
        /// TanStack Query's <c>networkMode</c>, whose default is <see cref="Velvet.NetworkMode.Online"/>.
        /// </summary>
        public NetworkMode NetworkMode { get; init; } = NetworkMode.Online;

        /// <summary>
        /// Turns a key into the string that names its entry, for every query that does not set its own: TanStack
        /// Query's <c>queryKeyHashFn</c>. Two keys the function maps to one string name one entry. When null, keys
        /// are told apart as <see cref="Velvet.QueryKey"/> describes, TanStack Query's <c>hashKey</c>.
        /// </summary>
        public Func<QueryKey, string>? QueryKeyHashFn { get; init; }

        /// <summary>
        /// A monotonic clock the client reads instead of its own stopwatch, for a host that measures time
        /// differently — game time that pauses, or a clock a test advances by hand.
        /// </summary>
        public Func<TimeSpan>? Clock { get; init; }
    }

    /// <summary>
    /// Provides a <see cref="QueryClient"/> to the <see cref="Hooks.UseQuery{TQueryFnData, TData}"/> calls below it:
    /// <c>V.Provider(QueryClientContext.Ref, value: client, children: ...)</c>, TanStack Query's
    /// <c>QueryClientProvider</c>. Read it with <c>Hooks.UseContext(QueryClientContext.Ref)</c> to reach the
    /// client from a component, as <c>useQueryClient()</c> does.
    /// </summary>
    public static class QueryClientContext
    {
        /// <summary>
        /// The context <see cref="Hooks.UseQuery{TQueryFnData, TData}"/> reads. Its default is null, so a query with no
        /// Provider above it and no client passed to it throws rather than sharing a hidden one.
        /// </summary>
        public static readonly ComponentContext<QueryClient?> Ref
            = ComponentContext<QueryClient?>.Create(defaultValue: null);
    }

    /// <summary>
    /// TanStack Query's <c>QueryClient</c>: a cache of query results keyed by <see cref="QueryKey"/>, shared
    /// by every <see cref="Hooks.UseQuery{TQueryFnData, TData}"/> that reaches it. One entry holds one result and at most one
    /// request in flight, whichever component started it, and keeps the result after the components reading
    /// it unmount, until it is collected. A request in flight is kept too when its query function takes no token;
    /// one that does is cancelled when the last component reading the entry leaves, as
    /// <see cref="QueryOptions{TQueryFnData, TData}.QueryFn"/> describes.
    /// </summary>
    /// <remarks>
    /// Main thread only, like the rest of Velvet. An entry nothing observes is removed once its
    /// garbage-collection time has passed on the client's clock, as TanStack Query's timer removes it. The
    /// clock is read once a frame, as a retry's wait is, while an entry that can still expire is held: removal
    /// lands on the first frame whose reading is at or past the deadline, and an entry whose time has run out
    /// reads as absent before then. The loop holds the client weakly, so it is not what keeps a dropped client
    /// alive. An entry with a request in flight has not run out, as v5 puts its removal
    /// off while it fetches: it runs out a whole garbage-collection time after the request settles, and the
    /// clock is read every frame until then.
    /// </remarks>
    public sealed class QueryClient
    {
        // Keyed by QueryEntry.Id: the key itself, or a key standing for the string a QueryKeyHashFn made of it.
        private readonly Dictionary<QueryKey, QueryEntry> _entries = new();
        private readonly List<QueryEntry> _inactive = new();
        private static readonly Func<int, Exception, TimeSpan> s_exponentialBackoff
            = (failureCount, _) => TimeSpan.FromMilliseconds(Math.Min(1000d * Math.Pow(2d, failureCount), 30000d));

        private readonly Func<TimeSpan> _clock;
        // Advanced by every render that reads this client, so an entry whose last reader left under the current
        // value left after the render whose effects are subscribing now.
        private int _readEpoch;
        private int _observerCount;
        private bool _collecting;
        private TimeSpan _nextWalk;
        private bool _canExpire;

        /// <summary>Creates an empty client.</summary>
        /// <param name="options">Defaults and clock; null takes <see cref="QueryClientOptions"/>' own.</param>
        public QueryClient(QueryClientOptions? options = null)
        {
            options ??= new QueryClientOptions();
            DefaultStaleTime = RequireNonNegative(options.StaleTime, nameof(QueryClientOptions.StaleTime));
            DefaultGcTime = RequireNonNegative(options.GcTime, nameof(QueryClientOptions.GcTime));
            DefaultRetry = options.Retry;
            DefaultRetryDelay = options.RetryDelay ?? s_exponentialBackoff;
            DefaultNetworkMode = options.NetworkMode;
            DefaultQueryKeyHashFn = options.QueryKeyHashFn;
            DefaultRefetchOnWindowFocus = options.RefetchOnWindowFocus;
            DefaultRefetchOnReconnect = options.RefetchOnReconnect;
            DefaultRefetchOnWindowFocusFn = options.RefetchOnWindowFocusFn;
            DefaultRefetchOnReconnectFn = options.RefetchOnReconnectFn;
            if (options.Clock != null)
            {
                _clock = options.Clock;
            }
            else
            {
                var stopwatch = Stopwatch.StartNew();
                _clock = () => stopwatch.Elapsed;
            }
        }

        /// <summary>The stale time a query that sets none of its own uses.</summary>
        public TimeSpan DefaultStaleTime { get; }

        /// <summary>The garbage-collection time a query that sets none of its own uses.</summary>
        public TimeSpan DefaultGcTime { get; }

        /// <summary>The number of retries a query that sets none of its own uses.</summary>
        public int DefaultRetry { get; }

        /// <summary>The retry delay a query that sets none of its own uses.</summary>
        public Func<int, Exception, TimeSpan> DefaultRetryDelay { get; }

        /// <summary>The network mode a query that sets none of its own uses.</summary>
        public NetworkMode DefaultNetworkMode { get; }

        /// <summary>The key hash function a query that sets none of its own uses; null when keys are told apart
        /// as <see cref="QueryKey"/> describes.</summary>
        public Func<QueryKey, string>? DefaultQueryKeyHashFn { get; }

        /// <summary>Whether a query that sets none of its own fetches again when the application becomes
        /// visible.</summary>
        public QueryRefetchMode DefaultRefetchOnWindowFocus { get; }

        /// <summary>Whether a query that sets none of its own fetches again when the device comes back
        /// online.</summary>
        public QueryRefetchMode DefaultRefetchOnReconnect { get; }

        /// <summary>The function a query that sets none of its own decides whether to fetch again when the
        /// application becomes visible with; it replaces <see cref="DefaultRefetchOnWindowFocus"/>.</summary>
        public Func<QueryInfo, QueryRefetchMode>? DefaultRefetchOnWindowFocusFn { get; }

        /// <summary>The function a query that sets none of its own decides whether to fetch again when the
        /// device comes back online with; it replaces <see cref="DefaultRefetchOnReconnect"/>.</summary>
        public Func<QueryInfo, QueryRefetchMode>? DefaultRefetchOnReconnectFn { get; }

        /// <summary>
        /// Marks every entry whose key <paramref name="queryKey"/> matches stale, and fetches again each
        /// one a mounted query is reading — TanStack Query's <c>invalidateQueries({ queryKey })</c>, with the
        /// partial match of its filter that <see cref="QueryKey"/> describes. For such
        /// an entry, a request already in flight is cancelled and started over when the entry holds data, so
        /// a result fetched before the change that invalidated it does not land as current, and joined when
        /// the entry is still waiting on its first result. An entry nothing reads is not fetched: a query
        /// mounting over it later fetches it, and a request it still has in flight, one whose query function
        /// takes no token, is left to land.
        /// </summary>
        /// <param name="queryKey">The filter; null matches every entry.</param>
        /// <returns>A task that completes once the requests this started have settled, whatever they did, so
        /// a mutation callback can return it to wait for the refetch as v5's <c>onSuccess</c> does.</returns>
        public VelvetTask InvalidateQueries(QueryKey? queryKey = null)
            => InvalidateQueries(queryKey == null ? null : new QueryFilters { QueryKey = queryKey }, null);

        /// <summary>
        /// <see cref="InvalidateQueries(QueryKey)"/> over the entries <paramref name="filters"/> selects, TanStack
        /// Query's <c>invalidateQueries(filters, options)</c>. Only entries an enabled query reads are fetched
        /// again.
        /// </summary>
        /// <param name="filters">The entries to mark stale; null selects every entry.</param>
        /// <param name="options">Whether the requests it starts cancel those in flight, and whether the task
        /// faults with their failure.</param>
        public VelvetTask InvalidateQueries(QueryFilters? filters, RefetchOptions? options = null)
        {
            options ??= new RefetchOptions();
            var tasks = new List<VelvetTask>();
            foreach (var entry in SelectEntries(filters))
            {
                tasks.Add(entry.Invalidate(options.CancelRefetch));
            }
            return Settle(tasks, options.ThrowOnError);
        }

        /// <summary>
        /// Fetches again the entries <paramref name="filters"/> selects that can be: those an enabled query reads,
        /// and, for an entry nothing reads, those that were fetched before and keep the options of that fetch.
        /// TanStack Query's <c>refetchQueries</c>.
        /// </summary>
        /// <param name="filters">The entries to fetch; null selects every entry.</param>
        /// <param name="options">Whether the requests cancel those in flight, and whether the task faults with a
        /// failure.</param>
        /// <returns>A task that completes once the requests have settled.</returns>
        public VelvetTask RefetchQueries(QueryFilters? filters = null, RefetchOptions? options = null)
        {
            options ??= new RefetchOptions();
            var tasks = new List<VelvetTask>();
            foreach (var entry in SelectEntries(filters))
            {
                tasks.Add(entry.Refetch(options.CancelRefetch));
            }
            return Settle(tasks, options.ThrowOnError);
        }

        /// <summary>
        /// Cancels the requests of the entries <paramref name="filters"/> selects, TanStack Query's
        /// <c>cancelQueries</c>. The token a cancelled query function received is cancelled, and a task a
        /// caller awaits for the request, such as <see cref="FetchQuery{T}"/>'s, is cancelled with it.
        /// </summary>
        /// <param name="filters">The entries to cancel; null selects every entry.</param>
        /// <param name="revert">True, the default, puts each entry back as it stood before the request; false
        /// leaves it in <see cref="QueryStatus.Error"/> with the cancellation as its error, keeping its data.</param>
        /// <returns>A completed task, so that an <c>OnMutate</c> can await it as v5's is awaited.</returns>
        public VelvetTask CancelQueries(QueryFilters? filters = null, bool revert = true)
        {
            foreach (var entry in SelectEntries(filters))
            {
                entry.CancelRequest(revert);
            }
            return VelvetTask.CompletedTask;
        }

        /// <summary>
        /// Removes the entries <paramref name="filters"/> selects and cancels their requests, TanStack Query's
        /// <c>removeQueries</c>. A mounted query reading a removed entry re-renders without data and fetches
        /// into a new one, as after <see cref="Clear"/>.
        /// </summary>
        /// <param name="filters">The entries to remove; null selects every entry.</param>
        public void RemoveQueries(QueryFilters? filters = null)
        {
            foreach (var entry in SelectEntries(filters))
            {
                _entries.Remove(entry.Id);
                _inactive.Remove(entry);
                entry.Remove();
            }
        }

        /// <summary>
        /// Puts the entries <paramref name="filters"/> selects back to the state of one never fetched, with no
        /// data and no error, cancelling their requests, and then fetches those an enabled query reads —
        /// TanStack Query's <c>resetQueries</c>. A <see cref="QueryFilters.Type"/> of null refetches the active
        /// entries; one that is set also selects what is refetched.
        /// </summary>
        /// <param name="filters">The entries to reset; null selects every entry.</param>
        /// <param name="options">As <see cref="RefetchQueries"/>.</param>
        /// <returns>A task that completes once the fetches have settled.</returns>
        public VelvetTask ResetQueries(QueryFilters? filters = null, RefetchOptions? options = null)
        {
            foreach (var entry in SelectEntries(filters))
            {
                entry.ResetState();
            }
            var refetch = (filters ?? new QueryFilters()) with { Type = filters?.Type ?? QueryTypeFilter.Active };
            return RefetchQueries(refetch, options);
        }

        /// <summary>
        /// The data <paramref name="options"/>' entry holds when it is fresh for the query's stale time, and
        /// otherwise the data a request for it returns, TanStack Query's <c>fetchQuery</c>. A request in flight
        /// is joined. The entry is created when there is none and, with nothing reading it, collected after the
        /// query's garbage-collection time.
        /// </summary>
        /// <param name="options">The key, the query function and the options of
        /// <see cref="QueryOptions{TQueryFnData, TData}"/> that shape a request: its stale time, garbage-collection
        /// time, retries, retry delay and structural sharing. Unlike a mounted query, a request retries nothing
        /// unless <see cref="QueryOptions{TQueryFnData, TData}.Retry"/> says so, as v5's <c>fetchQuery</c> sets
        /// <c>retry: false</c>.</param>
        /// <returns>A task of the entry's data; it faults with the request's failure, and is cancelled when
        /// something cancels the request.</returns>
        /// <exception cref="InvalidOperationException">The entry holds data of another type.</exception>
        public VelvetTask<T> FetchQuery<T>(QueryOptions<T, T> options)
        {
            var entry = EntryFor(options, out var fetchOptions, out var staleTime);
            if (!entry.IsStaleFor(staleTime)) return VelvetTask.FromResult(entry.Data);
            return entry.FetchAndWait(fetchOptions, cancelRefetch: false);
        }

        /// <summary>
        /// <see cref="FetchQuery{T}"/> without the data: fills the cache ahead of the component that reads it.
        /// TanStack Query's <c>prefetchQuery</c>.
        /// </summary>
        /// <returns>A task that completes once the request has settled, whatever it did.</returns>
        public VelvetTask PrefetchQuery<T>(QueryOptions<T, T> options)
        {
            var request = FetchQuery(options);
            return Settle(new List<VelvetTask> { request }, throwOnError: false);
        }

        /// <summary>
        /// The data the entry <paramref name="options"/> names holds, and otherwise the data a request for it
        /// returns, TanStack Query's <c>ensureQueryData</c>.
        /// </summary>
        /// <param name="options">As <see cref="FetchQuery{T}"/>.</param>
        /// <param name="revalidateIfStale">True also fetches an entry whose data is stale for the query's stale
        /// time, without waiting for it.</param>
        public VelvetTask<T> EnsureQueryData<T>(QueryOptions<T, T> options, bool revalidateIfStale = false)
        {
            var entry = EntryFor(options, out _, out var staleTime);
            if (!entry.HasData) return FetchQuery(options);
            if (revalidateIfStale && entry.IsStaleFor(staleTime)) PrefetchQuery(options).Forget();
            return VelvetTask.FromResult(entry.Data);
        }

        /// <summary>
        /// The state of the entry <paramref name="queryKey"/> names, TanStack Query's <c>getQueryState</c>: null
        /// when there is no such entry or it has gone unread past its garbage-collection time.
        /// </summary>
        /// <exception cref="InvalidOperationException">The entry holds data of another type.</exception>
        public QueryState<T>? GetQueryState<T>(QueryKey queryKey)
        {
            if (queryKey == null) throw new ArgumentNullException(nameof(queryKey));
            if (!_entries.TryGetValue(Identify(queryKey, DefaultQueryKeyHashFn), out var found) || IsExpired(found, Now)) return null;
            var entry = Typed<T>(found);
            return new QueryState<T>
            {
                Status = entry.Status,
                HasData = entry.HasData,
                Data = entry.HasData ? entry.Data : default,
                Error = entry.Error,
                IsInvalidated = entry.IsInvalidated,
                FetchStatus = entry.FetchStatus,
                DataUpdatedAt = entry.HasData ? entry.DataUpdatedAt : TimeSpan.Zero,
                FailureCount = entry.FailureCount,
                FailureReason = entry.FailureReason,
            };
        }

        /// <summary>
        /// The key and data of each entry <paramref name="filters"/> selects that holds data of type
        /// <typeparamref name="T"/>, TanStack Query's <c>getQueriesData</c>. An entry holding another type is
        /// left out rather than throwing, since a filter selects across keys.
        /// </summary>
        public IReadOnlyList<KeyValuePair<QueryKey, T?>> GetQueriesData<T>(QueryFilters? filters = null)
        {
            var found = new List<KeyValuePair<QueryKey, T?>>();
            foreach (var entry in SelectEntries(filters))
            {
                if (entry is QueryEntry<T> typed) found.Add(new KeyValuePair<QueryKey, T?>(typed.Key, typed.HasData ? typed.Data : default));
            }
            return found;
        }

        /// <summary>
        /// <see cref="SetQueryData{T}(QueryKey, Func{T, T})"/> over each entry <paramref name="filters"/> selects
        /// that holds data of type <typeparamref name="T"/>, TanStack Query's <c>setQueriesData</c>. An updater
        /// returning null leaves its entry as it was.
        /// </summary>
        /// <returns>The key and the data written to each entry.</returns>
        public IReadOnlyList<KeyValuePair<QueryKey, T?>> SetQueriesData<T>(
            QueryFilters? filters, Func<T?, T> updater)
        {
            if (updater == null) throw new ArgumentNullException(nameof(updater));
            var written = new List<KeyValuePair<QueryKey, T?>>();
            foreach (var entry in SelectEntries(filters))
            {
                if (entry is not QueryEntry<T> typed) continue;
                var next = updater(typed.HasData ? typed.Data : default);
                if (next is null) continue;
                written.Add(new KeyValuePair<QueryKey, T?>(typed.Key, typed.SetData(next)));
            }
            return written;
        }

        /// <summary>
        /// The data the entry <paramref name="queryKey"/> names holds, TanStack Query's <c>getQueryData</c>:
        /// default when there is no such entry, it has no data yet, or it has gone unread past its
        /// garbage-collection time. Placeholder data is never an entry's.
        /// </summary>
        /// <exception cref="InvalidOperationException">The entry holds data of another type.</exception>
        public T? GetQueryData<T>(QueryKey queryKey)
        {
            if (queryKey == null) throw new ArgumentNullException(nameof(queryKey));
            var entry = _entries.TryGetValue(Identify(queryKey, DefaultQueryKeyHashFn), out var found) && !IsExpired(found, Now)
                ? Typed<T>(found)
                : null;
            return entry != null ? entry.Data : default;
        }

        /// <summary>
        /// Writes <paramref name="data"/> into the entry <paramref name="queryKey"/> names, creating it when there
        /// is none, TanStack Query's <c>setQueryData</c>: the entry becomes <see cref="QueryStatus.Success"/>
        /// with fresh data and no error, and the queries reading it are told, re-rendering for what changed. The data is
        /// shared structurally with what the entry held, as a request landing is. A request in flight is left
        /// running and lands over it. A new entry nothing reads expires after
        /// <see cref="DefaultGcTime"/>. A null <paramref name="data"/> writes nothing.
        /// </summary>
        /// <returns>The data the entry holds afterwards; default when nothing was written.</returns>
        /// <exception cref="InvalidOperationException">The entry holds data of another type.</exception>
        public T? SetQueryData<T>(QueryKey queryKey, T data)
        {
            if (queryKey == null) throw new ArgumentNullException(nameof(queryKey));
            if (data is null) return default;
            return Ensure<T>(queryKey, DefaultGcTime).SetData(data);
        }

        /// <summary>
        /// <see cref="SetQueryData{T}(QueryKey, T)"/> with the data <paramref name="updater"/> makes of the data
        /// the entry holds, default when it holds none, as TanStack Query's functional updater. An updater
        /// returning null writes nothing.
        /// </summary>
        /// <returns>The data the entry holds afterwards; default when nothing was written.</returns>
        /// <exception cref="InvalidOperationException">The entry holds data of another type.</exception>
        public T? SetQueryData<T>(QueryKey queryKey, Func<T?, T> updater)
        {
            if (updater == null) throw new ArgumentNullException(nameof(updater));
            return SetQueryData<T>(queryKey, updater(GetQueryData<T>(queryKey)));
        }

        /// <summary>
        /// Removes every entry and cancels every request in flight, as TanStack Query's <c>clear()</c>. A
        /// mounted query re-renders without data and fetches into a new entry.
        /// </summary>
        public void Clear()
        {
            var entries = SnapshotEntries();
            _entries.Clear();
            _inactive.Clear();
            _canExpire = false;
            foreach (var entry in entries)
            {
                entry.Remove();
            }
        }

        internal TimeSpan Now => _clock();

        // The entries a filter selects, among those that have not expired. A snapshot, since what runs on them
        // can remove entries.
        private QueryEntry[] SelectEntries(QueryFilters? filters)
        {
            CollectGarbage();
            var selected = new List<QueryEntry>();
            foreach (var entry in SnapshotEntries())
            {
                if (Matches(entry, filters)) selected.Add(entry);
            }
            return selected.ToArray();
        }

        // TanStack's matchQuery.
        private static bool Matches(QueryEntry entry, QueryFilters? filters)
        {
            if (filters == null) return true;
            if (filters.QueryKey != null
                && !(filters.Exact ? entry.Id.Equals(Identify(filters.QueryKey, entry.KeyHashFn)) : entry.Key.Matches(filters.QueryKey)))
            {
                return false;
            }
            if (filters.Type == QueryTypeFilter.Active && !entry.IsActive) return false;
            if (filters.Type == QueryTypeFilter.Inactive && entry.IsActive) return false;
            if (filters.Stale.HasValue && entry.IsStaleForFilter != filters.Stale.Value) return false;
            if (filters.FetchStatus.HasValue && filters.FetchStatus.Value != entry.FetchStatus) return false;
            return filters.Predicate == null || filters.Predicate(entry.Describe());
        }

        // Waits for every task, whatever it did; with throwOnError the first failure is thrown after the rest
        // have settled.
        private static VelvetTask Settle(List<VelvetTask> tasks, bool throwOnError)
            => tasks.Count == 0 ? VelvetTask.CompletedTask : SettleAsync(tasks, throwOnError);

        private static async VelvetTask SettleAsync(List<VelvetTask> tasks, bool throwOnError)
        {
            Exception? failure = null;
            foreach (var task in tasks)
            {
                try
                {
                    await task;
                }
                catch (Exception taskFailure)
                {
                    failure ??= taskFailure;
                }
            }
            if (throwOnError && failure != null) throw failure;
        }

        // The entry a fetchQuery-style call works on, with the options its request runs with and the stale time
        // its data is judged by.
        private QueryEntry<T> EntryFor<T>(QueryOptions<T, T> options, out QueryFetchOptions<T> fetchOptions, out TimeSpan staleTime)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.QueryKey == null) throw new ArgumentException("The options have no QueryKey.", nameof(options));
            if (options.QueryFn == null) throw new ArgumentException("The options have no QueryFn.", nameof(options));
            staleTime = RequireNonNegative(options.StaleTime ?? DefaultStaleTime, nameof(QueryOptions<T, T>.StaleTime));
            var gcTime = RequireNonNegative(options.GcTime ?? DefaultGcTime, nameof(QueryOptions<T, T>.GcTime));
            fetchOptions = new QueryFetchOptions<T>().Assign(options, this);
            fetchOptions.SetRetries(options.Retry ?? 0);
            return Ensure<T>(options.QueryKey, options.QueryKeyHashFn ?? DefaultQueryKeyHashFn, gcTime);
        }

        // What names an entry in the cache: the key as it is, or, under a hash function, a key of the string the
        // function made of it, which no key an application builds equals.
        internal static QueryKey Identify(QueryKey key, Func<QueryKey, string>? hashFn)
        {
            if (hashFn == null) return key;
            var hash = hashFn(key) ?? throw new InvalidOperationException($"The QueryKeyHashFn returned null for {key}.");
            return new QueryKey(new HashedKeyIdentity(hash));
        }

        private sealed class HashedKeyIdentity : IEquatable<HashedKeyIdentity>
        {
            private readonly string _hash;

            internal HashedKeyIdentity(string hash) => _hash = hash;

            public bool Equals(HashedKeyIdentity? other) => other != null && string.Equals(_hash, other._hash, StringComparison.Ordinal);

            public override bool Equals(object? obj) => Equals(obj as HashedKeyIdentity);

            public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_hash);

            public override string ToString() => _hash;
        }

        // The client watches the application's visibility and connection while a query observes it, which is
        // when v5's onFocus and onOnline can find an observer to refetch for.
        internal void ObserverAdded()
        {
            if (_observerCount++ == 0) QueryClientSignals.Watch(this);
        }

        internal void ObserverRemoved()
        {
            if (--_observerCount == 0) QueryClientSignals.Unwatch(this);
        }

        // TanStack's queryCache.onFocus and onOnline.
        internal void OnSignal(bool reconnect)
        {
            foreach (var entry in SnapshotEntries())
            {
                entry.OnSignal(reconnect);
            }
        }

        internal QueryEntry<T>? Peek<T>(QueryKey key) => PeekById<T>(Identify(key, DefaultQueryKeyHashFn));

        // An expired entry reads as absent before a sweep removes it, so a render never shows an entry that had
        // already expired and that a sibling's subscription in the same commit would sweep.
        internal QueryEntry<T>? PeekById<T>(QueryKey id)
        {
            _readEpoch++;
            return _entries.TryGetValue(id, out var entry) && !IsExpired(entry, Now) ? Typed<T>(entry) : null;
        }

        internal QueryEntry<T> Build<T>(QueryKey key, QueryKey id, Func<QueryKey, string>? hashFn, TimeSpan gcTime)
        {
            // A reader leaving in this commit's effects hands its entry to the reader arriving in them — a
            // keyed remount, two readers swapped — however short its gcTime, as v5's addObserver clears the
            // removal timeout. Taken back before the sweep, which otherwise replaces an expired entry rather
            // than handing it back.
            if (_entries.TryGetValue(id, out var existing) && existing.InactiveEpoch == _readEpoch)
            {
                MarkActive(existing);
            }
            CollectGarbage();
            if (_entries.TryGetValue(id, out existing))
            {
                var typed = Typed<T>(existing);
                typed.RaiseGcTime(gcTime);
                return typed;
            }

            var entry = new QueryEntry<T>(this, key, id, hashFn, gcTime);
            _entries.Add(id, entry);
            return entry;
        }

        // TanStack's queryCache.build outside a subscription: an entry made here has no reader yet, so it is
        // collected after its gcTime unless one subscribes, as v5 schedules a new query's removal at once.
        internal QueryEntry<T> Ensure<T>(QueryKey key, TimeSpan gcTime) => Ensure<T>(key, DefaultQueryKeyHashFn, gcTime);

        internal QueryEntry<T> Ensure<T>(QueryKey key, Func<QueryKey, string>? hashFn, TimeSpan gcTime)
        {
            var id = Identify(key, hashFn);
            CollectGarbage();
            // An entry that exists is returned as it is, gcTime included, as v5's build returns the query it finds.
            if (_entries.TryGetValue(id, out var existing)) return Typed<T>(existing);

            var entry = new QueryEntry<T>(this, key, id, hashFn, gcTime);
            _entries.Add(id, entry);
            MarkInactive(entry);
            return entry;
        }

        internal void MarkInactive(QueryEntry entry)
        {
            entry.InactiveSince = Now;
            entry.InactiveEpoch = _readEpoch;
            _inactive.Add(entry);
            LowerNextWalk(entry, entry.InactiveSince.Value);
            if (!_collecting && CanExpire(entry))
            {
                _collecting = true;
                CollectOnFrames(new WeakReference<QueryClient>(this)).Forget();
            }
        }

        internal void MarkActive(QueryEntry entry)
        {
            entry.InactiveSince = null;
            // The deadline the last walk found may belong to this entry, so the next poll walks again.
            _canExpire = false;
            _inactive.Remove(entry);
        }

        internal void CollectGarbage() => Sweep(Now);

        // The earliest clock reading at which a poll has anything to remove. It is lowered where an entry's deadline
        // can fall before the one the last walk saw, an entry going inactive or a request settling; every other
        // change moves a deadline later, and a walk that comes early finds nothing.
        internal void LowerNextWalk(QueryEntry entry, TimeSpan since)
        {
            var deadline = Deadline(entry, since);
            // MUTANT_SURVIVES(equivalent, boundary): at equal deadlines the assignment writes the value already held.
            if (deadline < _nextWalk) _nextWalk = deadline;
        }

        private static TimeSpan Deadline(QueryEntry entry, TimeSpan since)
        {
            // MUTANT_SURVIVES(equivalent, boundary): at each boundary the sum is TimeSpan.MaxValue itself or cannot overflow, so both branches give the same value.
            return since > TimeSpan.Zero && entry.GcTime >= TimeSpan.MaxValue - since ? TimeSpan.MaxValue : since + entry.GcTime;
        }

        // Removes what has expired and reports whether an entry is left that can still expire.
        private bool Sweep(TimeSpan now)
        {
            var next = TimeSpan.MaxValue;
            var canExpire = false;
            for (var i = _inactive.Count - 1; i >= 0; i--)
            {
                var entry = _inactive[i];
                if (IsExpired(entry, now))
                {
                    _inactive.RemoveAt(i);
                    _entries.Remove(entry.Id);
                    entry.Remove();
                    continue;
                }
                if (!CanExpire(entry)) continue;
                if (entry.InactiveSince is not { } since) continue;
                canExpire = true;
                // An entry with a request in flight, paused or not, has no deadline until the request settles, which
                // lowers _nextWalk.
                if (entry.FetchStatus == QueryFetchStatus.Idle)
                {
                    var deadline = Deadline(entry, since);
                    // MUTANT_SURVIVES(equivalent, boundary): at equal deadlines the assignment writes the value already held.
                    if (deadline < next) next = deadline;
                }
            }
            // MUTANT_SURVIVES(equivalent, line removed): _nextWalk then stays at or below every reading, so each poll walks the list, which finds what a skipped poll would have found later.
            _nextWalk = next;
            // MUTANT_SURVIVES(equivalent, line removed): _canExpire then stays false, so each poll walks the list, as it does without the line above.
            _canExpire = canExpire;
            return canExpire;
        }

        // One frame's poll, true while another is due. _canExpire is what the last walk found, by this loop or by a
        // lazy sweep, so a sweep that removed the last expirable entry ends the loop at its next frame. The loop holds the client weakly, so a client the
        // application dropped is not kept alive by an entry that has not expired.
        private bool Poll()
        {
            var keep = false;
            try
            {
                var now = Now;
                if (_canExpire && now < _nextWalk) keep = true;
                else keep = Sweep(now);
            }
            finally
            {
                if (!keep) _collecting = false;
            }
            return keep;
        }

        // Constraint: a client with nothing left that can expire polls nothing. TanStack's scheduleGc sets no
        // timer for an infinite gcTime, and Sweep reports whether any entry can still expire.
        private static async VelvetTask CollectOnFrames(WeakReference<QueryClient> client)
        {
            do
            {
                await VelvetTask.Yield();
            }
            while (PollOf(client));
        }

        // Kept out of the async method, whose state would hold the client in a local across the await.
        private static bool PollOf(WeakReference<QueryClient> client)
            => client.TryGetTarget(out var target) && target.Poll();

        private static bool CanExpire(QueryEntry entry) => entry.GcTime != TimeSpan.MaxValue;

        // An entry with a request in flight is not expired whatever its age, a paused one included, as v5's
        // optionalRemove keeps a query whose fetch is not idle; QueryEntry.RescheduleCollection moves its deadline
        // once the request settles.
        private static bool IsExpired(QueryEntry entry, TimeSpan now)
        {
            var since = entry.InactiveSince;
            return since != null && entry.FetchStatus == QueryFetchStatus.Idle && now - since.Value >= entry.GcTime;
        }

        // v5's isValidTimeout and its zero check: anything else sets no interval.
        internal static TimeSpan? ValidInterval(TimeSpan? requested)
        {
            var every = requested ?? TimeSpan.Zero;
            // MUTANT_SURVIVES(equivalent, clause removed): a wait of TimeSpan.MaxValue on the client's clock never ends, so it fetches nothing either.
            return every > TimeSpan.Zero && every != TimeSpan.MaxValue ? every : null;
        }

        internal static TimeSpan RequireNonNegative(TimeSpan value, string name)
            => value < TimeSpan.Zero
                ? throw new ArgumentOutOfRangeException(name, value, $"{name} must not be negative.")
                : value;

        private QueryEntry[] SnapshotEntries()
        {
            var entries = new QueryEntry[_entries.Count];
            _entries.Values.CopyTo(entries, 0);
            return entries;
        }

        private static QueryEntry<T> Typed<T>(QueryEntry entry)
            => entry as QueryEntry<T> ?? throw new InvalidOperationException(
                $"Query {entry.Key} holds {entry.DataType.Name} and was read as {typeof(T).Name}. " +
                "A key names one entry, so every query sharing it must agree on its data type.");
    }

    internal abstract class QueryEntry
    {
        protected QueryEntry(QueryClient client, QueryKey key, QueryKey id, Func<QueryKey, string>? keyHashFn, TimeSpan gcTime)
        {
            Client = client;
            Key = key;
            Id = id;
            _createdKeyHashFn = keyHashFn;
            GcTime = gcTime;
        }

        internal QueryClient Client { get; }
        internal QueryKey Key { get; }
        // What the client's cache holds the entry under: Key, or the hash a QueryKeyHashFn made of it.
        internal QueryKey Id { get; }
        // The function of the options the entry was last handed, as v5 reads query.options; the one it was created with
        // until then.
        internal virtual Func<QueryKey, string>? KeyHashFn => _createdKeyHashFn;
        private readonly Func<QueryKey, string>? _createdKeyHashFn;
        internal TimeSpan GcTime { get; private set; }
        internal TimeSpan? InactiveSince { get; set; }
        internal int InactiveEpoch { get; set; }
        internal abstract Type DataType { get; }
        internal abstract QueryFetchStatus FetchStatus { get; }
        internal bool IsFetching => FetchStatus == QueryFetchStatus.Fetching;

        // v5's fetch calls scheduleGc as it settles, which starts the removal timer over, so an entry nothing reads
        // goes a whole gcTime after its request settled. An entry something reads has no removal to start over.
        internal void RescheduleCollection(TimeSpan now)
        {
            if (InactiveSince == null) return;
            InactiveSince = now;
            Client.LowerNextWalk(this, now);
        }

        // The longest any query asked for, as TanStack's updateGcTime keeps.
        internal void RaiseGcTime(TimeSpan gcTime)
        {
            // MUTANT_SURVIVES(equivalent, boundary): at equal times the assignment writes the value already held.
            if (gcTime > GcTime) GcTime = gcTime;
        }

        internal abstract bool IsActive { get; }
        internal abstract bool IsStaleForFilter { get; }
        internal abstract VelvetTask Invalidate(bool cancelRefetch);
        internal abstract VelvetTask Refetch(bool cancelRefetch);
        internal abstract void CancelRequest(bool revert);
        internal abstract void ResetState();
        internal abstract void Remove();
        internal abstract void OnSignal(bool reconnect);
        internal abstract QueryInfo Describe();
    }

    // One cached result and the request that fills it. The request belongs to the entry rather than to the
    // component that started it, so an observer leaving takes nothing from the observers that stay.
    internal sealed class QueryEntry<T> : QueryEntry
    {
        private readonly List<QueryEntryObserver<T>> _observers = new();
        private CancellationTokenSource? _inFlight;
        private bool _retriesStopped;
        private bool _paused;
        private TimeSpan _dataUpdatedAt;
        // TanStack's query.options: what the last query to commit over the entry, or to start a request for it,
        // handed it. An invalidation's refetch runs with them, whichever observer they came from.
        private QueryFetchOptions<T>? _options;
        // TanStack's #revertState: the state a request found the entry in, which cancelling it with revert puts back.
        private EntryState? _revertState;
        // Whether the request in flight runs a query function that takes the token, which v5 tells by the function
        // reading its signal.
        private bool _requestTakesToken;
        // The callers waiting on the request in flight, each settled when the request lands, fails or is cancelled.
        private List<VelvetTaskCompletionSource<T>>? _waiters;

        private readonly struct EntryState
        {
            internal EntryState(QueryEntry<T> entry)
            {
                Status = entry.Status;
                HasData = entry.HasData;
                Data = entry.Data;
                Error = entry.Error;
                FailureCount = entry.FailureCount;
                FailureReason = entry.FailureReason;
                IsInvalidated = entry.IsInvalidated;
                DataUpdatedAt = entry._dataUpdatedAt;
            }

            internal QueryStatus Status { get; }
            internal bool HasData { get; }
            internal T Data { get; }
            internal Exception? Error { get; }
            internal int FailureCount { get; }
            internal Exception? FailureReason { get; }
            internal bool IsInvalidated { get; }
            internal TimeSpan DataUpdatedAt { get; }
        }

        internal QueryEntry(QueryClient client, QueryKey key, QueryKey id, Func<QueryKey, string>? keyHashFn, TimeSpan gcTime)
            : base(client, key, id, keyHashFn, gcTime)
        {
        }

        internal override Func<QueryKey, string>? KeyHashFn => _options?.KeyHashFn ?? base.KeyHashFn;
        internal override Type DataType => typeof(T);
        internal QueryStatus Status { get; private set; } = QueryStatus.Pending;
        internal bool HasData { get; private set; }
        internal T Data { get; private set; } = default!;
        internal Exception? Error { get; private set; }
        internal int FailureCount { get; private set; }
        internal Exception? FailureReason { get; private set; }
        internal bool IsInvalidated { get; private set; }
        internal bool IsRemoved { get; private set; }
        // TanStack's fetchStatus: a request in flight is paused while it waits for the connection or the
        // application's visibility, and fetching otherwise.
        internal override QueryFetchStatus FetchStatus
            => _inFlight == null ? QueryFetchStatus.Idle : _paused ? QueryFetchStatus.Paused : QueryFetchStatus.Fetching;

        internal TimeSpan DataUpdatedAt => _dataUpdatedAt;

        internal bool IsStaleFor(TimeSpan staleTime)
            => !HasData || IsInvalidated || Client.Now - _dataUpdatedAt >= staleTime;

        internal void SetOptions(QueryFetchOptions<T> options) => _options = options;

        internal void Subscribe(QueryEntryObserver<T> observer)
        {
            _observers.Add(observer);
            Client.ObserverAdded();
            if (_observers.Count == 1) Client.MarkActive(this);
            if (observer.Enabled && IsStaleFor(observer.StaleTime))
            {
                Fetch(observer.FetchOptions, cancelRefetch: false);
            }
        }

        // With the last observer gone, a request whose query function takes the token is cancelled and the entry
        // put back as it stood before the request, as TanStack's removeObserver cancels with revert a request whose
        // function read its signal, and one that is paused with no data. Any other request finishes but is not
        // retried, where removeObserver calls cancelRetry.
        internal void Unsubscribe(QueryEntryObserver<T> observer)
        {
            // The options this observer handed are rewritten in place at its next commit, where v5's query keeps
            // a copy of what it was handed.
            if (ReferenceEquals(_options, observer.FetchOptions)) _options = observer.FetchOptions.Copy();
            if (!_observers.Remove(observer)) return;
            Client.ObserverRemoved();
            if (_observers.Count > 0 || IsRemoved) return;
            if (_inFlight != null && (_requestTakesToken || (_paused && Status == QueryStatus.Pending)))
            {
                CancelInFlight();
                Revert();
            }
            else
            {
                _retriesStopped = true;
            }
            Client.MarkInactive(this);
        }

        // Fetch, handing back a task of the request now in flight: the one this started, or the one it joined.
        internal VelvetTask<T> FetchAndWait(QueryFetchOptions<T> options, bool cancelRefetch)
        {
            Fetch(options, cancelRefetch);
            if (_inFlight == null) return VelvetTask.FromException<T>(new OperationCanceledException());
            var waiter = new VelvetTaskCompletionSource<T>();
            (_waiters ??= new List<VelvetTaskCompletionSource<T>>()).Add(waiter);
            return waiter.Task;
        }

        private void ResolveWaiters(T data)
        {
            var waiters = _waiters;
            if (waiters == null) return;
            _waiters = null;
            foreach (var waiter in waiters)
            {
                waiter.TrySetResult(data);
            }
        }

        private void RejectWaiters(Exception error)
        {
            var waiters = _waiters;
            if (waiters == null) return;
            _waiters = null;
            foreach (var waiter in waiters)
            {
                waiter.TrySetException(error);
            }
        }

        private void CancelWaiters()
        {
            var waiters = _waiters;
            if (waiters == null) return;
            _waiters = null;
            foreach (var waiter in waiters)
            {
                waiter.TrySetCanceled();
            }
        }

        // cancelRefetch is TanStack's fetch option of the same name: an explicit refetch over an entry that
        // holds data starts over, and every other request joins the one in flight, which retries again if
        // its retries had been stopped.
        internal void Fetch(QueryFetchOptions<T> options, bool cancelRefetch)
        {
            if (IsRemoved) return;
            if (_inFlight != null)
            {
                if (!cancelRefetch || !HasData)
                {
                    _retriesStopped = false;
                    return;
                }
                CancelInFlight();
            }
            // v5's fetch takes the options it is handed only once it is not joining a request in flight.
            _options = options;

            // Taken after a request started over is cancelled, as v5 stores the state at that point.
            _revertState = new EntryState(this);
            _requestTakesToken = options.TakesToken;
            var request = new CancellationTokenSource();
            _inFlight = request;
            _retriesStopped = false;
            // TanStack's fetchState: a request that cannot start yet is paused from the first notification.
            _paused = !QueryClientSignals.CanFetch(options.NetworkMode);
            FailureCount = 0;
            FailureReason = null;
            if (!HasData)
            {
                Status = QueryStatus.Pending;
                Error = null;
            }

            // An observer's snapshot can throw (a placeholder function), and the request marked in flight above
            // must still run, or later fetches would join one that does not exist.
            try
            {
                Notify();
            }
            finally
            {
                RunAsync(options, request).Forget();
            }
        }

        private async VelvetTask RunAsync(QueryFetchOptions<T> options, CancellationTokenSource request)
        {
            var queryFn = options.QueryFn;
            var maxRetries = options.MaxRetries;
            var retryDelay = options.RetryDelay;
            var sharing = options.StructuralSharing;
            var networkMode = options.NetworkMode;
            if (_paused)
            {
                await PauseAsync(request, networkMode, start: true);
                if (request.IsCancellationRequested) return;
            }
            for (var failures = 0; ; failures++)
            {
                T data = default!;
                Exception? failure = null;
                var token = request.Token;
                var task = Start(queryFn, token);
                try
                {
                    // A task that has already completed is held back one frame, so that readers subscribing in
                    // the commit that started this request join it rather than finding its result, as v5's
                    // result arrives a microtask later. UseQueryHookTests pins it with two readers mounting
                    // together.
                    if (task.Status.IsCompleted()) await VelvetTask.Yield();
                    data = await task.AttachExternalCancellation(token);
                }
                // Same gate as FiberAsyncResource.AwaitAsync: a cancellation this entry asked for records
                // nothing, and one raised by a token the query function owns is an error.
                catch (OperationCanceledException) when (request.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                if (request.IsCancellationRequested) return;
                if (failure == null)
                {
                    // v5 shares a landing with the query's options as they stand when it settles; the request's own
                    // are used only where the entry holds none.
                    Succeed(request, data, _options != null ? _options.StructuralSharing : sharing, failures);
                    return;
                }

                if (failures >= maxRetries || _retriesStopped)
                {
                    Fail(request, failure, failures);
                    return;
                }

                TimeSpan delay;
                try
                {
                    delay = retryDelay(failures, failure);
                }
                catch (Exception delayFailure)
                {
                    Fail(request, delayFailure, failures);
                    return;
                }

                RecordFailure(request, failures + 1, failure);
                await WaitFor(delay, request);
                if (request.IsCancellationRequested) return;
                if (!QueryClientSignals.CanContinue(networkMode))
                {
                    await PauseAsync(request, networkMode, start: false);
                    if (request.IsCancellationRequested) return;
                }
                if (_retriesStopped)
                {
                    Fail(request, failure, failures);
                    return;
                }
            }
        }

        // A query function that throws before returning a task fails the request as one returning a faulted
        // task does.
        private static VelvetTask<T> Start(Func<CancellationToken, VelvetTask<T>> queryFn, CancellationToken token)
        {
            try
            {
                return queryFn(token);
            }
            catch (Exception ex)
            {
                return VelvetTask.FromException<T>(ex);
            }
        }

        // TanStack's retryer pause: the request waits, polled once a frame like a retry's wait, until the
        // reading its network mode asks for allows it, which for the first request is the connection alone and for
        // a retry the application's visibility as well.
        private async VelvetTask PauseAsync(CancellationTokenSource request, NetworkMode networkMode, bool start)
        {
            SetPaused(request, true);
            do
            {
                await VelvetTask.Yield();
            }
            while (!request.IsCancellationRequested
                && !(start ? QueryClientSignals.CanFetch(networkMode) : QueryClientSignals.CanContinue(networkMode)));
            if (!request.IsCancellationRequested) SetPaused(request, false);
        }

        private void SetPaused(CancellationTokenSource request, bool paused)
        {
            if (!ReferenceEquals(_inFlight, request) || _paused == paused) return;
            _paused = paused;
            Notify();
        }

        // At least one frame, as v5 sleeps through a timer even for a zero delay. With the retries stopped the
        // wait ends early, since its only outcome is the failure.
        private async VelvetTask WaitFor(TimeSpan delay, CancellationTokenSource request)
        {
            var start = Client.Now;
            do
            {
                await VelvetTask.Yield();
            }
            while (!request.IsCancellationRequested && !_retriesStopped && Client.Now - start < delay);
        }

        private void Succeed(CancellationTokenSource request, T data, Func<object?, object, object>? sharing, int failures)
        {
            // A request stops being the one in flight when something cancels it or removes the entry, and a
            // completion already posted to the main thread by then still arrives here.
            if (!ReferenceEquals(_inFlight, request)) return;
            T shared;
            try
            {
                shared = Share(sharing, data);
            }
            catch (Exception ex)
            {
                Fail(request, ex, failures);
                return;
            }

            End(request);
            Data = shared;
            HasData = true;
            Error = null;
            Status = QueryStatus.Success;
            IsInvalidated = false;
            FailureCount = 0;
            FailureReason = null;
            _dataUpdatedAt = Client.Now;
            try
            {
                Notify();
            }
            finally
            {
                ResolveWaiters(shared);
            }
        }

        // TanStack's setData with manual set, as setQueryData calls it: the data lands as a request's would, but a
        // request in flight keeps running and its failure count stays.
        internal T SetData(T data)
        {
            // v5's setData shares with the query's own options, those the entry was last handed.
            var shared = Share(_options?.StructuralSharing, data);
            Data = shared;
            HasData = true;
            Error = null;
            Status = QueryStatus.Success;
            IsInvalidated = false;
            _dataUpdatedAt = Client.Now;
            Notify();
            return shared;
        }

        private T Share(Func<object?, object, object>? sharing, T data)
        {
            if (sharing == null) return QueryStructuralSharing.Replace(HasData ? Data : default, data);
            return (T)sharing(HasData ? (object?)Data : null, data!);
        }

        private void Fail(CancellationTokenSource request, Exception error, int failures)
        {
            if (!ReferenceEquals(_inFlight, request)) return;
            End(request);
            // The last good data stays beside the error, as TanStack keeps it, and stale: TanStack flags it
            // invalidated so that the next reader fetches again.
            Error = error;
            Status = QueryStatus.Error;
            IsInvalidated = true;
            FailureCount = failures + 1;
            FailureReason = error;
            try
            {
                Notify();
            }
            finally
            {
                RejectWaiters(error);
            }
        }

        private void RecordFailure(CancellationTokenSource request, int failureCount, Exception error)
        {
            if (!ReferenceEquals(_inFlight, request)) return;
            FailureCount = failureCount;
            FailureReason = error;
            Notify();
        }

        private void End(CancellationTokenSource request)
        {
            _inFlight = null;
            _paused = false;
            RescheduleCollection(Client.Now);
            // MUTANT_SURVIVES(equivalent, line removed): nothing cancels a settled request again, so its source only waits for collection.
            request.Dispose();
        }

        // Only an entry an enabled query reads is fetched, as v5 refetches the active queries.
        internal override VelvetTask Invalidate(bool cancelRefetch)
        {
            IsInvalidated = true;
            foreach (var observer in _observers)
            {
                if (!observer.Enabled) continue;
                // An observer hands the entry its options when it subscribes, so an entry with one holds some.
                return FetchAndWait(_options!, cancelRefetch);
            }
            return VelvetTask.CompletedTask;
        }

        internal override bool IsActive
        {
            get
            {
                foreach (var observer in _observers)
                {
                    if (observer.Enabled) return true;
                }
                return false;
            }
        }

        // TanStack's query.isStale(): a mounted query's own reading of it, else whether the entry lacks data or
        // was invalidated.
        internal override bool IsStaleForFilter
        {
            get
            {
                if (_observers.Count == 0) return !HasData || IsInvalidated;
                foreach (var observer in _observers)
                {
                    if (observer.Enabled && IsStaleFor(observer.StaleTime)) return true;
                }
                return false;
            }
        }

        // TanStack's isDisabled: with readers, whether none of them is enabled; without, whether the entry was
        // never fetched. An entry written by hand holds no options to fetch with and is left alone too.
        internal override VelvetTask Refetch(bool cancelRefetch)
        {
            var fetchable = _observers.Count > 0 ? IsActive : _options != null && (HasData || Error != null);
            return fetchable ? FetchAndWait(_options!, cancelRefetch) : VelvetTask.CompletedTask;
        }

        // TanStack's query.cancel({ revert }): the request stops and its token is cancelled. Reverting puts the
        // entry back as the request found it; otherwise the cancellation is the entry's error.
        internal override void CancelRequest(bool revert)
        {
            if (_inFlight == null) return;
            CancelInFlight();
            if (revert)
            {
                Revert();
            }
            else
            {
                var error = new OperationCanceledException();
                Error = error;
                Status = QueryStatus.Error;
                IsInvalidated = true;
                FailureCount++;
                FailureReason = error;
            }
            RescheduleCollection(Client.Now);
            Notify();
        }

        // TanStack's query.reset(): the state of an entry never fetched, its request cancelled without a word.
        internal override void ResetState()
        {
            CancelInFlight();
            Status = QueryStatus.Pending;
            HasData = false;
            Error = null;
            FailureCount = 0;
            FailureReason = null;
            IsInvalidated = false;
            Notify();
        }

        // TanStack's query.onFocus and onOnline: the first observer that asks for it refetches, joining a request
        // already in flight.
        internal override void OnSignal(bool reconnect)
        {
            foreach (var observer in _observers)
            {
                var rule = reconnect ? observer.RefetchOnReconnect : observer.RefetchOnWindowFocus;
                if (!observer.ShouldFetchOn(rule, this)) continue;
                Fetch(observer.FetchOptions, cancelRefetch: false);
                return;
            }
        }

        internal override void Remove()
        {
            IsRemoved = true;
            CancelInFlight();
            foreach (var observer in _observers.ToArray())
            {
                observer.OnRemoved();
            }
        }

        internal override QueryInfo Describe()
            => new(Key, Status, HasData ? (object?)Data : null, Error, FetchStatus);

        private void Revert()
        {
            if (_revertState is not { } saved) return;
            Status = saved.Status;
            HasData = saved.HasData;
            Data = saved.Data;
            Error = saved.Error;
            FailureCount = saved.FailureCount;
            FailureReason = saved.FailureReason;
            IsInvalidated = saved.IsInvalidated;
            _dataUpdatedAt = saved.DataUpdatedAt;
        }

        private void CancelInFlight()
        {
            var cts = _inFlight;
            if (cts == null) return;
            _inFlight = null;
            _paused = false;
            // Contained on FiberAsyncResource.Dispose's terms: the token went to the query function, so a
            // callback firing here can be the application's.
            try
            {
                cts.Cancel();
            }
            catch (Exception cancellationFailure)
            {
                FiberLogger.LogException(nameof(QueryClient), cancellationFailure);
            }
            // MUTANT_SURVIVES(equivalent, line removed): the source is cancelled already, and RunAsync's catch reads only IsCancellationRequested, which disposal leaves alone.
            cts.Dispose();
            CancelWaiters();
        }

        private void Notify()
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnQueryUpdate();
            }
        }
    }

    // TanStack's focusManager and onlineManager, as the clients observing queries subscribe to them: the readings
    // NetworkSignals takes are polled once a frame while a client watches, and a reading turning true since the
    // client's last one is the event. Each client keeps its own last reading, taken first when it starts
    // watching: a change while it was not watching is not one it is told of.
    internal static class QueryClientSignals
    {
        private sealed class Watcher
        {
            internal Watcher(QueryClient client, bool visible, bool online)
            {
                Client = client;
                Visible = visible;
                Online = online;
            }

            internal QueryClient Client { get; }
            internal bool Visible { get; set; }
            internal bool Online { get; set; }
        }

        private static readonly List<Watcher> s_watchers = new();
        // The watchers as a poll found them, refilled each frame rather than copied anew.
        private static readonly List<Watcher> s_polled = new();
        private static readonly Func<bool> s_readVisible = NetworkSignals.ReadVisible;
        private static readonly Func<bool> s_readOnline = NetworkSignals.ReadOnline;
        private static bool s_polling;
        // Whether a failing reading has been logged, so one that fails every frame logs once.
        private static bool s_visibleFailureLogged;
        private static bool s_onlineFailureLogged;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_watchers.Clear();
            // MUTANT_SURVIVES(equivalent, line removed): a poll refills the list from the watchers before it reads it.
            s_polled.Clear();
            s_polling = false;
            s_visibleFailureLogged = false;
            s_onlineFailureLogged = false;
            NetworkSignals.IsVisible = null;
            NetworkSignals.IsOnline = null;
        }

        internal static void Watch(QueryClient client)
        {
            s_watchers.Add(new Watcher(
                client, IsVisible(), TryRead(s_readOnline, ref s_onlineFailureLogged) ?? true));
            // MUTANT_SURVIVES(equivalent, guard removed): a second poll runs after the first has stored the frame's readings in every watcher, so it finds no change to report.
            if (s_polling) return;
            s_polling = true;
            Poll().Forget();
        }

        // TanStack's canFetch: only Online waits for the connection. A reading that fails is taken as online, as
        // Watch takes it.
        internal static bool CanFetch(NetworkMode mode)
            => mode != NetworkMode.Online || (TryRead(s_readOnline, ref s_onlineFailureLogged) ?? true);

        // The retryer's canContinue: a retry waits for the application to be visible, and for the connection
        // unless the mode is Always.
        internal static bool CanContinue(NetworkMode mode)
            => IsVisible() && (mode == NetworkMode.Always || (TryRead(s_readOnline, ref s_onlineFailureLogged) ?? true));

        // TanStack's focusManager.isFocused(), a visibility reading, which a refetch interval asks at each tick.
        internal static bool IsVisible() => TryRead(s_readVisible, ref s_visibleFailureLogged) ?? true;

        internal static void Unwatch(QueryClient client)
        {
            // MUTANT_SURVIVES(equivalent, boundary): a client is unwatched only after it was watched, so the loop returns before the bound.
            for (var i = 0; i < s_watchers.Count; i++)
            {
                if (!ReferenceEquals(s_watchers[i].Client, client)) continue;
                s_watchers.RemoveAt(i);
                return;
            }
        }

        private static async VelvetTask Poll()
        {
            try
            {
                while (s_watchers.Count > 0)
                {
                    await VelvetTask.Yield();
                    var visible = TryRead(s_readVisible, ref s_visibleFailureLogged);
                    var online = TryRead(s_readOnline, ref s_onlineFailureLogged);
                    // MUTANT_SURVIVES(equivalent, line removed): a watcher visited again finds the readings its first visit stored, so it reports no change, and a client told twice in a frame joins the request the first signal started.
                    s_polled.Clear();
                    s_polled.AddRange(s_watchers);
                    foreach (var watcher in s_polled)
                    {
                        var focused = visible == true && !watcher.Visible;
                        var reconnected = online == true && !watcher.Online;
                        if (visible is { } nowVisible) watcher.Visible = nowVisible;
                        if (online is { } nowOnline) watcher.Online = nowOnline;
                        if (focused) Tell(watcher.Client, reconnect: false);
                        if (reconnected) Tell(watcher.Client, reconnect: true);
                    }
                }
            }
            finally
            {
                s_polling = false;
            }
        }

        // A signal's refetch builds a snapshot, which calls the application's placeholder function; a throw from
        // it must neither end the poll nor keep the other clients from being told.
        private static void Tell(QueryClient client, bool reconnect)
        {
            try
            {
                client.OnSignal(reconnect);
            }
            catch (Exception signalFailure)
            {
                FiberLogger.LogException(nameof(QueryClient), signalFailure);
            }
        }

        // A reading an override throws from is taken as no reading, so one failure does not end the polling for
        // every client, and only its first failure is logged.
        private static bool? TryRead(Func<bool> read, ref bool failureLogged)
        {
            try
            {
                return read();
            }
            catch (Exception readFailure)
            {
                if (!failureLogged) FiberLogger.LogException(nameof(NetworkSignals), readFailure);
                failureLogged = true;
                return null;
            }
        }
    }
}
