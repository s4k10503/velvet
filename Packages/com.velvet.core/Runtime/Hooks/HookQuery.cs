#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    /// The properties of a <see cref="QueryResult{T}"/>, for <see cref="QueryOptionsBase{TQueryFnData, TData}.NotifyOnChangeProps"/>:
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
        /// <summary><see cref="QueryResult{T}.FetchStatus"/>.</summary>
        FetchStatus = 1 << 11,
        /// <summary><see cref="QueryResult{T}.IsPaused"/>.</summary>
        IsPaused = 1 << 12,
        /// <summary><see cref="InfiniteQueryResult{TData}.HasNextPage"/>.</summary>
        HasNextPage = 1 << 13,
        /// <summary><see cref="InfiniteQueryResult{TData}.HasPreviousPage"/>.</summary>
        HasPreviousPage = 1 << 14,
        /// <summary><see cref="InfiniteQueryResult{TData}.IsFetchingNextPage"/>.</summary>
        IsFetchingNextPage = 1 << 15,
        /// <summary><see cref="InfiniteQueryResult{TData}.IsFetchingPreviousPage"/>.</summary>
        IsFetchingPreviousPage = 1 << 16,
        /// <summary><see cref="InfiniteQueryResult{TData}.IsFetchNextPageError"/>.</summary>
        IsFetchNextPageError = 1 << 17,
        /// <summary><see cref="InfiniteQueryResult{TData}.IsFetchPreviousPageError"/>.</summary>
        IsFetchPreviousPageError = 1 << 18,
        /// <summary><see cref="QueryResult{T}.IsRefetching"/>.</summary>
        IsRefetching = 1 << 19,
        /// <summary><see cref="QueryResult{T}.IsRefetchError"/>.</summary>
        IsRefetchError = 1 << 20,
        /// <summary>Every property, TanStack Query's <c>'all'</c>.</summary>
        All = Status | IsPending | IsSuccess | IsError | Data | Error | IsFetching | IsStale | FailureCount | FailureReason
            | IsPlaceholderData | FetchStatus | IsPaused | HasNextPage | HasPreviousPage | IsFetchingNextPage
            | IsFetchingPreviousPage | IsFetchNextPageError | IsFetchPreviousPageError | IsRefetching | IsRefetchError,
    }

    /// <summary>
    /// Whether a query fetches again when the application becomes visible or the device comes back online:
    /// TanStack Query's <c>refetchOnWindowFocus</c> and <c>refetchOnReconnect</c> given as <c>false</c>,
    /// <c>true</c> or <c>'always'</c>.
    /// </summary>
    public enum QueryRefetchMode
    {
        /// <summary>Never, TanStack Query's <c>false</c>.</summary>
        Never,
        /// <summary>When its data is stale, TanStack Query's <c>true</c>.</summary>
        IfStale,
        /// <summary>Whether or not its data is stale, TanStack Query's <c>'always'</c>.</summary>
        Always,
    }

    /// <summary>
    /// How long a query's data stays fresh after it lands: TanStack Query's <c>staleTime</c> given as a number,
    /// to which a <see cref="TimeSpan"/> converts, or as <c>'static'</c>, <see cref="Static"/>.
    /// </summary>
    public readonly struct QueryStaleTime
    {
        private QueryStaleTime(TimeSpan time, bool isStatic)
        {
            Time = time;
            IsStatic = isStatic;
        }

        /// <summary>
        /// TanStack Query's <c>'static'</c>: data that never turns stale, not by age and not by an invalidation.
        /// A mount, focus or reconnect does not fetch it, and
        /// <see cref="QueryClient.InvalidateQueries(QueryFilters, RefetchOptions)"/>,
        /// <see cref="QueryClient.RefetchQueries"/> and <see cref="QueryClient.ResetQueries"/> pass over an entry
        /// holding data that one of its readers holds static. A refetch interval and
        /// <see cref="QueryResult{T}.Refetch"/> still fetch. An entry with no data is loaded on mount and by those
        /// commands as any is, but not on focus or reconnect, which pass over a static query whatever its entry holds.
        /// </summary>
        public static QueryStaleTime Static { get; } = new(TimeSpan.MaxValue, true);

        /// <summary>The age at which data turns stale; <see cref="TimeSpan.MaxValue"/> under
        /// <see cref="Static"/>.</summary>
        public TimeSpan Time { get; }

        /// <summary>Whether this is <see cref="Static"/>.</summary>
        public bool IsStatic { get; }

        /// <summary>Data turns stale once its age reaches <paramref name="time"/>; <see cref="TimeSpan.MaxValue"/>
        /// keeps it fresh until it is invalidated.</summary>
        public static implicit operator QueryStaleTime(TimeSpan time) => new(time, false);
    }

    /// <summary>
    /// TanStack Query's <c>QueryErrorResetBoundary</c> value: whether the queries below it may fetch again after
    /// an error a query threw to an error boundary, for <see cref="QueryOptionsBase{TQueryFnData, TData}.ThrowOnError"/>
    /// and <see cref="Hooks.UseSuspenseQuery{TQueryFnData, TData}"/>. Provide one with
    /// <c>V.Provider(QueryErrorResetBoundaryContext.Ref, value: boundary, children: ...)</c>, keeping the instance
    /// across renders, and call <see cref="Reset"/> from the error boundary's <c>onReset</c>; without one the
    /// queries share the context's default instance, as v5's do.
    /// </summary>
    public sealed class QueryErrorResetBoundary
    {
        /// <summary>Whether <see cref="Reset"/> has been called since <see cref="ClearReset"/> last ran: a query
        /// rendering below it then throws no error, and loads an entry that failed when it mounts.</summary>
        public bool IsReset { get; private set; }

        /// <summary>Lets the queries below the boundary fetch again, TanStack Query's <c>reset</c>.</summary>
        public void Reset() => IsReset = true;

        /// <summary>Undoes <see cref="Reset"/>, TanStack Query's <c>clearReset</c>. A query below the boundary
        /// calls it from the effects of its first commit, and a suspended query when the request it waits on
        /// settles with no data.</summary>
        public void ClearReset() => IsReset = false;
    }

    /// <summary>
    /// The context <see cref="Hooks.UseQueryErrorResetBoundary"/> reads: TanStack Query's
    /// <c>QueryErrorResetBoundary</c> component is <c>V.Provider(QueryErrorResetBoundaryContext.Ref, ...)</c>.
    /// </summary>
    public static class QueryErrorResetBoundaryContext
    {
        private static readonly QueryErrorResetBoundary s_global = new();

        /// <summary>The context; its default value is one instance every query without a Provider shares.</summary>
        public static readonly ComponentContext<QueryErrorResetBoundary> Ref
            = ComponentContext<QueryErrorResetBoundary>.Create(defaultValue: s_global);

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_global.ClearReset();
    }

    /// <summary>
    /// Data a query shows while it has none of its own, or nothing: what
    /// <see cref="QueryOptionsBase{TQueryFnData, TData}.PlaceholderData"/> returns, where TanStack Query's function
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

    /// <summary>Ready-made <see cref="QueryOptionsBase{TQueryFnData, TData}.PlaceholderData"/> functions.</summary>
    public static class QueryPlaceholder
    {
        /// <summary>
        /// TanStack Query's <c>keepPreviousData</c>: the data of the last entry this component read that had
        /// some, so a component changing its key keeps showing the old key's data until the new key's lands.
        /// Pass it as <c>PlaceholderData = QueryPlaceholder.KeepPreviousData</c>.
        /// </summary>
        /// <param name="previousData">That entry's data.</param>
        /// <param name="previousQuery">That entry; null when there is no such entry.</param>
        /// <returns>The previous data, or no data when there is none.</returns>
        public static QueryPlaceholder<T> KeepPreviousData<T>(T? previousData, Query? previousQuery)
            => previousQuery == null ? default : new QueryPlaceholder<T>(previousData!);
    }

    /// <summary>
    /// What every query's options hold, whatever fetches its data: TanStack Query's options with their
    /// <c>queryKey</c>, <c>meta</c>, <c>enabled</c>, <c>select</c>, <c>placeholderData</c>, <c>initialData</c>,
    /// <c>initialDataUpdatedAt</c>, <c>staleTime</c>, <c>gcTime</c>, <c>refetchInterval</c>,
    /// <c>refetchIntervalInBackground</c>, <c>refetchOnMount</c>, <c>refetchOnWindowFocus</c>,
    /// <c>refetchOnReconnect</c>, <c>retry</c>, <c>retryDelay</c>, <c>retryOnMount</c>, <c>throwOnError</c>,
    /// <c>structuralSharing</c> and <c>notifyOnChangeProps</c>. <see cref="QueryOptions{TQueryFnData, TData}"/> adds
    /// a query function, and <see cref="InfiniteQueryOptions{TQueryFnData, TPageParam, TData}"/> a page function and
    /// its paging.
    /// </summary>
    /// <typeparam name="TQueryFnData">What the entry holds.</typeparam>
    /// <typeparam name="TData">What the result's <see cref="QueryResult{T}.Data"/> holds; the entry's data unless
    /// <see cref="Select"/> turns it into something else.</typeparam>
    /// <param name="QueryKey">Names the cache entry. Queries presenting equal keys share one entry.</param>
    public abstract record QueryOptionsBase<TQueryFnData, TData>(QueryKey QueryKey)
    {
        // What a request runs, as v5 reads options.queryFn; an infinite query's runs its behavior instead.
        internal abstract QueryFunction<TQueryFnData> Function { get; }

        /// <summary>
        /// TanStack Query's <c>meta</c>: what the entry carries as <see cref="Query.Meta"/> for the query cache's
        /// callbacks, its events and a filter's predicate, and what a context-taking query function is handed. The
        /// entry holds the meta of the options it was last handed.
        /// </summary>
        public IReadOnlyDictionary<string, object?>? Meta { get; init; }

        /// <summary>Overrides <see cref="QueryClient.DefaultStaleTime"/> for this query, TanStack Query's
        /// <c>staleTime</c> given as a number or as <c>'static'</c>: see <see cref="QueryStaleTime"/>.</summary>
        public QueryStaleTime? StaleTime { get; init; }

        /// <summary>
        /// <see cref="StaleTime"/> given as a function of the entry the query reads, TanStack Query's
        /// <c>staleTime</c> as a function. It is asked each time the stale time is read: for the result's
        /// <see cref="QueryResult{T}.IsStale"/>, for whether to fetch, and for the wait until the data turns stale.
        /// It replaces <see cref="StaleTime"/> when both are set. One that throws during a render throws from
        /// the hook, and one <see cref="QueryClient.FetchQuery{T}"/> or <see cref="QueryClient.EnsureQueryData{T}"/>
        /// asks from that call, as v5's do; elsewhere it counts as <see cref="TimeSpan.MaxValue"/>, and its failure
        /// is logged once for each function instance.
        /// </summary>
        public Func<Query, QueryStaleTime>? StaleTimeFn { get; init; }

        /// <summary>Overrides <see cref="QueryClient.DefaultGcTime"/> for this query. An entry keeps the
        /// longest time any query subscribing to it asked for.</summary>
        public TimeSpan? GcTime { get; init; }

        /// <summary>Overrides <see cref="QueryClient.DefaultRetry"/> for this query: which failed requests run
        /// again before the failure is the entry's error, TanStack Query's <c>retry</c>. A number retries that
        /// many times, zero or less nothing; <c>true</c> retries without end and <c>false</c> never;
        /// <see cref="RetryRule.When"/> asks a function of the failures so far and the failure's exception before
        /// each retry, and one that throws fails the request.</summary>
        public RetryRule? Retry { get; init; }

        /// <summary>Overrides <see cref="QueryClient.DefaultRetryDelay"/> for this query: the wait before the
        /// next request, from the number of failures before this one and this failure's exception, TanStack
        /// Query's <c>retryDelay</c>.</summary>
        public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

        /// <summary>
        /// Overrides <see cref="QueryClient.DefaultNetworkMode"/> for this query: TanStack Query's
        /// <c>networkMode</c>. Under <see cref="Velvet.NetworkMode.Online"/> a request does not start while the
        /// device reads as offline and the query reports <see cref="QueryFetchStatus.Paused"/> until it does;
        /// <see cref="Velvet.NetworkMode.OfflineFirst"/> makes the first request whatever the connection and
        /// pauses only before a retry; <see cref="Velvet.NetworkMode.Always"/> never pauses for the
        /// connection. A retry also waits for the application to be visible. <see cref="NetworkSignals"/> owns
        /// the readings.
        /// </summary>
        public NetworkMode? NetworkMode { get; init; }

        /// <summary>
        /// Overrides <see cref="QueryClient.DefaultQueryKeyHashFn"/> for this query: TanStack Query's
        /// <c>queryKeyHashFn</c>. Queries whose keys it maps to one string read one entry, and a query that
        /// sets none reads a different entry from one that does, as <see cref="QueryClient"/> describes.
        /// </summary>
        public Func<QueryKey, string>? QueryKeyHashFn { get; init; }

        /// <summary>
        /// Chooses the data an entry holds when a request lands, from the data it held, null when it held
        /// none, and the data that arrived: TanStack Query's <c>structuralSharing</c> given as a function.
        /// When null, the data that arrived keeps the instance the entry held where the two are deeply equal
        /// and keeps the equal parts of it where they are not; <c>(_, arrived) =&gt; arrived</c> turns that
        /// off. The same choice is made between a result's data and what <see cref="Select"/> or
        /// <see cref="PlaceholderData"/> produces next. The values are boxed, as v5 hands its function
        /// <c>unknown</c>: a function is handed <typeparamref name="TQueryFnData"/> values when it chooses the
        /// entry's data and <typeparamref name="TData"/> values when it chooses the result's, and has to return
        /// the type it was handed. What counts as deeply equal is described in react-migration.md §1-2b.
        /// </summary>
        public Func<object?, object, object>? StructuralSharing { get; init; }

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
        /// <see cref="Enabled"/> given as a function of the entry the query reads, TanStack Query's <c>enabled</c>
        /// as a function. It is asked wherever the query's being enabled is read, by its renders, its commits, its
        /// signals and the client's commands, and replaces <see cref="Enabled"/> when both are set. One that throws
        /// during a render throws from the hook; elsewhere it counts as false, and its failure is logged once for
        /// each function instance.
        /// </summary>
        public Func<Query, bool>? EnabledFn { get; init; }

        /// <summary>
        /// TanStack Query's <c>retryOnMount</c>: false keeps a query mounting over an entry whose request failed
        /// and that holds no data from fetching it again. A query that throws its errors to an error boundary
        /// counts it as false until its <see cref="QueryErrorResetBoundary"/> is reset.
        /// </summary>
        public bool RetryOnMount { get; init; } = true;

        /// <summary>
        /// <see cref="RetryOnMount"/> given as a function of the entry, asked when a component mounts over an entry
        /// that failed with no data, and by the render before that; it replaces <see cref="RetryOnMount"/> when
        /// both are set, and fails as <see cref="EnabledFn"/> does.
        /// </summary>
        public Func<Query, bool>? RetryOnMountFn { get; init; }

        /// <summary>
        /// TanStack Query's <c>initialData</c> given as a function: the data an entry with none takes when the
        /// query is handed it, as if a request had landed it. The entry is then <see cref="QueryStatus.Success"/>
        /// with data dated by <see cref="InitialDataUpdatedAt"/>, which <see cref="StaleTime"/> judges as it judges
        /// fetched data, and <see cref="QueryClient.ResetQueries"/> puts it back. It is asked while the entry has
        /// no data: when a render builds the entry, and each time the query hands the entry its options. A null
        /// return is no data.
        /// </summary>
        public Func<TQueryFnData>? InitialData { get; init; }

        /// <summary>
        /// TanStack Query's <c>initialDataUpdatedAt</c> given as a value: when <see cref="InitialData"/>'s data
        /// was last updated, on the client's <see cref="QueryClientOptions.Clock"/>. When null, the data dates
        /// from the moment the entry takes it.
        /// </summary>
        public TimeSpan? InitialDataUpdatedAt { get; init; }

        /// <summary>
        /// TanStack Query's <c>initialDataUpdatedAt</c> given as a function, asked when the entry takes
        /// <see cref="InitialData"/>'s data; a null return dates the data from that moment. It replaces
        /// <see cref="InitialDataUpdatedAt"/> when both are set.
        /// </summary>
        public Func<TimeSpan?>? InitialDataUpdatedAtFn { get; init; }

        /// <summary>
        /// Overrides <see cref="QueryClient.DefaultThrowOnError"/> for this query, TanStack Query's
        /// <c>throwOnError</c> given as a boolean: true throws the entry's error from the hook, to the nearest
        /// error boundary, once the result is <see cref="QueryStatus.Error"/> and not fetching, unless the nearest
        /// <see cref="QueryErrorResetBoundary"/> has been reset since a query below it last mounted. While that
        /// boundary has not been reset, a query that throws its errors does not fetch again on mounting over an
        /// entry with an error and no data, as <see cref="RetryOnMount"/> false does.
        /// </summary>
        public bool? ThrowOnError { get; init; }

        /// <summary>
        /// <see cref="ThrowOnError"/> given as a function, handed the error and the entry, TanStack Query's
        /// <c>throwOnError</c> as a function: it decides in place of the value, asked during a render over an
        /// entry or a result in error. It replaces <see cref="ThrowOnError"/> when both are set, and a query's own
        /// setting, in either form, stands ahead of the client's.
        /// </summary>
        public Func<Exception, Query, bool>? ThrowOnErrorFn { get; init; }

        /// <summary>
        /// TanStack Query's <c>select</c>: turns the entry's data, and placeholder data, into the result's. Over
        /// the entry's data it runs again only when that data's instance or this function's changes, so pass a
        /// stable instance, such as one held in a field, to keep from running it each render. One that throws makes the result an
        /// error. Required when <typeparamref name="TData"/> is not <typeparamref name="TQueryFnData"/>.
        /// </summary>
        public Func<TQueryFnData, TData>? Select { get; init; }

        /// <summary>
        /// TanStack Query's <c>placeholderData</c> given as a function: data the result shows while the entry
        /// has none and is pending, handed the data and the <see cref="Query"/> of the last entry this
        /// component read that had data and still has it — default and null when there is none. The result is then <see cref="QueryStatus.Success"/>
        /// with <see cref="QueryResult{T}.IsPlaceholderData"/> true, and the entry is left without data.
        /// <see cref="QueryPlaceholder.KeepPreviousData{T}"/> keeps the previous key's data; <c>(_, _) =&gt; value</c>
        /// is the value form.
        /// </summary>
        public Func<TQueryFnData?, Query?, QueryPlaceholder<TQueryFnData>>? PlaceholderData { get; init; }

        /// <summary>
        /// TanStack Query's <c>refetchInterval</c> given as a number: an enabled query fetches again each time
        /// this much has passed on the client's clock since its entry last changed — a request starting,
        /// landing or failing — or since the interval last came round, whether or not its data is stale,
        /// joining a request already in flight. The wait is checked once a frame. Null, zero or less, or <see cref="TimeSpan.MaxValue"/> fetches on no
        /// interval.
        /// </summary>
        public TimeSpan? RefetchInterval { get; init; }

        /// <summary>
        /// TanStack Query's <c>refetchInterval</c> given as a function: the interval, handed the entry the query
        /// reads, with the meaning <see cref="RefetchInterval"/> gives a number and null for no interval. It
        /// replaces <see cref="RefetchInterval"/> when both are set. It is called whenever the entry changes and
        /// at every commit of the component, not once a frame; one that throws counts as no interval, and its
        /// failure is logged once for each function instance.
        /// </summary>
        public Func<Query, TimeSpan?>? RefetchIntervalFn { get; init; }

        /// <summary>
        /// TanStack Query's <c>refetchIntervalInBackground</c>: true lets <see cref="RefetchInterval"/> fetch
        /// while the application is not visible, as <see cref="NetworkSignals"/> reads it; by default it waits.
        /// </summary>
        public bool RefetchIntervalInBackground { get; init; }

        /// <summary>
        /// Overrides <see cref="QueryClient.DefaultRefetchOnMount"/> for this query: whether an enabled query
        /// mounting over an entry that holds data fetches it again, TanStack Query's <c>refetchOnMount</c>. An
        /// entry with no data is fetched on mount whatever this says, but for one whose request failed and that
        /// <see cref="RetryOnMount"/>, or a <see cref="ThrowOnError"/> whose boundary is not reset, holds back.
        /// </summary>
        public QueryRefetchMode? RefetchOnMount { get; init; }

        /// <summary>
        /// <see cref="RefetchOnMount"/> given as a function, TanStack Query's <c>refetchOnMount</c> as a function:
        /// handed the entry the query reads, and asked when the component first subscribes, and by the render
        /// before that to report the fetch. It replaces <see cref="RefetchOnMount"/> when both are set. One that
        /// throws during a render throws from the hook; elsewhere it is logged and counts as
        /// <see cref="QueryRefetchMode.Never"/>.
        /// </summary>
        public Func<Query, QueryRefetchMode>? RefetchOnMountFn { get; init; }

        /// <summary>
        /// Overrides <see cref="QueryClient.DefaultRefetchOnWindowFocus"/> for this query: whether an enabled
        /// query fetches again when the application becomes visible, TanStack Query's
        /// <c>refetchOnWindowFocus</c>.
        /// </summary>
        public QueryRefetchMode? RefetchOnWindowFocus { get; init; }

        /// <summary>
        /// <see cref="RefetchOnWindowFocus"/> given as a function, TanStack Query's <c>refetchOnWindowFocus</c>
        /// as a function: handed the entry the query reads, and called each time the application becomes visible
        /// for an enabled query. It replaces <see cref="RefetchOnWindowFocus"/> when both are set, and one that
        /// throws is logged and counts as <see cref="QueryRefetchMode.Never"/>.
        /// </summary>
        public Func<Query, QueryRefetchMode>? RefetchOnWindowFocusFn { get; init; }

        /// <summary>
        /// Overrides <see cref="QueryClient.DefaultRefetchOnReconnect"/> for this query: whether an enabled query
        /// fetches again when the device comes back online, TanStack Query's <c>refetchOnReconnect</c>.
        /// </summary>
        public QueryRefetchMode? RefetchOnReconnect { get; init; }

        /// <summary>
        /// <see cref="RefetchOnReconnect"/> given as a function, with the terms of
        /// <see cref="RefetchOnWindowFocusFn"/> for the device coming back online.
        /// </summary>
        public Func<Query, QueryRefetchMode>? RefetchOnReconnectFn { get; init; }
    }

    /// <summary>
    /// Options passed to <see cref="Hooks.UseQuery{TQueryFnData, TData}"/>: TanStack Query's
    /// <c>{ queryKey, queryFn }</c> with the options <see cref="QueryOptionsBase{TQueryFnData, TData}"/> lists.
    /// <see cref="QueryOptions{T}"/> is the form without a select that changes the data's type.
    /// </summary>
    /// <typeparam name="TQueryFnData">What the query function returns and the entry holds.</typeparam>
    /// <typeparam name="TData">What the result's <see cref="QueryResult{T}.Data"/> holds.</typeparam>
    /// <param name="QueryKey">Names the cache entry. Queries presenting equal keys share one entry.</param>
    /// <param name="QueryFn">Fetches the entry's data. The token is cancelled when a refetch starts over this
    /// request, when <see cref="QueryClient.Clear"/> removes the entry, and when the last query reading the
    /// entry leaves while the request is in flight, which also puts the entry back as it stood before the
    /// request started: TanStack Query aborts the request of a query function that read its <c>signal</c> in
    /// the same case. A function that reads <see cref="QueryFunctionContext.CancellationToken"/> has read it, and so
    /// has a function taking the token; one taking no token never has, and its request runs on after the last
    /// reader leaves.</param>
    public record QueryOptions<TQueryFnData, TData>(QueryKey QueryKey, QueryFunction<TQueryFnData> QueryFn)
        : QueryOptionsBase<TQueryFnData, TData>(QueryKey)
    {
        /// <summary>Options whose query function takes TanStack Query's <c>QueryFunctionContext</c>: the client, the
        /// key, the options' <see cref="QueryOptionsBase{TQueryFnData, TData}.Meta"/> and the token.</summary>
        /// <param name="queryKey">Names the cache entry. Queries presenting equal keys share one entry.</param>
        /// <param name="queryFn">Fetches the entry's data.</param>
        public QueryOptions(QueryKey queryKey, Func<QueryFunctionContext, VelvetTask<TQueryFnData>> queryFn)
            : this(queryKey, QueryFunction.Of(queryFn))
        {
        }

        /// <summary>Options whose query function takes the token, and so has read it.</summary>
        /// <param name="queryKey">Names the cache entry. Queries presenting equal keys share one entry.</param>
        /// <param name="queryFn">Fetches the entry's data.</param>
        public QueryOptions(QueryKey queryKey, Func<CancellationToken, VelvetTask<TQueryFnData>> queryFn)
            : this(queryKey, QueryFunction.Of(queryFn))
        {
        }

        /// <summary>
        /// Options whose query function takes no token: its request is not cancelled when the last query
        /// reading the entry leaves, and lands in the entry for the next reader to find.
        /// </summary>
        /// <param name="queryKey">Names the cache entry. Queries presenting equal keys share one entry.</param>
        /// <param name="queryFn">Fetches the entry's data.</param>
        public QueryOptions(QueryKey queryKey, Func<VelvetTask<TQueryFnData>> queryFn)
            : this(queryKey, QueryFunction.Of(queryFn))
        {
        }

        internal override QueryFunction<TQueryFnData> Function => QueryFn;
    }

    /// <summary>
    /// <see cref="QueryOptions{TQueryFnData, TData}"/> for a query whose result holds the data its query
    /// function returns.
    /// </summary>
    /// <param name="QueryKey">Names the cache entry. Queries presenting equal keys share one entry.</param>
    /// <param name="QueryFn">Fetches the entry's data; see <see cref="QueryOptions{TQueryFnData, TData}.QueryFn"/>.</param>
    public sealed record QueryOptions<T>(QueryKey QueryKey, QueryFunction<T> QueryFn) : QueryOptions<T, T>(QueryKey, QueryFn)
    {
        /// <summary>Options whose query function takes a <see cref="QueryFunctionContext"/>.</summary>
        /// <param name="queryKey">Names the cache entry. Queries presenting equal keys share one entry.</param>
        /// <param name="queryFn">Fetches the entry's data.</param>
        public QueryOptions(QueryKey queryKey, Func<QueryFunctionContext, VelvetTask<T>> queryFn)
            : this(queryKey, QueryFunction.Of(queryFn))
        {
        }

        /// <summary>Options whose query function takes the token.</summary>
        /// <param name="queryKey">Names the cache entry. Queries presenting equal keys share one entry.</param>
        /// <param name="queryFn">Fetches the entry's data.</param>
        public QueryOptions(QueryKey queryKey, Func<CancellationToken, VelvetTask<T>> queryFn)
            : this(queryKey, QueryFunction.Of(queryFn))
        {
        }

        /// <summary>Options whose query function takes no token; see the constructor of
        /// <see cref="QueryOptions{TQueryFnData, TData}"/> taking one.</summary>
        /// <param name="queryKey">Names the cache entry. Queries presenting equal keys share one entry.</param>
        /// <param name="queryFn">Fetches the entry's data.</param>
        public QueryOptions(QueryKey queryKey, Func<VelvetTask<T>> queryFn)
            : this(queryKey, QueryFunction.Of(queryFn))
        {
        }
    }

    /// <summary>Makes a <see cref="QueryFunction{T}"/> of a function of any of the three forms.</summary>
    public static class QueryFunction
    {
        /// <summary>A function taking the <see cref="QueryFunctionContext"/>.</summary>
        public static QueryFunction<T> Of<T>(Func<QueryFunctionContext, VelvetTask<T>> queryFn) => new(queryFn);

        /// <summary>A function taking the token, which counts as reading it.</summary>
        public static QueryFunction<T> Of<T>(Func<CancellationToken, VelvetTask<T>> queryFn) => new(queryFn);

        /// <summary>A function taking nothing, which never reads the token.</summary>
        public static QueryFunction<T> Of<T>(Func<VelvetTask<T>> queryFn) => new(queryFn);
    }

    /// <summary>
    /// A query function in one of its three forms — taking the <see cref="QueryFunctionContext"/>, the token, or
    /// nothing — as <see cref="QueryOptions{TQueryFnData, TData}.QueryFn"/> holds it. Two compare equal when they
    /// hold equal delegates, so options rebuilt each render around one function compare equal. Each of the three
    /// delegate types converts to it implicitly.
    /// </summary>
    /// <typeparam name="T">What the function returns.</typeparam>
    public readonly struct QueryFunction<T> : IEquatable<QueryFunction<T>>
    {
        private readonly Delegate? _fn;

        internal QueryFunction(Delegate? fn) => _fn = fn;

        /// <summary>Whether a function is held.</summary>
        public bool HasValue => _fn != null;

        /// <summary>The function as it was given: a <c>Func</c> of the context, of the token, or of nothing.</summary>
        public Delegate? Delegate => _fn;

        // The token-taking form reads the context's token as it starts, which is the read that marks the request.
        internal VelvetTask<T> Invoke(QueryFunctionContext context) => _fn switch
        {
            Func<QueryFunctionContext, VelvetTask<T>> contextual => contextual(context),
            Func<CancellationToken, VelvetTask<T>> tokenTaking => tokenTaking(context.CancellationToken),
            Func<VelvetTask<T>> tokenless => tokenless(),
            _ => throw new InvalidOperationException("The query has no QueryFn."),
        };

        /// <inheritdoc/>
        public bool Equals(QueryFunction<T> other) => Equals(_fn, other._fn);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is QueryFunction<T> && Equals((QueryFunction<T>)obj);

        /// <inheritdoc/>
        public override int GetHashCode() => _fn?.GetHashCode() ?? 0;

        /// <summary>Wraps a function taking the context.</summary>
        public static implicit operator QueryFunction<T>(Func<QueryFunctionContext, VelvetTask<T>> queryFn) => new(queryFn);

        /// <summary>Wraps a function taking the token.</summary>
        public static implicit operator QueryFunction<T>(Func<CancellationToken, VelvetTask<T>> queryFn) => new(queryFn);

        /// <summary>Wraps a function taking nothing.</summary>
        public static implicit operator QueryFunction<T>(Func<VelvetTask<T>> queryFn) => new(queryFn);
    }

    /// <summary>
    /// What <see cref="Hooks.UseQuery{TQueryFnData, TData}"/> returns: the entry's state as this render reads it,
    /// TanStack Query's <c>UseQueryResult</c>. Reading a property marks it as one the component depends on,
    /// which is what <see cref="QueryOptionsBase{TQueryFnData, TData}.NotifyOnChangeProps"/> leaves to decide when the
    /// component re-renders.
    /// </summary>
    public class QueryResult<T>
    {
        private readonly QuerySnapshot<T> _snapshot;
        private readonly IQueryResultSource _observer;

        internal QueryResult(QuerySnapshot<T> snapshot, IQueryResultSource observer)
        {
            _snapshot = snapshot;
            _observer = observer;
        }

        private protected QuerySnapshot<T> Snapshot => _snapshot;

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
        /// <summary>True while a request for the entry is in flight and not paused, retries waiting out their
        /// delay included, or is about to start because this component is mounting over, or has just changed
        /// its key to, an entry with no data or stale data.</summary>
        public bool IsFetching => Read(QueryProperties.IsFetching, _snapshot.FetchStatus == QueryFetchStatus.Fetching);
        /// <summary>True while a request for the entry waits, for the connection or, between two attempts,
        /// for the application to be visible, TanStack Query's <c>isPaused</c>. A paused query is not
        /// <see cref="IsFetching"/>.</summary>
        public bool IsPaused => Read(QueryProperties.IsPaused, _snapshot.FetchStatus == QueryFetchStatus.Paused);
        /// <summary>Whether a request for the entry is in flight and whether it waits, TanStack Query's
        /// <c>fetchStatus</c>.</summary>
        public QueryFetchStatus FetchStatus => Read(QueryProperties.FetchStatus, _snapshot.FetchStatus);
        /// <summary>True when the query is enabled and the entry has no data, or holds data that has been
        /// invalidated or is at least as old as the query's stale time and is not
        /// <see cref="QueryStaleTime.Static"/>.</summary>
        public bool IsStale => Read(QueryProperties.IsStale, _snapshot.IsStale);
        /// <summary>How many times the request in flight or the last one has failed, TanStack Query's
        /// <c>failureCount</c>: zero once a request lands, and one more than the retries made when the last
        /// request failed for good.</summary>
        public int FailureCount => Read(QueryProperties.FailureCount, _snapshot.FailureCount);
        /// <summary>The exception of the latest failure of the request in flight or the last one, TanStack
        /// Query's <c>failureReason</c>; null once a request lands.</summary>
        public Exception? FailureReason => Read(QueryProperties.FailureReason, _snapshot.FailureReason);
        /// <summary>True when <see cref="Data"/> is
        /// <see cref="QueryOptionsBase{TQueryFnData, TData}.PlaceholderData"/>'s rather than the entry's, TanStack
        /// Query's <c>isPlaceholderData</c>.</summary>
        public bool IsPlaceholderData => Read(QueryProperties.IsPlaceholderData, _snapshot.IsPlaceholderData);
        /// <summary>True while a request is in flight and running over a result that is not
        /// <see cref="QueryStatus.Pending"/>, TanStack Query's <c>isRefetching</c>; an infinite query's request for its
        /// next or previous page is not one.</summary>
        public bool IsRefetching => Read(QueryProperties.IsRefetching, _snapshot.IsRefetching);
        /// <summary>True when the result is <see cref="QueryStatus.Error"/> over an entry that holds data, TanStack
        /// Query's <c>isRefetchError</c>; an infinite query's failure to fetch its next or previous page is not
        /// one.</summary>
        public bool IsRefetchError => Read(QueryProperties.IsRefetchError, _snapshot.IsRefetchError);

        /// <summary>
        /// Fetches the entry again, TanStack Query's <c>refetch()</c>, whether or not the query is enabled: a
        /// request in flight over data the entry holds is cancelled and started over, and one for an entry
        /// still waiting on its first result is joined. Does nothing before the component's effects have run,
        /// and after it unmounts.
        /// </summary>
        public void Refetch() => _observer.Refetch();

        private protected TValue Read<TValue>(QueryProperties property, TValue value)
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
        internal QueryFetchStatus FetchStatus { get; init; }
        internal bool IsStale { get; init; }
        internal int FailureCount { get; init; }
        internal Exception? FailureReason { get; init; }
        internal bool IsPlaceholderData { get; init; }
        // Whether the entry holds data, where v5 reads whether its dataUpdatedAt is other than zero.
        internal bool EntryHasData { get; init; }

        // TanStack's isRefetching and isRefetchError, with an infinite query's paging requests taken out as its
        // InfiniteQueryObserver takes them out; any other query has no FetchDirection.
        internal bool IsRefetching => FetchStatus == QueryFetchStatus.Fetching && Status != QueryStatus.Pending && FetchDirection == null;
        internal bool IsRefetchError => Status == QueryStatus.Error && EntryHasData && FetchDirection == null;
        // An infinite query's: whether its paging names a page either side, and the end the entry's latest request
        // was asked for, TanStack's fetchMeta.fetchMore.direction.
        internal bool HasNextPage { get; init; }
        internal bool HasPreviousPage { get; init; }
        internal FetchDirection? FetchDirection { get; init; }

        internal QueryProperties DifferencesFrom(in QuerySnapshot<T> other)
        {
            var changed = QueryProperties.None;
            if (Status != other.Status) changed |= QueryProperties.Status;
            if ((Status == QueryStatus.Pending) != (other.Status == QueryStatus.Pending)) changed |= QueryProperties.IsPending;
            if ((Status == QueryStatus.Success) != (other.Status == QueryStatus.Success)) changed |= QueryProperties.IsSuccess;
            if ((Status == QueryStatus.Error) != (other.Status == QueryStatus.Error)) changed |= QueryProperties.IsError;
            if (!QueryData.Same(Data, other.Data)) changed |= QueryProperties.Data;
            if (!ReferenceEquals(Error, other.Error)) changed |= QueryProperties.Error;
            if (FetchStatus != other.FetchStatus) changed |= QueryProperties.FetchStatus;
            if ((FetchStatus == QueryFetchStatus.Fetching) != (other.FetchStatus == QueryFetchStatus.Fetching)) changed |= QueryProperties.IsFetching;
            if ((FetchStatus == QueryFetchStatus.Paused) != (other.FetchStatus == QueryFetchStatus.Paused)) changed |= QueryProperties.IsPaused;
            if (IsStale != other.IsStale) changed |= QueryProperties.IsStale;
            if (FailureCount != other.FailureCount) changed |= QueryProperties.FailureCount;
            if (!ReferenceEquals(FailureReason, other.FailureReason)) changed |= QueryProperties.FailureReason;
            if (IsPlaceholderData != other.IsPlaceholderData) changed |= QueryProperties.IsPlaceholderData;
            if (IsRefetching != other.IsRefetching) changed |= QueryProperties.IsRefetching;
            if (IsRefetchError != other.IsRefetchError) changed |= QueryProperties.IsRefetchError;
            return changed | PagingDifferencesFrom(other);
        }

        private QueryProperties PagingDifferencesFrom(in QuerySnapshot<T> other)
        {
            var changed = QueryProperties.None;
            if (HasNextPage != other.HasNextPage) changed |= QueryProperties.HasNextPage;
            if (HasPreviousPage != other.HasPreviousPage) changed |= QueryProperties.HasPreviousPage;
            if (IsFetchingTowards(Velvet.FetchDirection.Forward) != other.IsFetchingTowards(Velvet.FetchDirection.Forward)) changed |= QueryProperties.IsFetchingNextPage;
            if (IsFetchingTowards(Velvet.FetchDirection.Backward) != other.IsFetchingTowards(Velvet.FetchDirection.Backward)) changed |= QueryProperties.IsFetchingPreviousPage;
            if (FailedTowards(Velvet.FetchDirection.Forward) != other.FailedTowards(Velvet.FetchDirection.Forward)) changed |= QueryProperties.IsFetchNextPageError;
            if (FailedTowards(Velvet.FetchDirection.Backward) != other.FailedTowards(Velvet.FetchDirection.Backward)) changed |= QueryProperties.IsFetchPreviousPageError;
            return changed;
        }

        // TanStack's isFetchingNextPage and isFetchingPreviousPage.
        internal bool IsFetchingTowards(FetchDirection direction) => FetchStatus == QueryFetchStatus.Fetching && FetchDirection == direction;

        // TanStack's isFetchNextPageError and isFetchPreviousPageError.
        internal bool FailedTowards(FetchDirection direction) => Status == QueryStatus.Error && FetchDirection == direction;
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
    // its commit hands the observer. A suspending query is v5's useSuspenseQuery: always enabled, with no
    // placeholder data, throwing the error of an entry with no data, and with its stale and garbage-collection
    // times raised to a second at least, as v5's ensureSuspenseTimers raises them.
    internal readonly struct QuerySettings<TQueryFnData, TData>
    {
        internal QuerySettings(QueryOptionsBase<TQueryFnData, TData> options, QueryClient client, bool suspense)
        {
            Options = options;
            Suspense = suspense;
            var gcTime = QueryClient.RequireNonNegative(
                options.GcTime ?? client.DefaultGcTime, nameof(QueryOptions<TQueryFnData, TData>.GcTime));
            GcTime = suspense && gcTime < QueryStaleRule.MinSuspenseTime ? QueryStaleRule.MinSuspenseTime : gcTime;
            Placeholder = suspense ? null : options.PlaceholderData;
            ThrowOnError = QueryThrowRule.Resolve(options.ThrowOnError, options.ThrowOnErrorFn,
                client.DefaultThrowOnError, client.DefaultThrowOnErrorFn);
            Rules = new QueryRules
            {
                Enabled = suspense ? QueryBooleanRule.True : new QueryBooleanRule(options.Enabled, options.EnabledFn),
                RetryOnMount = new QueryBooleanRule(options.RetryOnMount, options.RetryOnMountFn),
                Suspense = suspense,
                StaleTime = QueryStaleRule.Resolve(options.StaleTime, options.StaleTimeFn, client.DefaultStaleTime,
                    client.DefaultStaleTimeFn, suspense),
                RefetchOnMount = QueryRefetchRule.Resolve(options.RefetchOnMount, options.RefetchOnMountFn,
                    client.DefaultRefetchOnMount, client.DefaultRefetchOnMountFn),
                RefetchOnWindowFocus = QueryRefetchRule.Resolve(options.RefetchOnWindowFocus, options.RefetchOnWindowFocusFn,
                    client.DefaultRefetchOnWindowFocus, client.DefaultRefetchOnWindowFocusFn),
                RefetchOnReconnect = QueryRefetchRule.Resolve(options.RefetchOnReconnect, options.RefetchOnReconnectFn,
                    client.DefaultRefetchOnReconnect, client.DefaultRefetchOnReconnectFn),
            };
            RefetchInterval = QueryClient.ValidInterval(options.RefetchInterval);
            NetworkMode = options.NetworkMode ?? client.DefaultNetworkMode;
            KeyHashFn = options.QueryKeyHashFn ?? client.DefaultQueryKeyHashFn;
            KeyId = QueryClient.Identify(options.QueryKey, KeyHashFn);
        }

        private QuerySettings(in QuerySettings<TQueryFnData, TData> source, QueryRules rules)
        {
            this = source;
            Rules = rules;
        }

        internal QueryOptionsBase<TQueryFnData, TData> Options { get; }
        internal bool Suspense { get; }
        internal QueryRules Rules { get; }
        internal QueryThrowRule ThrowOnError { get; }
        internal TimeSpan GcTime { get; }
        internal Func<TQueryFnData?, Query?, QueryPlaceholder<TQueryFnData>>? Placeholder { get; }
        internal TimeSpan? RefetchInterval { get; }
        internal NetworkMode NetworkMode { get; }
        internal Func<QueryKey, string>? KeyHashFn { get; }
        // What names the entry in the client's cache; see QueryClient.Identify.
        internal QueryKey KeyId { get; }
        // The paging of an infinite query; null for any other.
        internal IInfiniteQueryOptions<TQueryFnData>? Paging => Options as IInfiniteQueryOptions<TQueryFnData>;
        internal Func<TQueryFnData, TData>? Select => Options.Select;

        // TanStack's getOptimisticResult, which builds the entry a render reads when there is none, with the
        // render's options.
        internal QueryEntry<TQueryFnData> ReadEntry(QueryClient client)
            => client.PeekById<TQueryFnData>(KeyId)
                ?? client.Ensure(Options.QueryKey, KeyHashFn, GcTime, new QueryFetchOptions<TQueryFnData>().Assign(Options, client),
                    heldForRender: true);

        // TanStack's ensurePreventErrorBoundaryRetry: a query that throws its errors does not load an entry that
        // failed again when it mounts, until the boundary has been reset.
        internal QuerySettings<TQueryFnData, TData> PreventingErrorBoundaryRetry(
            QueryEntry<TQueryFnData> entry, QueryErrorResetBoundary boundary)
        {
            var throws = Suspense || ThrowOnError.ThrowsOnMount(entry);
            return throws && !boundary.IsReset ? new QuerySettings<TQueryFnData, TData>(this, Rules.WithoutRetryOnMount()) : this;
        }

        // TanStack's getHasError. A suspending query's throwOnError is useSuspenseQuery's, which throws when the
        // entry has no data.
        internal bool HasError(in QuerySnapshot<TData> result, QueryEntry<TQueryFnData> entry, QueryErrorResetBoundary boundary)
        {
            if (result.Status != QueryStatus.Error || boundary.IsReset || result.FetchStatus == QueryFetchStatus.Fetching)
            {
                return false;
            }
            if (Suspense) return !result.HasData || !entry.HasData;
            return ThrowOnError.Throws(result.Error!, entry);
        }
    }

    // A query's throwOnError: the query's function, else its value, else the client's function, else the
    // client's value, the order QueryRefetchRule gives the refetch modes. Asked only during a render, so a throw
    // from the function throws from the render, as v5's does.
    internal readonly struct QueryThrowRule
    {
        private readonly bool _value;
        private readonly Func<Exception, Query, bool>? _fn;

        private QueryThrowRule(bool value, Func<Exception, Query, bool>? fn)
        {
            _value = value;
            _fn = fn;
        }

        internal static QueryThrowRule Resolve(
            bool? queryValue, Func<Exception, Query, bool>? queryFn,
            bool clientValue, Func<Exception, Query, bool>? clientFn)
        {
            if (queryFn != null) return new QueryThrowRule(default, queryFn);
            if (queryValue != null) return new QueryThrowRule(queryValue.Value, null);
            if (clientFn != null) return new QueryThrowRule(default, clientFn);
            return new QueryThrowRule(clientValue, null);
        }

        internal bool Throws<T>(Exception error, QueryEntry<T> entry)
            => _fn != null ? _fn(error, entry.Query) : _value;

        // ensurePreventErrorBoundaryRetry asks a function only over an entry in error, and otherwise counts it as
        // true, as v5 reads the function itself as truthy.
        internal bool ThrowsOnMount<T>(QueryEntry<T> entry)
        {
            if (_fn == null) return _value;
            return entry.Status != QueryStatus.Error || _fn(entry.Error!, entry.Query);
        }
    }

    // v5's QueryBooleanOption, enabled and retryOnMount: a value, or a function of the entry in its place.
    internal readonly struct QueryBooleanRule
    {
        internal static readonly QueryBooleanRule True = new(true, null);

        private readonly bool _value;
        private readonly Func<Query, bool>? _fn;

        internal QueryBooleanRule(bool value, Func<Query, bool>? fn)
        {
            _value = value;
            _fn = fn;
        }

        // A function that throws outside a render counts as false, the answer that starts nothing.
        internal bool Read(QueryEntry entry, bool rethrow)
        {
            var fn = _fn;
            if (fn == null) return _value;
            try
            {
                return fn(entry.Query);
            }
            catch (Exception failure) when (!rethrow)
            {
                QueryFunctionFailures.LogOnce(fn, failure);
                return false;
            }
        }
    }

    // Read at commits, at updates of the result and at signals, so a function that always throws would log over
    // and over: its failure is logged once for each function instance.
    internal static class QueryFunctionFailures
    {
        private static readonly ConditionalWeakTable<Delegate, Delegate> s_logged = new();

        internal static void LogOnce(Delegate fn, Exception failure)
        {
            if (s_logged.TryGetValue(fn, out _)) return;
            s_logged.Add(fn, fn);
            FiberLogger.LogException(nameof(QueryClient), failure);
        }
    }

    // What decides a query's fetches from its entry: v5's enabled, staleTime, refetchOnMount, refetchOnWindowFocus,
    // refetchOnReconnect, retryOnMount and suspense, read by a render and by the observer its commit updates.
    internal readonly struct QueryRules
    {
        private QueryRules(in QueryRules source, QueryBooleanRule retryOnMount)
        {
            this = source;
            RetryOnMount = retryOnMount;
        }

        internal QueryBooleanRule Enabled { get; init; }
        internal QueryBooleanRule RetryOnMount { get; init; }
        internal bool Suspense { get; init; }
        internal QueryStaleRule StaleTime { get; init; }
        internal QueryRefetchRule RefetchOnMount { get; init; }
        internal QueryRefetchRule RefetchOnWindowFocus { get; init; }
        internal QueryRefetchRule RefetchOnReconnect { get; init; }

        // ensurePreventErrorBoundaryRetry writes retryOnMount: false over whatever the options gave.
        internal QueryRules WithoutRetryOnMount() => new(this, new QueryBooleanRule(false, null));

        // TanStack's resolveEnabled(options.enabled, query) !== false.
        internal bool IsEnabled(QueryEntry entry, bool rethrow) => Enabled.Read(entry, rethrow);

        // TanStack's isStale(query, options).
        internal bool IsStale<T>(QueryEntry<T> entry, bool rethrow)
            => IsEnabled(entry, rethrow) && entry.IsStaleFor(StaleTime.Read(entry, rethrow));

        // TanStack's shouldFetchOn: an enabled query whose stale time is not static fetches under Always, and under
        // IfStale over stale data. The function forms are asked only of such a query.
        internal bool ShouldFetchOn<T>(QueryRefetchRule rule, QueryEntry<T> entry, bool rethrow)
        {
            if (!IsEnabled(entry, rethrow)) return false;
            var staleTime = StaleTime.Read(entry, rethrow);
            if (staleTime.IsStatic) return false;
            var mode = rule.Read(entry, rethrow);
            return mode == QueryRefetchMode.Always || (mode == QueryRefetchMode.IfStale && entry.IsStaleFor(staleTime));
        }

        // TanStack's shouldFetchOnMount: an entry with no data is loaded unless it failed with retryOnMount off,
        // and one with data is fetched as refetchOnMount says.
        internal bool ShouldFetchOnMount<T>(QueryEntry<T> entry, bool rethrow)
        {
            if (entry.HasData) return ShouldFetchOn(RefetchOnMount, entry, rethrow);
            return IsEnabled(entry, rethrow) && (entry.Status != QueryStatus.Error || RetryOnMount.Read(entry, rethrow));
        }

        // TanStack's shouldFetchOptionally, for a query whose entry changed or that was turned on.
        internal bool ShouldFetchOptionally<T>(QueryEntry<T> entry, bool rethrow)
            => (!Suspense || entry.Status != QueryStatus.Error) && IsStale(entry, rethrow);
    }

    // A query's stale time: the query's function, else its value, else the client's function, else the client's
    // value, the order QueryRefetchRule gives the refetch modes.
    internal readonly struct QueryStaleRule
    {
        internal static readonly TimeSpan MinSuspenseTime = TimeSpan.FromSeconds(1);
        private readonly QueryStaleTime _value;
        private readonly Func<Query, QueryStaleTime>? _fn;
        private readonly bool _suspense;

        private QueryStaleRule(QueryStaleTime value, Func<Query, QueryStaleTime>? fn, bool suspense)
        {
            _value = value;
            _fn = fn;
            _suspense = suspense;
        }

        internal static QueryStaleRule Resolve(
            QueryStaleTime? queryValue, Func<Query, QueryStaleTime>? queryFn,
            QueryStaleTime clientValue, Func<Query, QueryStaleTime>? clientFn, bool suspense)
        {
            if (queryFn != null) return new QueryStaleRule(default, queryFn, suspense);
            if (queryValue != null) return new QueryStaleRule(Validate(queryValue.Value), null, suspense);
            if (clientFn != null) return new QueryStaleRule(default, clientFn, suspense);
            return new QueryStaleRule(clientValue, null, suspense);
        }

        // A render and the client commands that return data, FetchQuery and EnsureQueryData, rethrow, as v5 throws
        // from each; anywhere else a throw is contained, so that one function cannot cut short a commit's effects,
        // a signal, a frame's wait or a command that only starts fetches.
        internal QueryStaleTime Read(QueryEntry entry, bool rethrow)
        {
            var value = _fn == null ? _value : Call(entry, rethrow);
            return _suspense && !value.IsStatic && value.Time < MinSuspenseTime ? MinSuspenseTime : value;
        }

        private QueryStaleTime Call(QueryEntry entry, bool rethrow)
        {
            var fn = _fn!;
            try
            {
                return Validate(fn(entry.Query));
            }
            catch (Exception failure) when (!rethrow)
            {
                QueryFunctionFailures.LogOnce(fn, failure);
                return TimeSpan.MaxValue;
            }
        }

        internal static QueryStaleTime Validate(QueryStaleTime value)
            => value.IsStatic ? value : QueryClient.RequireNonNegative(value.Time, nameof(QueryOptions<object>.StaleTime));
    }

    // What decides whether a signal refetches a query: the mode the query sets, else the client's, and a function
    // in place of a mode where either form was given, the query's before the client's.
    internal readonly struct QueryRefetchRule
    {
        private readonly QueryRefetchMode _mode;
        private readonly Func<Query, QueryRefetchMode>? _fn;

        private QueryRefetchRule(QueryRefetchMode mode, Func<Query, QueryRefetchMode>? fn)
        {
            _mode = mode;
            _fn = fn;
        }

        internal static QueryRefetchRule Resolve(
            QueryRefetchMode? queryMode, Func<Query, QueryRefetchMode>? queryFn,
            QueryRefetchMode clientMode, Func<Query, QueryRefetchMode>? clientFn)
        {
            if (queryFn != null) return new QueryRefetchRule(default, queryFn);
            if (queryMode != null) return new QueryRefetchRule(queryMode.Value, null);
            if (clientFn != null) return new QueryRefetchRule(default, clientFn);
            return new QueryRefetchRule(clientMode, null);
        }

        internal QueryRefetchMode Read(QueryEntry entry, bool rethrow)
        {
            if (_fn == null) return _mode;
            try
            {
                return _fn(entry.Query);
            }
            catch (Exception failure) when (!rethrow)
            {
                FiberLogger.LogException(nameof(QueryClient), failure);
                return QueryRefetchMode.Never;
            }
        }
    }

    // The options a request runs with. An observer keeps one and rewrites it each commit. A request reads its
    // query function and retries when it starts, and the structural sharing of the options its entry holds when
    // it lands.
    internal sealed class QueryFetchOptions<T>
    {
        internal QueryFunction<T> QueryFn { get; private set; }
        internal RetryRule Retry { get; private set; }
        internal Func<int, Exception, TimeSpan> RetryDelay { get; private set; } = null!;
        internal Func<object?, object, object>? StructuralSharing { get; private set; }
        internal NetworkMode NetworkMode { get; private set; }
        internal Func<QueryKey, string>? KeyHashFn { get; private set; }
        internal Func<T>? InitialData { get; private set; }
        internal TimeSpan? InitialDataUpdatedAt { get; private set; }
        internal Func<TimeSpan?>? InitialDataUpdatedAtFn { get; private set; }
        internal IReadOnlyDictionary<string, object?>? Meta { get; private set; }
        // TanStack's options.behavior: what a request runs in place of QueryFn, an infinite query's paging.
        internal IQueryBehavior<T>? Behavior { get; private set; }

        internal QueryFetchOptions<T> Assign<TData>(QueryOptionsBase<T, TData> options, QueryClient client)
        {
            Meta = options.Meta;
            Behavior = (options as IInfiniteQueryOptions<T>)?.CreateBehavior(pages: null);
            KeyHashFn = options.QueryKeyHashFn ?? client.DefaultQueryKeyHashFn;
            NetworkMode = options.NetworkMode ?? client.DefaultNetworkMode;
            QueryFn = options.Function;

            // v5's retryer reads a retry nobody set as three.
            Retry = options.Retry ?? client.DefaultRetry ?? 3;
            RetryDelay = options.RetryDelay ?? client.DefaultRetryDelay;
            StructuralSharing = options.StructuralSharing;
            InitialData = options.InitialData;
            InitialDataUpdatedAt = options.InitialDataUpdatedAt;
            InitialDataUpdatedAtFn = options.InitialDataUpdatedAtFn;
            return this;
        }

        internal void SetRetry(RetryRule retry) => Retry = retry;

        internal void SetBehavior(IQueryBehavior<T>? behavior) => Behavior = behavior;

        internal QueryFetchOptions<T> Copy() => (QueryFetchOptions<T>)MemberwiseClone();
    }

    // What an entry knows of the observers reading it, whatever the type they read its data as.
    internal abstract class QueryEntryObserver<T> : QueryObserver
    {
        internal QueryFetchOptions<T> FetchOptions { get; } = new();
        internal QueryRules Rules { get; private protected set; }
        internal bool IsEnabled(QueryEntry entry) => Rules.IsEnabled(entry, rethrow: false);

        // TanStack's onQueryUpdate: the entry's state changed.
        internal abstract void OnQueryUpdate();

        // The entry this observer reads was removed, which no snapshot carries.
        internal abstract void OnRemoved();
    }

    // The per-component half of UseQuery, TanStack's QueryObserver: the options of the latest committed render,
    // and the entry that render subscribed to.
    internal sealed class QueryObserver<TQueryFnData, TData> : QueryEntryObserver<TQueryFnData>, IQueryResultSource, IInfiniteResultSource<TData>
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
        private CancellationTokenSource? _intervalWait;
        // TanStack's #currentRefetchInterval: what the latest restart of the interval computed.
        private TimeSpan? _currentInterval;
        private Func<Query, TimeSpan?>? _failedIntervalFn;
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
        private Func<TQueryFnData?, Query?, QueryPlaceholder<TQueryFnData>>? _currentPlaceholder;
        private QueryEntry<TQueryFnData>? _lastWithData;

        public void Refetch() => Entry?.Fetch(FetchOptions, cancelRefetch: true);

        public override Query? GetCurrentQuery() => Entry?.Query;

        // TanStack's InfiniteQueryObserver fetchNextPage and fetchPreviousPage: a fetch with fetchMore's direction,
        // cancelling a request in flight unless asked not to, and the result once it settles; a failure is swallowed
        // unless throwOnError, as v5's fetch catches it.
        public async VelvetTask<InfiniteQueryResult<TData>> FetchPage(FetchDirection direction, bool cancelRefetch, bool throwOnError)
        {
            var entry = Entry;
            if (entry != null)
            {
                try
                {
                    await entry.FetchAndWait(FetchOptions, cancelRefetch, direction);
                }
                catch (Exception) when (!throwOnError)
                {
                }
            }
            return new InfiniteQueryResult<TData>(_current, this, this);
        }

        public void Track(QueryProperties property) => _tracked |= property;

        // Runs after every commit rather than on a key dependency: QueryKey compares by content, and an
        // effect's dependency array compares a reference type by instance, so a key rebuilt each render
        // would resubscribe — and refetch a stale entry — on every commit.
        // The options are taken here rather than at render, as v5's setOptions runs in an effect: until this
        // commit's subscription moves to the new key, a refetch or an invalidation reaching the old entry has
        // to run the old entry's query function.
        internal void Sync(QueryClient client, in QuerySettings<TQueryFnData, TData> settings)
        {
            var previousRules = Rules;
            var previousOptions = _settings.Options;
            var options = settings.Options;
            var key = options.QueryKey;
            var current = Entry;
            var staying = current != null && !current.IsRemoved && ReferenceEquals(current.Client, client) && current.Id.Equals(settings.KeyId);
            // Leaving before the options below are rewritten in place lets the entry left keep the ones it was
            // handed: QueryEntry.Unsubscribe copies them.
            if (!staying) Unsubscribe();
            _settings = settings;
            Rules = settings.Rules;
            FetchOptions.Assign(options, client);
            if (staying)
            {
#if UNITY_EDITOR
                _reprintedKeyCommits = 0;
#endif
                // v5's setOptions hands the entry this commit's options at every commit.
                current!.SetOptions(FetchOptions);
                AnnounceOptions(current, previousOptions, options);
                // v5 resolves the previous options' enabled against the entry as it stands now.
                var wasEnabled = previousRules.IsEnabled(current!, rethrow: false);
                // TanStack's shouldFetchOptionally: a query turned on over stale data fetches it.
                if (!wasEnabled && Rules.ShouldFetchOptionally(current!, rethrow: false))
                {
                    current!.Fetch(FetchOptions, cancelRefetch: false);
                }
                // Over the same entry, v5's setOptions restarts the interval only when the query was turned on or
                // off or the interval changed, so a re-render does not push the next refetch back.
                var nextInterval = ComputeInterval(current!);
                if (wasEnabled != IsEnabled(current!) || nextInterval != _currentInterval) RestartInterval(nextInterval);
                ScheduleStale();
                return;
            }
#if UNITY_EDITOR
            WarnOnReprintedKey(current, key);
#endif

            var entry = client.Build<TQueryFnData>(key, settings.KeyId, settings.KeyHashFn, settings.GcTime);
            Entry = entry;
            entry.SetOptions(FetchOptions);
            entry.Subscribe(this);
            AnnounceOptions(entry, previousOptions, options);
            // TanStack's onSubscribe asks shouldFetchOnMount at a component's first subscription, and its setOptions
            // asks shouldFetchOptionally once the subscription moves from one entry to another.
            var fetches = current == null
                ? Rules.ShouldFetchOnMount(entry, rethrow: false)
                : Rules.ShouldFetchOptionally(entry, rethrow: false);
            if (fetches) entry.Fetch(FetchOptions, cancelRefetch: false);
            // A change that landed between the render and this subscription was delivered to no one here.
            Update();
            RestartInterval();
        }

        // TanStack's observerOptionsUpdated, announced over the entry this commit reads once the options differ from
        // those the previous commit handed; a first commit has none to differ from.
        private void AnnounceOptions(
            QueryEntry<TQueryFnData> entry, QueryOptionsBase<TQueryFnData, TData>? previous, QueryOptionsBase<TQueryFnData, TData> options)
        {
            if (previous == null || Equals(previous, options)) return;
            entry.Client.QueryCache.Notify(new QueryCacheEvent(QueryCacheEventType.ObserverOptionsUpdated, entry.Query, observer: this));
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
                "the same, so each commit reads a new entry and fetches it. A class that defines no equality compares " +
                "by reference; make it a record, or build the key from its fields (react-migration.md §1-2b).");
        }
