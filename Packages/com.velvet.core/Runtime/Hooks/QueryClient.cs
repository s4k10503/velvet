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
        /// <see cref="TimeSpan.MaxValue"/> keeps a result fresh until it is invalidated, and
        /// <see cref="QueryStaleTime.Static"/> keeps it fresh for good.
        /// </summary>
        public QueryStaleTime StaleTime { get; init; } = TimeSpan.Zero;

        /// <summary>
        /// <see cref="StaleTime"/> given as a function of the entry a query reads, for every query that sets
        /// neither form of its own; see <see cref="QueryOptions{TQueryFnData, TData}.StaleTimeFn"/>. It replaces
        /// <see cref="StaleTime"/> when both are set.
        /// </summary>
        public Func<Query, QueryStaleTime>? StaleTimeFn { get; init; }

        /// <summary>
        /// How long an entry nothing observes is kept; after that the client removes it, as the remarks on
        /// <see cref="QueryClient"/> say. TanStack Query's default is five minutes.
        /// <see cref="TimeSpan.MaxValue"/> keeps it until <see cref="QueryClient.Clear"/>.
        /// </summary>
        public TimeSpan GcTime { get; init; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Which failed requests run again before the failure is the entry's error, for every query that does not
        /// set its own: TanStack Query's <c>retry</c>, in the forms
        /// <see cref="QueryOptions{TQueryFnData, TData}.Retry"/> takes. When null, a mounted query retries three
        /// times and <see cref="QueryClient.FetchQuery{T}"/> not at all, as v5 treats a retry nobody set.
        /// </summary>
        public RetryRule? Retry { get; init; }

        /// <summary>
        /// The wait before the next request, from the number of failures before this one and this failure's
        /// exception: TanStack Query's <c>retryDelay</c>. When null, one second doubled at each failure and
        /// capped at thirty seconds, TanStack Query's default. The wait is measured on <see cref="Clock"/>
        /// and checked once a frame, and lasts at least one frame.
        /// </summary>
        public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

        /// <summary>
        /// Whether a query that sets neither form of its own throws its error to the nearest error boundary:
        /// TanStack Query's <c>throwOnError</c> in <c>defaultOptions.queries</c>, whose default is false; see
        /// <see cref="QueryOptions{TQueryFnData, TData}.ThrowOnError"/>.
        /// </summary>
        public bool ThrowOnError { get; init; }

        /// <summary>
        /// <see cref="ThrowOnError"/> given as a function of the error and the entry, with the terms of
        /// <see cref="QueryOptions{TQueryFnData, TData}.ThrowOnErrorFn"/>. It replaces <see cref="ThrowOnError"/>
        /// when both are set.
        /// </summary>
        public Func<Exception, Query, bool>? ThrowOnErrorFn { get; init; }

        /// <summary>
        /// Whether a query mounting over an entry that holds data fetches it again: TanStack Query's
        /// <c>refetchOnMount</c>, whose default refetches stale data.
        /// </summary>
        public QueryRefetchMode RefetchOnMount { get; init; } = QueryRefetchMode.IfStale;

        /// <summary>
        /// <see cref="RefetchOnMount"/> given as a function of the entry a query reads, with the terms of
        /// <see cref="QueryOptions{TQueryFnData, TData}.RefetchOnMountFn"/>.
        /// </summary>
        public Func<Query, QueryRefetchMode>? RefetchOnMountFn { get; init; }

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
        public Func<Query, QueryRefetchMode>? RefetchOnWindowFocusFn { get; init; }

        /// <summary>
        /// Whether a query fetches again when the device comes back online, as <see cref="NetworkSignals"/>
        /// reads it: TanStack Query's <c>refetchOnReconnect</c>, whose default refetches stale data.
        /// </summary>
        public QueryRefetchMode RefetchOnReconnect { get; init; } = QueryRefetchMode.IfStale;

        /// <summary>
        /// <see cref="RefetchOnReconnect"/> given as a function of the entry a query reads, with the terms of
        /// <see cref="RefetchOnWindowFocusFn"/>.
        /// </summary>
        public Func<Query, QueryRefetchMode>? RefetchOnReconnectFn { get; init; }

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

        /// <summary>
        /// The cache the client's entries are kept in, TanStack Query's <c>queryCache</c> option; null makes a new
        /// one.
        /// </summary>
        public QueryCache? QueryCache { get; init; }

        /// <summary>
        /// The cache the client's mutations are kept in, TanStack Query's <c>mutationCache</c> option; null makes
        /// a new one.
        /// </summary>
        public MutationCache? MutationCache { get; init; }

        /// <summary>
        /// The options every mutation of the client starts from, TanStack Query's <c>defaultOptions.mutations</c>:
        /// what a mutation's own options and the defaults <see cref="QueryClient.SetMutationDefaults"/> registered
        /// for its key leave null is taken from here. A <see cref="MutationDefaults"/> fills the options every record
        /// shares; a typed options record also supplies its function and callbacks to options of its own type.
        /// </summary>
        public MutationOptionsBase? Mutations { get; init; }
    }

    /// <summary>
    /// Provides a <see cref="QueryClient"/> to the <see cref="Hooks.UseQuery{TQueryFnData, TData}"/>,
    /// <see cref="Hooks.UseMutation{TVariables, TData}"/> and <see cref="Hooks.UseMutationState{TResult}"/> calls below it:
    /// <c>V.Provider(QueryClientContext.Ref, value: client, children: ...)</c>, TanStack Query's
    /// <c>QueryClientProvider</c>. Read it with <c>Hooks.UseContext(QueryClientContext.Ref)</c> to reach the
    /// client from a component, as <c>useQueryClient()</c> does.
    /// </summary>
    public static class QueryClientContext
    {
        /// <summary>
        /// The context the query and mutation hooks read. Its default is null, so a hook with no Provider above it and
        /// no client passed to it throws rather than sharing a hidden one.
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
        private readonly QueryCache _queryCache;
        private readonly List<QueryEntry> _inactive = new();
        private static readonly Func<int, Exception, TimeSpan> s_exponentialBackoff
            = (failureCount, _) => TimeSpan.FromMilliseconds(Math.Min(1000d * Math.Pow(2d, failureCount), 30000d));

        private readonly Func<TimeSpan> _clock;
        private readonly MutationCache _mutationCache;
        private readonly MutationOptionsBase? _mutationOptions;
        // In registration order, a key registered again keeping its place, as v5's Map does.
        private readonly List<KeyValuePair<QueryKey, MutationOptionsBase>> _mutationDefaults = new();
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
            DefaultStaleTime = QueryStaleRule.Validate(options.StaleTime);
            DefaultStaleTimeFn = options.StaleTimeFn;
            DefaultGcTime = RequireNonNegative(options.GcTime, nameof(QueryClientOptions.GcTime));
            DefaultRetry = options.Retry;
            DefaultRefetchOnMount = options.RefetchOnMount;
            DefaultRefetchOnMountFn = options.RefetchOnMountFn;
            DefaultThrowOnError = options.ThrowOnError;
            DefaultThrowOnErrorFn = options.ThrowOnErrorFn;
            DefaultRetryDelay = options.RetryDelay ?? s_exponentialBackoff;
            DefaultNetworkMode = options.NetworkMode;
            DefaultQueryKeyHashFn = options.QueryKeyHashFn;
            DefaultRefetchOnWindowFocus = options.RefetchOnWindowFocus;
            DefaultRefetchOnReconnect = options.RefetchOnReconnect;
            DefaultRefetchOnWindowFocusFn = options.RefetchOnWindowFocusFn;
            DefaultRefetchOnReconnectFn = options.RefetchOnReconnectFn;
            _queryCache = options.QueryCache ?? new QueryCache();
            _mutationCache = options.MutationCache ?? new MutationCache();
            _mutationOptions = options.Mutations;
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
        public QueryStaleTime DefaultStaleTime { get; }

        /// <summary>The function a query that sets no stale time of its own reads its stale time from; it replaces
        /// <see cref="DefaultStaleTime"/>.</summary>
        public Func<Query, QueryStaleTime>? DefaultStaleTimeFn { get; }

        /// <summary>The garbage-collection time a query that sets none of its own uses.</summary>
        public TimeSpan DefaultGcTime { get; }

        /// <summary>The retry rule a query that sets none of its own uses; null when none was set, which
        /// <see cref="QueryClientOptions.Retry"/> describes.</summary>
        public RetryRule? DefaultRetry { get; }

        /// <summary>Whether a query that sets neither form of its own throws its error to the nearest error
        /// boundary.</summary>
        public bool DefaultThrowOnError { get; }

        /// <summary>The function a query that sets neither form of its own decides whether to throw its error
        /// with; it replaces <see cref="DefaultThrowOnError"/>.</summary>
        public Func<Exception, Query, bool>? DefaultThrowOnErrorFn { get; }

        /// <summary>Whether a query that sets none of its own fetches again when it mounts over data.</summary>
        public QueryRefetchMode DefaultRefetchOnMount { get; }

        /// <summary>The function a query that sets none of its own decides whether to fetch again on mounting
        /// over data with; it replaces <see cref="DefaultRefetchOnMount"/>.</summary>
        public Func<Query, QueryRefetchMode>? DefaultRefetchOnMountFn { get; }

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
        public Func<Query, QueryRefetchMode>? DefaultRefetchOnWindowFocusFn { get; }

        /// <summary>The function a query that sets none of its own decides whether to fetch again when the
        /// device comes back online with; it replaces <see cref="DefaultRefetchOnReconnect"/>.</summary>
        public Func<Query, QueryRefetchMode>? DefaultRefetchOnReconnectFn { get; }

        /// <summary>
        /// Marks every entry whose key <paramref name="queryKey"/> matches stale, and fetches again each
        /// one a mounted query is reading, unless a reader holds its data <see cref="QueryStaleTime.Static"/>,
        /// which no invalidation makes stale — TanStack Query's <c>invalidateQueries({ queryKey })</c>, with the
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
        /// and, for an entry nothing reads, those that were fetched before and keep the options of that fetch;
        /// an entry holding data that a reader's stale time makes <see cref="QueryStaleTime.Static"/> is left
        /// alone. TanStack Query's <c>refetchQueries</c>.
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
                RemoveEntry(entry);
            }
        }

        /// <summary>
        /// Puts the entries <paramref name="filters"/> selects back to the state of one never fetched, with no
        /// data and no error, or with the initial data it took
        /// (<see cref="QueryOptions{TQueryFnData, TData}.InitialData"/>), cancelling their requests, and then
        /// fetches those an enabled query reads —
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
        /// time, retries, retry delay, initial data and structural sharing. Unlike a mounted query, a request
        /// retries nothing unless <see cref="QueryOptions{TQueryFnData, TData}.Retry"/> or
        /// <see cref="QueryClientOptions.Retry"/> says so, as v5's <c>fetchQuery</c> sets <c>retry: false</c>
        /// where neither is set. A <see cref="QueryOptions{TQueryFnData, TData}.StaleTimeFn"/> that throws throws
        /// from this call, as v5's <c>fetchQuery</c> does.</param>
        /// <returns>A task of the entry's data; it faults with the request's failure, and is cancelled when
        /// something cancels the request.</returns>
        /// <exception cref="InvalidOperationException">The entry holds data of another type.</exception>
        public VelvetTask<T> FetchQuery<T>(QueryOptions<T, T> options)
        {
            var entry = EntryFor(options, out var fetchOptions, out var staleTime);
            if (!entry.IsStaleFor(staleTime.Read(entry, rethrow: true))) return VelvetTask.FromResult(entry.Data);
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
        /// <see cref="FetchQuery{T}"/> for an infinite query, TanStack Query's <c>fetchInfiniteQuery</c>: the pages the
        /// entry holds when they are fresh for the query's stale time, and otherwise the pages a request fetches from
        /// the first, as many as <see cref="InfiniteQueryOptions{TQueryFnData, TPageParam, TData}.Pages"/> asks or, by
        /// default, as the entry holds, at least one.
        /// </summary>
        /// <returns>A task of the entry's pages; it faults with the request's failure.</returns>
        public VelvetTask<InfiniteData<TQueryFnData, TPageParam>> FetchInfiniteQuery<TQueryFnData, TPageParam>(
            InfiniteQueryOptions<TQueryFnData, TPageParam> options)
        {
            var entry = EntryFor(options, out var fetchOptions, out var staleTime);
            // v5's fetchInfiniteQuery sets the behavior with the options' pages, and the request keeps it.
            fetchOptions.SetBehavior(((IInfiniteQueryOptions<InfiniteData<TQueryFnData, TPageParam>>)options).CreateBehavior(options.Pages));
            if (!entry.IsStaleFor(staleTime.Read(entry, rethrow: true))) return VelvetTask.FromResult(entry.Data);
            return entry.FetchAndWait(fetchOptions, cancelRefetch: false);
        }

        /// <summary>
        /// <see cref="FetchInfiniteQuery{TQueryFnData, TPageParam}"/> without the pages, TanStack Query's
        /// <c>prefetchInfiniteQuery</c>.
        /// </summary>
        /// <returns>A task that completes once the request has settled, whatever it did.</returns>
        public VelvetTask PrefetchInfiniteQuery<TQueryFnData, TPageParam>(InfiniteQueryOptions<TQueryFnData, TPageParam> options)
        {
            var request = FetchInfiniteQuery(options);
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
            if (revalidateIfStale && entry.IsStaleFor(staleTime.Read(entry, rethrow: true))) PrefetchQuery(options).Forget();
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
            if (!_queryCache.TryGet(Identify(queryKey, DefaultQueryKeyHashFn), out var found) || IsExpired(found, Now)) return null;
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
            var entry = _queryCache.TryGet(Identify(queryKey, DefaultQueryKeyHashFn), out var found) && !IsExpired(found, Now)
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
        /// Removes every entry and cancels every request in flight, as TanStack Query's <c>clear()</c>, which
        /// clears <see cref="GetQueryCache"/>. A mounted query re-renders without data and fetches into a new
        /// entry. The mutations in <see cref="GetMutationCache"/> are removed too; one still running runs on.
        /// </summary>
        public void Clear()
        {
            _queryCache.Clear();
            _mutationCache.Clear();
        }

        /// <summary>The cache the client's entries are kept in, TanStack Query's <c>getQueryCache()</c>.</summary>
        public QueryCache GetQueryCache() => _queryCache;

        /// <summary>
        /// How many of the entries <paramref name="filters"/> selects have a request in flight and running, TanStack
        /// Query's <c>isFetching</c>; the filters' own <see cref="QueryFilters.FetchStatus"/> is replaced.
        /// </summary>
        public int IsFetching(QueryFilters? filters = null)
            => _queryCache.FindAll((filters ?? new QueryFilters()) with { FetchStatus = QueryFetchStatus.Fetching }).Count;

        /// <summary>The cache the client's mutations are kept in, TanStack Query's <c>getMutationCache()</c>.</summary>
        public MutationCache GetMutationCache() => _mutationCache;

        /// <summary>
        /// How many of the mutations <paramref name="filters"/> selects are pending, TanStack Query's
        /// <c>isMutating</c>; the filters' own <see cref="MutationFilters.Status"/> is replaced.
        /// </summary>
        public int IsMutating(MutationFilters? filters = null)
            => _mutationCache.FindAll((filters ?? new MutationFilters()) with { Status = MutationStatus.Pending }).Count;

        /// <summary>
        /// Registers <paramref name="options"/> as the defaults of every mutation whose key
        /// <paramref name="mutationKey"/> leads, TanStack Query's <c>setMutationDefaults</c>. Registering a key
        /// again replaces its defaults where they stood in the order.
        /// </summary>
        public void SetMutationDefaults(QueryKey mutationKey, MutationOptionsBase options)
        {
            if (mutationKey == null) throw new ArgumentNullException(nameof(mutationKey));
            if (options == null) throw new ArgumentNullException(nameof(options));
            for (var i = 0; i < _mutationDefaults.Count; i++)
            {
                if (!_mutationDefaults[i].Key.Equals(mutationKey)) continue;
                _mutationDefaults[i] = new KeyValuePair<QueryKey, MutationOptionsBase>(mutationKey, options);
                return;
            }
            _mutationDefaults.Add(new KeyValuePair<QueryKey, MutationOptionsBase>(mutationKey, options));
        }

        /// <summary>
        /// The options every record shares, as the defaults <see cref="SetMutationDefaults"/> registered for keys
        /// leading <paramref name="mutationKey"/> set them, merged in registration order with a later registration
        /// winning: TanStack Query's <c>getMutationDefaults</c>, and what <see cref="DefaultMutationOptions"/> fills
        /// those options from. An empty <see cref="MutationDefaults"/> when none matches. A registration's function
        /// and callbacks are not in it, since they reach only options of the registration's own type: read them with
        /// <see cref="GetMutationDefaults{TOptions}"/>.
        /// </summary>
        public MutationDefaults GetMutationDefaults(QueryKey mutationKey)
        {
            if (mutationKey == null) throw new ArgumentNullException(nameof(mutationKey));
            return (MutationDefaults)FillFromKeyDefaults(new MutationDefaults(), mutationKey);
        }

        /// <summary>
        /// The defaults <see cref="SetMutationDefaults"/> registered for keys leading <paramref name="mutationKey"/>
        /// as options of type <typeparamref name="TOptions"/>: the shared options as
        /// <see cref="GetMutationDefaults"/> reports them, and the function and callbacks of the registrations of
        /// exactly that type, merged in registration order with a later one winning. Null when no registration of
        /// that type matches.
        /// </summary>
        public TOptions? GetMutationDefaults<TOptions>(QueryKey mutationKey) where TOptions : MutationOptionsBase
        {
            if (mutationKey == null) throw new ArgumentNullException(nameof(mutationKey));
            return (TOptions?)TypedKeyDefaults(typeof(TOptions), mutationKey);
        }

        /// <summary>
        /// <paramref name="options"/> with what it leaves null taken from the defaults registered for keys leading
        /// its key, as <see cref="GetMutationDefaults{TOptions}"/> reads them for the options' own type, and then from
        /// <see cref="QueryClientOptions.Mutations"/>: TanStack Query's <c>defaultMutationOptions</c>.
        /// <see cref="Hooks.UseMutation{TVariables, TData}"/> runs its options through this.
        /// </summary>
        public T DefaultMutationOptions<T>(T options) where T : MutationOptionsBase
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            MutationOptionsBase filled = options;
            if (options.MutationKey is { } mutationKey)
            {
                filled = filled.WithDefaults(
                    TypedKeyDefaults(options.GetType(), mutationKey) ?? FillFromKeyDefaults(new MutationDefaults(), mutationKey));
            }
            if (_mutationOptions != null) filled = filled.WithDefaults(_mutationOptions);
            return (T)filled;
        }

        // The registrations of exactly this type merged latest first, carrying the shared options of every
        // matching registration; null when none of this type matches.
        private MutationOptionsBase? TypedKeyDefaults(Type type, QueryKey mutationKey)
        {
            MutationOptionsBase? typed = null;
            for (var i = _mutationDefaults.Count - 1; i >= 0; i--)
            {
                var registered = _mutationDefaults[i];
                if (registered.Value.GetType() != type || !mutationKey.Matches(registered.Key)) continue;
                typed = typed == null ? registered.Value : typed.WithDefaults(registered.Value);
            }
            return typed?.WithSharedFrom(FillFromKeyDefaults(new MutationDefaults(), mutationKey));
        }

        // Fills from each matching registration in turn, the latest first, taking only what is still null. For the
        // shared options, and for a function or callback among layers of the options' own type, that is the result
        // of v5's spread. A layer of another record type fills only the shared options: a function it sets could
        // never be called with these options' types, so it is passed over.
        private MutationOptionsBase FillFromKeyDefaults(MutationOptionsBase filled, QueryKey mutationKey)
        {
            for (var i = _mutationDefaults.Count - 1; i >= 0; i--)
            {
                if (mutationKey.Matches(_mutationDefaults[i].Key)) filled = filled.WithDefaults(_mutationDefaults[i].Value);
            }
            return filled;
        }

        /// <summary>
        /// Wakes the client's paused mutations while the device reads as online, TanStack Query's
        /// <c>resumePausedMutations</c>; offline it does nothing. See <see cref="MutationCache.ResumePausedMutations"/>.
        /// </summary>
        public VelvetTask ResumePausedMutations()
            => NetworkSignals.ReadOnline() ? _mutationCache.ResumePausedMutations() : VelvetTask.CompletedTask;

        internal TimeSpan Now => _clock();

        internal Func<TimeSpan> Clock => _clock;

        // The entries a filter selects, among those that have not expired. A snapshot, since what runs on them
        // can remove entries.
        private QueryEntry[] SelectEntries(QueryFilters? filters)
        {
            CollectGarbage();
            var selected = new List<QueryEntry>();
            foreach (var entry in SnapshotEntries())
            {
                if (QueryFilters.Match(filters, entry)) selected.Add(entry);
            }
            return selected.ToArray();
        }

        // TanStack's queryCache.remove: the entry leaves the cache and its readers move, then the removal is
        // announced. An entry no longer in the cache is left alone.
        internal void RemoveEntry(QueryEntry entry)
        {
            if (!_queryCache.Detach(entry)) return;
            _inactive.Remove(entry);
            Announce(entry);
        }

        private void Announce(QueryEntry removed)
        {
            removed.Remove();
            _queryCache.Notify(new QueryCacheEvent(QueryCacheEventType.Removed, removed.Query));
        }

        internal QueryCache QueryCache => _queryCache;

        internal bool IsExpiredNow(QueryEntry entry) => IsExpired(entry, Now);

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
        private QueryEntry<T> EntryFor<T>(QueryOptions<T, T> options, out QueryFetchOptions<T> fetchOptions, out QueryStaleRule staleTime)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.QueryKey == null) throw new ArgumentException("The options have no QueryKey.", nameof(options));
            if (options.QueryFn == null) throw new ArgumentException("The options have no QueryFn.", nameof(options));
            staleTime = QueryStaleRule.Resolve(options.StaleTime, options.StaleTimeFn, DefaultStaleTime, DefaultStaleTimeFn, suspense: false);
            var gcTime = RequireNonNegative(options.GcTime ?? DefaultGcTime, nameof(QueryOptions<T, T>.GcTime));
            fetchOptions = new QueryFetchOptions<T>().Assign(options, this);
            fetchOptions.SetRetry(options.Retry ?? DefaultRetry ?? false);
            // A new entry takes the options' initial data, as v5's build creates the query with them.
            return Ensure(options.QueryKey, options.QueryKeyHashFn ?? DefaultQueryKeyHashFn, gcTime, fetchOptions);
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

        // An expired entry reads as absent before a sweep removes it, so a render never shows an entry that had
        // already expired and that a sibling's subscription in the same commit would sweep.
        internal QueryEntry<T>? PeekById<T>(QueryKey id)
        {
            _readEpoch++;
            return _queryCache.TryGet(id, out var entry) && !IsExpired(entry, Now) ? Typed<T>(entry) : null;
        }

        internal QueryEntry<T> Build<T>(QueryKey key, QueryKey id, Func<QueryKey, string>? hashFn, TimeSpan gcTime)
        {
            // A reader leaving in this commit's effects hands its entry to the reader arriving in them — a
            // keyed remount, two readers swapped — however short its gcTime, as v5's addObserver clears the
            // removal timeout. Taken back before the sweep, which otherwise replaces an expired entry rather
            // than handing it back.
            if (_queryCache.TryGet(id, out var existing) && existing.InactiveEpoch == _readEpoch)
            {
                MarkActive(existing);
            }
            CollectGarbage();
            if (_queryCache.TryGet(id, out existing))
            {
                var typed = Typed<T>(existing);
                typed.RaiseGcTime(gcTime);
                return typed;
            }

            var entry = new QueryEntry<T>(this, key, id, hashFn, gcTime);
            _queryCache.Add(entry);
            return entry;
        }

        // TanStack's queryCache.build outside a subscription: an entry made here has no reader yet, so it is
        // collected after its gcTime unless one subscribes, as v5 schedules a new query's removal at once.
        internal QueryEntry<T> Ensure<T>(QueryKey key, TimeSpan gcTime) => Ensure<T>(key, DefaultQueryKeyHashFn, gcTime);

        internal QueryEntry<T> Ensure<T>(
            QueryKey key, Func<QueryKey, string>? hashFn, TimeSpan gcTime, QueryFetchOptions<T>? options = null,
            bool heldForRender = false)
        {
            var id = Identify(key, hashFn);
            CollectGarbage();
            // An entry that exists is returned as it is, gcTime and options included, as v5's build returns the
            // query it finds; a new one is made with the options, initial data and all.
            if (_queryCache.TryGet(id, out var existing)) return Typed<T>(existing);

            // The options, initial data and all, are taken before the entry is added, as v5's build constructs the
            // query with them, so the cache announces it holding them.
            var entry = new QueryEntry<T>(this, key, id, hashFn, gcTime);
            if (options != null) entry.SetOptions(options);
            _queryCache.Add(entry);
            MarkInactive(entry);
            entry.RenderHoldPolls = heldForRender ? RenderHoldPolls : 0;
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
            entry.RenderHoldPolls = 0;
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
            List<QueryEntry>? expired = null;
            for (var i = _inactive.Count - 1; i >= 0; i--)
            {
                var entry = _inactive[i];
                if (IsExpired(entry, now))
                {
                    _inactive.RemoveAt(i);
                    if (_queryCache.Detach(entry)) (expired ??= new List<QueryEntry>()).Add(entry);
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
            // Announced once the walk is done, since a cache listener can reach the client and sweep again. A
            // listener's throw is logged rather than thrown, which would end the poll and with it the collection of
            // every other entry.
            if (expired != null)
            {
                foreach (var entry in expired)
                {
                    try
                    {
                        Announce(entry);
                    }
                    catch (Exception listenerFailure)
                    {
                        FiberLogger.LogException(nameof(QueryCache), listenerFailure);
                    }
                }
            }
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
                if (_canExpire && now < _nextWalk)
                {
                    keep = true;
                }
                else
                {
                    ReleaseRenderHolds();
                    keep = Sweep(now);
                }
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
            return since != null && entry.RenderHoldPolls == 0 && entry.FetchStatus == QueryFetchStatus.Idle
                && now - since.Value >= entry.GcTime;
        }

        // An entry a render built is held from collection, however short its gcTime, through the poll that walks the
        // entries next and released at the one after, so a commit whose passive effects run after one walking poll
        // still subscribes to it. A commit whose effects run after two can find it collected and build another.
        private const int RenderHoldPolls = 2;

        private void ReleaseRenderHolds()
        {
            foreach (var entry in _inactive)
            {
                if (entry.RenderHoldPolls > 0) entry.RenderHoldPolls--;
            }
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

        private QueryEntry[] SnapshotEntries() => _queryCache.Snapshot();

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
            Query = new Query(this);
        }

        internal QueryClient Client { get; }
        // The entry as the cache's events, its filters and the options' functions are handed it.
        internal Query Query { get; }
        internal QueryStatus Status { get; private protected set; } = QueryStatus.Pending;
        internal bool HasData { get; private protected set; }
        internal Exception? Error { get; private protected set; }
        internal int FailureCount { get; private protected set; }
        internal Exception? FailureReason { get; private protected set; }
        internal bool IsInvalidated { get; private protected set; }
        internal abstract object? BoxedData { get; }
        internal abstract TimeSpan DataUpdatedAt { get; }
        internal abstract IReadOnlyDictionary<string, object?>? Meta { get; }
        internal abstract int ObserverCount { get; }
        internal abstract bool IsDisabled { get; }
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
        // The walking polls left before an entry a render built may be collected: see QueryClient.ReleaseRenderHolds.
        internal int RenderHoldPolls { get; set; }
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
        private List<VelvetTaskCompletionSource<Unit>>? _suspended;
        // TanStack's #initialState, which a reset returns to: no data, or the initial data the entry took.
        private EntryState _initialState;
        // TanStack's isFetched: whether data has landed or been written, or a request has failed, since the entry
        // was made or reset. Initial data is neither.
        private bool _fetched;
        // TanStack's fetchMeta.fetchMore.direction: the end of an infinite query's pages the latest request was asked
        // for, null for a request that fetches from the first page.
        private FetchDirection? _fetchDirection;

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
                Fetched = entry._fetched;
                FetchDirection = entry._fetchDirection;
            }

            // TanStack's getDefaultState with initial data.
            internal EntryState(T initialData, TimeSpan dataUpdatedAt)
            {
                this = default;
                Status = QueryStatus.Success;
                HasData = true;
                Data = initialData;
                DataUpdatedAt = dataUpdatedAt;
            }

            internal bool Fetched { get; }
            internal FetchDirection? FetchDirection { get; }
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
        internal T Data { get; private set; } = default!;
        internal override object? BoxedData => HasData ? Data : null;
        internal override IReadOnlyDictionary<string, object?>? Meta => _options?.Meta;
        internal override int ObserverCount => _observers.Count;
        internal bool IsRemoved { get; private set; }
        // TanStack's fetchStatus: a request in flight is paused while it waits for the connection or the
        // application's visibility, and fetching otherwise.
        internal override QueryFetchStatus FetchStatus
            => _inFlight == null ? QueryFetchStatus.Idle : _paused ? QueryFetchStatus.Paused : QueryFetchStatus.Fetching;

        internal override TimeSpan DataUpdatedAt => _dataUpdatedAt;

        // TanStack's isStaleByTime: no data is stale, static data never is, and otherwise invalidated data or data
        // as old as the stale time.
        internal bool IsStaleFor(QueryStaleTime staleTime)
        {
            if (!HasData) return true;
            if (staleTime.IsStatic) return false;
            return IsInvalidated || Client.Now - _dataUpdatedAt >= staleTime.Time;
        }

        // TanStack's query.isStatic: data a reader holds static is fetched by no client command.
        private bool IsStatic
        {
            get
            {
                if (!HasData) return false;
                foreach (var observer in _observers)
                {
                    if (observer.Rules.StaleTime.Read(this, rethrow: false).IsStatic) return true;
                }
                return false;
            }
        }

        // The request in flight, which a suspending query waits on; null while there is none.
        internal object? Request => _inFlight;

        internal void SetOptions(QueryFetchOptions<T> options)
        {
            TakeOptions(options);
            ReleaseSuspended();
        }

        // The suspended reads waiting on the entry, as v5's fetchOptimistic waits on its query. One that could
        // resume at once, as where the suspending render's own fetch gave the entry initial data, is released as any
        // other is, so the render registering it suspends and resumes, as v5's thrown promise always settles later.
        internal VelvetTask<Unit> WhenSuspendedCanResume(CancellationToken token)
        {
            var source = new VelvetTaskCompletionSource<Unit>();
            (_suspended ??= new List<VelvetTaskCompletionSource<Unit>>()).Add(source);
            token.Register(static state => ((VelvetTaskCompletionSource<Unit>)state!).TrySetCanceled(), source);
            ReleaseSuspended();
            return source.Task;
        }

        // v5's fetchOptimistic settles once the query holds data or its fetch has settled.
        private bool CanResumeSuspended => HasData || _inFlight == null;

        // Called once an update to the entry is complete, never from inside one, since releasing a suspended read
        // renders its component at once. A release reached while some component renders is posted to the main
        // thread instead, so that one render never runs inside another.
        private void ReleaseSuspended()
        {
            var suspended = _suspended;
            if (suspended == null || !CanResumeSuspended) return;
            if (FiberAmbientStack.Current != null)
            {
                VelvetMainThread.Post(static state => ((QueryEntry<T>)state!).ReleaseSuspended(), this);
                return;
            }
            _suspended = null;
            foreach (var source in suspended)
            {
                source.TrySetResult(Unit.Default);
            }
        }

        // TanStack's query.setOptions: an entry with no data takes the initial data of the options it is handed.
        private void TakeOptions(QueryFetchOptions<T> options)
        {
            _options = options;
            var initialData = options.InitialData;
            if (HasData || initialData == null) return;
            var data = initialData();
            if (data is null) return;
            var updatedAt = options.InitialDataUpdatedAtFn != null ? options.InitialDataUpdatedAtFn() : options.InitialDataUpdatedAt;
            _initialState = new EntryState(data, updatedAt ?? Client.Now);
            Data = data;
            HasData = true;
            Error = null;
            Status = QueryStatus.Success;
            IsInvalidated = false;
            _dataUpdatedAt = _initialState.DataUpdatedAt;
            Notify(QueryActionType.SetState);
        }

        // The observer decides whether its subscription fetches, as v5's onSubscribe does.
        internal void Subscribe(QueryEntryObserver<T> observer)
        {
            _observers.Add(observer);
            Client.ObserverAdded();
            if (_observers.Count == 1) Client.MarkActive(this);
            Client.QueryCache.Notify(new QueryCacheEvent(QueryCacheEventType.ObserverAdded, Query, observer: observer));

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
            if (_observers.Count == 0 && !IsRemoved) OnLastObserverLeft();
            Client.QueryCache.Notify(new QueryCacheEvent(QueryCacheEventType.ObserverRemoved, Query, observer: observer));
        }

        private void OnLastObserverLeft()
        {
            if (_inFlight != null && (_requestTakesToken || (_paused && Status == QueryStatus.Pending)))
            {
                CancelInFlight();
                Revert();
                Notify(QueryActionType.SetState);
            }
            else
            {
                _retriesStopped = true;
            }
            Client.MarkInactive(this);
            ReleaseSuspended();
        }

        // Fetch, handing back a task of the request now in flight: the one this started, or the one it joined.
        internal VelvetTask<T> FetchAndWait(QueryFetchOptions<T> options, bool cancelRefetch, FetchDirection? direction = null)
        {
            Fetch(options, cancelRefetch, direction);
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
        internal FetchDirection? FetchDirection => _fetchDirection;

        internal void Fetch(QueryFetchOptions<T> options, bool cancelRefetch, FetchDirection? direction = null)
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
            TakeOptions(options);

            // Taken after a request started over is cancelled, as v5 stores the state at that point.
            _revertState = new EntryState(this);
            _fetchDirection = direction;
            // TanStack's behavior.onFetch, which reads the data as the request finds it.
            var attempt = options.Behavior?.OnFetch(HasData, Data, direction);
            _requestTakesToken = false;
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
                Notify(QueryActionType.Fetch);
            }
            finally
            {
                RunAsync(options, request, attempt).Forget();
            }
            // Initial data the options gave lets a suspended read resume.
            ReleaseSuspended();
        }

        private async VelvetTask RunAsync(
            QueryFetchOptions<T> options, CancellationTokenSource request, Func<QueryFunctionContext, VelvetTask<T>>? attempt)
        {
            var queryFn = options.QueryFn;
            var retry = options.Retry;
            var retryDelay = options.RetryDelay;
            var sharing = options.StructuralSharing;
            var networkMode = options.NetworkMode;
            var meta = options.Meta;
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
                // The function has read the token only once it has started, which a request paused at the outset has not.
                if (ReferenceEquals(_inFlight, request)) _requestTakesToken = options.TakesToken;
                var task = Start(attempt ?? ContextualOf(queryFn), queryFn, meta, request);

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

                TimeSpan delay;
                bool retries;
                try
                {
                    // v5's retryer asks for the wait before it asks whether to retry, as RetryPolicy does.
                    delay = retryDelay(failures, failure);
                    retries = retry.Allows(failures, failure);
                }
                catch (Exception decisionFailure)
                {
                    Fail(request, decisionFailure, failures);
                    return;
                }

                if (!retries || _retriesStopped)
                {
                    Fail(request, failure, failures);
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
        private static Func<QueryFunctionContext, VelvetTask<T>>? ContextualOf(Func<CancellationToken, VelvetTask<T>> queryFn)
            => queryFn.Target is QueryFunctions.Contextual<T> contextual ? contextual.Invoke : null;

        // A context-taking function, an infinite query's paging among them, is handed the context, whose token marks
        // the request as one that took it when it is read, as reading v5's signal does.
        private VelvetTask<T> Start(
            Func<QueryFunctionContext, VelvetTask<T>>? contextual, Func<CancellationToken, VelvetTask<T>> queryFn,
            IReadOnlyDictionary<string, object?>? meta, CancellationTokenSource request)
        {
            var token = request.Token;
            try
            {
                if (contextual != null)
                {
                    return contextual(new QueryFunctionContext(Client, Key, meta, token, () =>
                    {
                        if (ReferenceEquals(_inFlight, request)) _requestTakesToken = true;
                    }));
                }
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
            Notify(paused ? QueryActionType.Pause : QueryActionType.Continue);
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
            _fetched = true;
            Error = null;
            Status = QueryStatus.Success;
            IsInvalidated = false;
            FailureCount = 0;
            FailureReason = null;
            _dataUpdatedAt = Client.Now;
            Exception? callbackFailure = null;
            try
            {
                Notify(QueryActionType.Success);
                // v5 runs the cache's callbacks inside the request, so a throw from either is the request's failure,
                // over the data that has already landed.
                callbackFailure = RunSuccessCallbacks(data);
            }
            finally
            {
                if (callbackFailure == null) ResolveWaiters(shared);
                ReleaseSuspended();
            }
            if (callbackFailure != null) FailLanded(callbackFailure);
        }

        private Exception? RunSuccessCallbacks(T data)
        {
            var cache = Client.QueryCache;
            try
            {
                cache.OnSuccess?.Invoke(data, Query);
                cache.OnSettled?.Invoke(data, Error, Query);
                return null;
            }
            catch (Exception failure)
            {
                return failure;
            }
        }

        // v5's error branch for a throw the cache's success callbacks raised: the request has settled already, so
        // the entry takes the error over its data, and the callers waiting are handed it.
        private void FailLanded(Exception error)
        {
            Error = error;
            Status = QueryStatus.Error;
            IsInvalidated = true;
            FailureCount++;
            FailureReason = error;
            try
            {
                Notify(QueryActionType.Error);
                error = RunErrorCallbacks(error);
            }
            finally
            {
                RejectWaiters(error);
                ReleaseSuspended();
            }
        }

        // A throw from OnError skips OnSettled and is what the callers waiting are handed, as v5's catch block
        // rethrows it.
        private Exception RunErrorCallbacks(Exception error)
        {
            var cache = Client.QueryCache;
            try
            {
                cache.OnError?.Invoke(error, Query);
            }
            catch (Exception callbackFailure)
            {
                return callbackFailure;
            }
            cache.OnSettled?.Invoke(BoxedData, error, Query);
            return error;
        }

        // TanStack's setData with manual set, as setQueryData calls it: the data lands as a request's would, but a
        // request in flight keeps running and its failure count stays.
        internal T SetData(T data)
        {
            // v5's setData shares with the query's own options, those the entry was last handed.
            var shared = Share(_options?.StructuralSharing, data);
            Data = shared;
            HasData = true;
            _fetched = true;
            Error = null;
            Status = QueryStatus.Success;
            IsInvalidated = false;
            _dataUpdatedAt = Client.Now;
            Notify(QueryActionType.Success);
            ReleaseSuspended();
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
            _fetched = true;
            FailureCount = failures + 1;
            FailureReason = error;
            try
            {
                Notify(QueryActionType.Error);
                error = RunErrorCallbacks(error);
            }
            finally
            {
                RejectWaiters(error);
                ReleaseSuspended();
            }
        }

        private void RecordFailure(CancellationTokenSource request, int failureCount, Exception error)
        {
            if (!ReferenceEquals(_inFlight, request)) return;
            FailureCount = failureCount;
            FailureReason = error;
            Notify(QueryActionType.Failed);
        }

        private void End(CancellationTokenSource request)
        {
            _inFlight = null;
            _paused = false;
            RescheduleCollection(Client.Now);
            // MUTANT_SURVIVES(equivalent, line removed): nothing cancels a settled request again, so its source only waits for collection.
            request.Dispose();
        }

        // Only an entry an enabled query reads is fetched, as v5 refetches the active queries, and not one a reader
        // holds static, which v5's refetchQueries passes over.
        internal override VelvetTask Invalidate(bool cancelRefetch)
        {
            if (!IsInvalidated)
            {
                IsInvalidated = true;
                AnnounceUpdate(QueryActionType.Invalidate);
            }
            if (IsStatic) return VelvetTask.CompletedTask;
            foreach (var observer in _observers)
            {
                if (!observer.IsEnabled(this)) continue;
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
                    if (observer.IsEnabled(this)) return true;
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
                    if (observer.Rules.IsStale(this, rethrow: false)) return true;
                }
                return false;
            }
        }

        // An entry written by hand holds no options to fetch with and is left alone, as is a disabled one and one a
        // reader holds static (TanStack's isStatic).
        internal override VelvetTask Refetch(bool cancelRefetch)
        {
            return _options != null && !IsDisabled && !IsStatic ? FetchAndWait(_options, cancelRefetch) : VelvetTask.CompletedTask;
        }

        // TanStack's isDisabled: with readers, whether none of them is enabled; without, whether no data has landed or
        // been written and no request has failed.
        internal override bool IsDisabled => _observers.Count > 0 ? !IsActive : !_fetched;


        // TanStack's query.cancel({ revert }): the request stops and its token is cancelled. Reverting puts the
        // entry back as the request found it; otherwise the cancellation is the entry's error.
        internal override void CancelRequest(bool revert)
        {
            if (_inFlight == null) return;
            CancelInFlight();
            if (revert)
            {
                Revert();
                RescheduleCollection(Client.Now);
                Notify(QueryActionType.SetState);
                ReleaseSuspended();
                return;
            }
            var error = new OperationCanceledException();
            Error = error;
            Status = QueryStatus.Error;
            IsInvalidated = true;
            _fetched = true;
            FailureCount++;
            FailureReason = error;
            RescheduleCollection(Client.Now);
            try
            {
                Notify(QueryActionType.Error);
                // v5's cancel that neither reverts nor is silent is an error like any other, the cache's callbacks
                // included.
                RunErrorCallbacks(error);
            }
            finally
            {
                ReleaseSuspended();
            }
        }

        // TanStack's query.reset(): the state the entry was made in, its initial data included, its request
        // cancelled without a word.
        internal override void ResetState()
        {
            CancelInFlight();
            Restore(_initialState);
            Notify(QueryActionType.SetState);
            ReleaseSuspended();
        }

        // TanStack's query.onFocus and onOnline: the first observer that asks for it refetches, joining a request
        // already in flight.
        internal override void OnSignal(bool reconnect)
        {
            foreach (var observer in _observers)
            {
                var rules = observer.Rules;
                var rule = reconnect ? rules.RefetchOnReconnect : rules.RefetchOnWindowFocus;
                if (!rules.ShouldFetchOn(rule, this, rethrow: false)) continue;
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
            ReleaseSuspended();
        }

        private void Revert()
        {
            if (_revertState is { } saved) Restore(saved);
        }

        private void Restore(in EntryState saved)
        {
            _fetched = saved.Fetched;
            _fetchDirection = saved.FetchDirection;
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

        // TanStack's dispatch: the readers are told, then the cache's listeners, while the entry is in the cache.
        private void Notify(QueryActionType action)
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnQueryUpdate();
            }
            AnnounceUpdate(action);
        }

        private void AnnounceUpdate(QueryActionType action)
        {
            var cache = Client.QueryCache;
            if (cache.TryGet(Id, out var held) && ReferenceEquals(held, this))
            {
                cache.Notify(new QueryCacheEvent(QueryCacheEventType.Updated, Query, action));
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