#endif

        private void Unsubscribe()
        {
            CancelStaleWait();
            CancelInterval();
            var entry = Entry;
            if (entry == null) return;
            Entry = null;
            entry.Unsubscribe(this);
        }

        internal override void OnRemoved()
        {
            CancelStaleWait();
            // MUTANT_SURVIVES(equivalent, line removed): a removed entry fetches nothing, and the re-render this asks for moves the subscription, whose Unsubscribe cancels the wait.
            CancelInterval();
            OnChange();
        }

        // TanStack's onQueryUpdate, which updates the result and restarts the timers: the interval is measured
        // from the entry's latest change.
        internal override void OnQueryUpdate()
        {
            try
            {
                Update();
            }
            finally
            {
                RestartInterval();
            }
        }

        private void RestartInterval() => RestartInterval(Entry != null ? ComputeInterval(Entry) : null);

        // TanStack's #computeRefetchInterval.
        private TimeSpan? ComputeInterval(QueryEntry<TQueryFnData> entry)
        {
            var fn = _settings.Options.RefetchIntervalFn;
            if (fn == null) return _settings.RefetchInterval;
            try
            {
                return QueryClient.ValidInterval(fn(entry.Query));
            }
            catch (Exception failure)
            {
                // Called at every commit, so a function that always throws would log each render.
                if (!ReferenceEquals(_failedIntervalFn, fn))
                {
                    _failedIntervalFn = fn;
                    FiberLogger.LogException(nameof(QueryClient), failure);
                }
                return null;
            }
        }

        // TanStack's #updateRefetchInterval. The interval is polled once a frame on the client's clock, as a
        // retry's wait is, and fires again an interval after it last fired until something restarts it.
        private void RestartInterval(TimeSpan? every)
        {
            CancelInterval();
            _currentInterval = every;
            var entry = Entry;
            // MUTANT_SURVIVES(equivalent, clause removed): every caller has subscribed, and an entry notifies only its own observers, whose leaving runs from effects a hook setter queues rather than runs.
            if (entry == null || !IsEnabled(entry) || every == null) return;
            var wait = new CancellationTokenSource();
            _intervalWait = wait;
            RunInterval(entry, wait, every.Value).Forget();
        }

        private async VelvetTask RunInterval(QueryEntry<TQueryFnData> entry, CancellationTokenSource wait, TimeSpan every)
        {
            var client = entry.Client;
            var start = client.Now;
            while (true)
            {
                do
                {
                    await VelvetTask.Yield();
                }
                while (!wait.IsCancellationRequested && client.Now - start < every);
                if (wait.IsCancellationRequested) return;
                start = client.Now;
                if (_settings.Options.RefetchIntervalInBackground || QueryClientSignals.IsVisible())
                {
                    entry.Fetch(FetchOptions, cancelRefetch: false);
                }
            }
        }

        private void CancelInterval() => Cancel(ref _intervalWait);

        private static void Cancel(ref CancellationTokenSource? wait)
        {
            var pending = wait;
            if (pending == null) return;
            wait = null;
            pending.Cancel();
            // MUTANT_SURVIVES(equivalent, line removed): the source carries no timer and its token went only to the observer's own loop, so an undisposed one holds nothing.
            pending.Dispose();
        }

        // TanStack's updateResult: the entry changed, and the component re-renders if a property it has read
        // is among what differs from the reading its latest render took, or if it has read none.
        // Runs only while subscribed: Sync subscribes before it calls it, an entry tells only the observers it
        // holds, and Unsubscribe cancels the stale wait as it clears Entry.
        private void Update()
        {
            var entry = Entry!;
            var next = CreateResult(entry, _settings, inRender: false);
            if (entry.HasData) _lastWithData = entry;
            var changed = _hasCurrent ? _current.DifferencesFrom(next) : QueryProperties.All;
            if (changed != QueryProperties.None)
            {
                var first = !_hasCurrent;
                _current = next;
                _hasCurrent = true;
                if (first || Notifies(changed)) OnChange();
                // Whatever the component reads, as v5 tells the cache of every result that changed.
                entry.Client.QueryCache.Notify(new QueryCacheEvent(QueryCacheEventType.ObserverResultsUpdated, entry.Query));
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
            var staleTime = entry == null || !IsEnabled(entry) ? QueryStaleTime.Static : Rules.StaleTime.Read(entry, rethrow: false);
            // A static stale time is TimeSpan.MaxValue, so neither has a moment to wait for, as v5's isValidTimeout
            // turns both away.
            if (entry == null || staleTime.Time == TimeSpan.MaxValue || entry.IsStaleFor(staleTime)
                || !Notifies(QueryProperties.IsStale))
            {
                CancelStaleWait();
                return;
            }

            // MUTANT_SURVIVES(equivalent, guard removed): a wait started again for the same data ends at the same moment.
            if (Equals(_staleFor, (entry.DataUpdatedAt, staleTime.Time))) return;
            CancelStaleWait();
            var wait = new CancellationTokenSource();
            _staleWait = wait;
            _staleFor = (entry.DataUpdatedAt, staleTime.Time);
            WaitStale(entry.Client, wait, entry.DataUpdatedAt, staleTime.Time).Forget();
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
            _staleFor = null;
            Cancel(ref _staleWait);
        }

        private bool Notifies(QueryProperties changed)
        {
            var listed = _settings.Options?.NotifyOnChangeProps;
            if (listed == null && _tracked == QueryProperties.None) return true;
            return (changed & (listed ?? _tracked)) != QueryProperties.None;
        }

        internal QueryResult<TData> Read(QueryEntry<TQueryFnData> entry, in QuerySettings<TQueryFnData, TData> settings)
            => Read(entry, settings, this);

        // A result whose reads are tracked by another source: UseQueries shares one set across its observers.
        internal QueryResult<TData> Read(
            QueryEntry<TQueryFnData> entry, in QuerySettings<TQueryFnData, TData> settings, IQueryResultSource source)
        {
            _current = CreateResult(entry, settings, inRender: true);
            _hasCurrent = true;
            return ResultOf(settings.Paging != null, source);
        }

        // The latest reading as a result, an infinite query's carrying its paging.
        internal override object ResultOf(IQueryResultSource source) => ResultOf(_settings.Paging != null, source);

        internal override void TrackFor(QueryProperties property) => Track(property);

        internal override bool SuspendsWithoutData => _settings.Suspense && !_current.HasData;

        private QuerySnapshot<TData> _recorded;
        private bool _hasRecorded;

        internal override bool ResultChangedSinceRecorded => !_hasRecorded || _current.DifferencesFrom(_recorded) != QueryProperties.None;

        internal override void RecordResult()
        {
            _recorded = _current;
            _hasRecorded = true;
        }

        private QueryResult<TData> ResultOf(bool infinite, IQueryResultSource source)
            => infinite ? new InfiniteQueryResult<TData>(_current, source, this) : new QueryResult<TData>(_current, source);

        // What names the entry the latest commit subscribed to, which UseQueries matches a render's queries by;
        // null before the first commit.
        internal override QueryKey? CommittedKeyId => _settings.Options == null ? null : _settings.KeyId;

        // UseQueries drops an observer whose query left the list.
        internal override void Leave() => Unsubscribe();


        // The reading the latest render or update took, which UseQuery's error rule reads without marking any
        // property as read.
        internal QuerySnapshot<TData> Current => _current;

        // TanStack's createResult: the entry's state, with the fetch a subscription is about to start, then the
        // selected data, then the placeholder data, then a failure of the select.
        // A function form a render asks throws from the render, as v5's does; one asked by an update outside a
        // render is contained.
        private QuerySnapshot<TData> CreateResult(
            QueryEntry<TQueryFnData> entry, in QuerySettings<TQueryFnData, TData> settings, bool inRender)
        {
            var status = entry.Status;
            var hasSource = entry.HasData;
            var source = hasSource ? entry.Data : default!;
            var error = entry.Error;
            var fetchStatus = entry.FetchStatus;
            var failureCount = entry.FailureCount;
            var failureReason = entry.FailureReason;
            var isStale = settings.Rules.IsStale(entry, inRender);

            // TanStack's getOptimisticResult: a render whose subscription will fetch — a mount, a key change, or
            // the query turned on — already reports that fetch, rather than the render before the subscription's
            // own reporting a settled entry.
            if (WillFetch(entry, settings, inRender))
            {
                // TanStack's fetchState: the fetch about to start is paused when it cannot start yet.
                fetchStatus = QueryClientSignals.CanFetch(settings.NetworkMode) ? QueryFetchStatus.Fetching : QueryFetchStatus.Paused;
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
                data = Share(_hasCurrent && _current.HasData, _hasCurrent ? _current.Data : default, placeholder, settings.Options.StructuralSharing);
                hasData = true;
                isPlaceholderData = true;
                _currentPlaceholder = settings.Placeholder;
            }

            // v5 drops a select error once the data it came from is gone, rather than reporting it over the state of
            // an entry that has none. The memo goes with it: v5 memoizes on the previous result's data, so data
            // that comes back is selected again and its failure reported again.
            if (!hasSource && !isPlaceholderData) (_selectError, _selectFn) = (null, null);
            if (_selectError != null)
            {
                error = _selectError;
                data = _selectResult;
                hasData = _hasSelectResult;
                status = QueryStatus.Error;
                isPlaceholderData = false;
            }

            var paging = settings.Paging;
            return new QuerySnapshot<TData>
            {
                Status = status,
                HasData = hasData,
                Data = hasData ? data : default,
                Error = error,
                FetchStatus = fetchStatus,
                IsStale = isStale,
                FailureCount = failureCount,
                FailureReason = failureReason,
                IsPlaceholderData = isPlaceholderData,
                EntryHasData = hasSource,

                // v5 reads the paging off the entry's own data, not the selected or placeholder data.
                HasNextPage = paging != null && hasSource && paging.HasNextPage(source),
                HasPreviousPage = paging != null && hasSource && paging.HasPreviousPage(source),
                FetchDirection = entry.FetchDirection,
            };
        }


        // TanStack's fetchOnMount for an observer not yet subscribed, and fetchOptionally for one that is: Sync
        // makes the same choice when it subscribes. The rules last committed are the previous options, whose
        // enabled v5 resolves against the entry the render reads.
        private bool WillFetch(QueryEntry<TQueryFnData> entry, in QuerySettings<TQueryFnData, TData> settings, bool inRender)
        {
            if (Entry == null) return settings.Rules.ShouldFetchOnMount(entry, inRender);
            return (!ReferenceEquals(entry, Entry) || !Rules.IsEnabled(entry, inRender))
                && settings.Rules.ShouldFetchOptionally(entry, inRender);
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
                data = Share(_hasCurrent && _current.HasData, _hasCurrent ? _current.Data : default, select(source), settings.Options.StructuralSharing);
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

            // An entry that has lost its data since hands none, as v5's state.data reads undefined.
            var previous = _lastWithData is { HasData: true } held ? held : null;
            var produced = settings.Placeholder!(
                previous != null ? previous.Data : default, previous?.Query);
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

        // TanStack's replaceData: a function given as structuralSharing replaces the default comparison.
        private static TData Share(bool hasPrevious, TData? previous, TData next, Func<object?, object, object>? sharing)
        {
            if (sharing == null) return QueryStructuralSharing.Replace(previous, next);
            return (TData)sharing(hasPrevious ? (object?)previous : null, next!);
        }
    }
}
