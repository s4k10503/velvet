#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Velvet
{
    /// <summary>
    /// What an infinite query's entry holds, TanStack Query's <c>InfiniteData</c>: its pages, in order, and the page
    /// parameter each was fetched with.
    /// </summary>
    /// <typeparam name="TPage">What the query function returns for one page.</typeparam>
    /// <typeparam name="TPageParam">What names a page to the query function.</typeparam>
    /// <param name="Pages">The pages.</param>
    /// <param name="PageParams">The parameter of each page, at the page's index.</param>
    public sealed record InfiniteData<TPage, TPageParam>(IReadOnlyList<TPage> Pages, IReadOnlyList<TPageParam> PageParams);

    /// <summary>Which end of an infinite query's pages a request adds to, TanStack Query's <c>direction</c>.</summary>
    public enum FetchDirection
    {
        /// <summary>After the last page, TanStack Query's <c>'forward'</c>.</summary>
        Forward,
        /// <summary>Before the first page, TanStack Query's <c>'backward'</c>.</summary>
        Backward,
    }

    /// <summary>
    /// A page parameter, or none: what <see cref="InfiniteQueryOptions{TQueryFnData, TPageParam, TData}.GetNextPageParam"/>
    /// returns, where TanStack Query's function returns the parameter or <c>undefined</c> / <c>null</c>. A value
    /// converts to it implicitly, and a null value is none.
    /// </summary>
    /// <typeparam name="T">The page parameter's type.</typeparam>
    public readonly struct PageParam<T>
    {
        /// <summary>Wraps <paramref name="value"/>; a null value is none.</summary>
        public PageParam(T value)
        {
            Value = value;
            HasValue = value is not null;
        }

        /// <summary>No page parameter: there is no page to fetch at that end.</summary>
        public static PageParam<T> None => default;

        /// <summary>Whether there is a page parameter.</summary>
        public bool HasValue { get; }

        /// <summary>The page parameter; default when there is none.</summary>
        public T Value { get; }

        /// <summary>Wraps <paramref name="value"/>; a null value is none.</summary>
        public static implicit operator PageParam<T>(T value) => new(value);
    }

    /// <summary>
    /// What an infinite query's function is handed for one page, TanStack Query's <c>QueryFunctionContext</c> with
    /// its <c>pageParam</c> and <c>direction</c>.
    /// </summary>
    /// <typeparam name="TPageParam">What names a page.</typeparam>
    public sealed class QueryFunctionContext<TPageParam> : QueryFunctionContext
    {
        internal QueryFunctionContext(QueryFunctionContext source, TPageParam pageParam, FetchDirection direction)
            : base(source)
        {
            PageParam = pageParam;
            Direction = direction;
        }

        /// <summary>The parameter of the page to fetch.</summary>
        public TPageParam PageParam { get; }

        /// <summary>Whether the page goes after the last page or before the first.</summary>
        public FetchDirection Direction { get; }
    }

    /// <summary>
    /// Options passed to <see cref="Hooks.UseInfiniteQuery{TQueryFnData, TPageParam, TData}"/>, TanStack Query's
    /// infinite query options: the options of <see cref="QueryOptions{TQueryFnData, TData}"/> over the
    /// <see cref="InfiniteData{TPage, TPageParam}"/> the entry holds, with a query function that fetches one page, the
    /// first page's parameter, and the functions that name the pages either side of those held.
    /// <see cref="InfiniteQueryOptions{TQueryFnData, TPageParam}"/> is the form without a select.
    /// </summary>
    /// <typeparam name="TQueryFnData">What the query function returns for one page.</typeparam>
    /// <typeparam name="TPageParam">What names a page.</typeparam>
    /// <typeparam name="TData">What the result's data holds; the pages unless a select turns them into something
    /// else.</typeparam>
    public record InfiniteQueryOptions<TQueryFnData, TPageParam, TData>
        : QueryOptionsBase<InfiniteData<TQueryFnData, TPageParam>, TData>, IInfiniteQueryOptions<InfiniteData<TQueryFnData, TPageParam>>
    {
        /// <summary>Creates the options.</summary>
        /// <param name="queryKey">Names the cache entry, as <see cref="QueryOptionsBase{TQueryFnData, TData}.QueryKey"/>
        /// does.</param>
        /// <param name="queryFn">Fetches one page. Its request is cancelled when the last query reading the entry
        /// leaves only if it has read <see cref="QueryFunctionContext.CancellationToken"/>.</param>
        /// <param name="initialPageParam">TanStack Query's <c>initialPageParam</c>: the first page's parameter.</param>
        /// <param name="getNextPageParam">TanStack Query's <c>getNextPageParam</c>: handed the last page, every page,
        /// the last page's parameter and every parameter, it returns the parameter of the page after the last, or
        /// <see cref="PageParam{T}.None"/> when there is none.</param>
        public InfiniteQueryOptions(
            QueryKey queryKey,
            Func<QueryFunctionContext<TPageParam>, VelvetTask<TQueryFnData>> queryFn,
            TPageParam initialPageParam,
            Func<TQueryFnData, IReadOnlyList<TQueryFnData>, TPageParam, IReadOnlyList<TPageParam>, PageParam<TPageParam>> getNextPageParam)
            : base(queryKey)
        {
            QueryFn = queryFn;
            InitialPageParam = initialPageParam;
            GetNextPageParam = getNextPageParam;
        }

        /// <summary>TanStack Query's <c>queryFn</c> for an infinite query, which fetches one page.</summary>
        public Func<QueryFunctionContext<TPageParam>, VelvetTask<TQueryFnData>> QueryFn { get; init; }

        // A request runs the paging the behavior builds, never a function of the whole entry.
        internal override QueryFunction<InfiniteData<TQueryFnData, TPageParam>> Function => default;

        /// <summary>TanStack Query's <c>initialPageParam</c>.</summary>
        public TPageParam InitialPageParam { get; init; }

        /// <summary>TanStack Query's <c>getNextPageParam</c>; see the constructor.</summary>
        public Func<TQueryFnData, IReadOnlyList<TQueryFnData>, TPageParam, IReadOnlyList<TPageParam>, PageParam<TPageParam>> GetNextPageParam { get; init; }

        /// <summary>
        /// TanStack Query's <c>getPreviousPageParam</c>: handed the first page, every page, the first page's parameter
        /// and every parameter, it returns the parameter of the page before the first, or
        /// <see cref="PageParam{T}.None"/>. Null leaves the result without a previous page.
        /// </summary>
        public Func<TQueryFnData, IReadOnlyList<TQueryFnData>, TPageParam, IReadOnlyList<TPageParam>, PageParam<TPageParam>>? GetPreviousPageParam { get; init; }

        /// <summary>
        /// TanStack Query's <c>maxPages</c>: the most pages the entry keeps. A page fetched after the last drops the
        /// first, and one fetched before the first drops the last. Null or zero keeps every page.
        /// </summary>
        public int? MaxPages { get; init; }

        /// <summary>
        /// TanStack Query's <c>pages</c> for <see cref="QueryClient.FetchInfiniteQuery{TQueryFnData, TPageParam}"/> and
        /// <see cref="QueryClient.PrefetchInfiniteQuery{TQueryFnData, TPageParam}"/>: how many pages a request fetches
        /// from the first, where by default it fetches as many as the entry holds. A mounted query does not read it.
        /// </summary>
        public int? Pages { get; init; }

        IQueryBehavior<InfiniteData<TQueryFnData, TPageParam>> IInfiniteQueryOptions<InfiniteData<TQueryFnData, TPageParam>>.CreateBehavior(int? pages)
            => new InfiniteQueryBehavior<TQueryFnData, TPageParam>(QueryFn, InitialPageParam, GetNextPageParam, GetPreviousPageParam, MaxPages ?? 0, pages);

        bool IInfiniteQueryOptions<InfiniteData<TQueryFnData, TPageParam>>.HasNextPage(InfiniteData<TQueryFnData, TPageParam> data)
            => InfiniteQueryBehavior<TQueryFnData, TPageParam>.NextParam(GetNextPageParam, data).HasValue;

        bool IInfiniteQueryOptions<InfiniteData<TQueryFnData, TPageParam>>.HasPreviousPage(InfiniteData<TQueryFnData, TPageParam> data)
            => InfiniteQueryBehavior<TQueryFnData, TPageParam>.PreviousParam(GetPreviousPageParam, data).HasValue;
    }

    /// <summary>
    /// <see cref="InfiniteQueryOptions{TQueryFnData, TPageParam, TData}"/> for a query whose result holds the
    /// <see cref="InfiniteData{TPage, TPageParam}"/> the entry holds.
    /// </summary>
    /// <typeparam name="TQueryFnData">What the query function returns for one page.</typeparam>
    /// <typeparam name="TPageParam">What names a page.</typeparam>
    public sealed record InfiniteQueryOptions<TQueryFnData, TPageParam>
        : InfiniteQueryOptions<TQueryFnData, TPageParam, InfiniteData<TQueryFnData, TPageParam>>
    {
        /// <summary>Creates the options; see the constructor of
        /// <see cref="InfiniteQueryOptions{TQueryFnData, TPageParam, TData}"/>.</summary>
        /// <param name="queryKey">Names the cache entry.</param>
        /// <param name="queryFn">Fetches one page.</param>
        /// <param name="initialPageParam">The first page's parameter.</param>
        /// <param name="getNextPageParam">The parameter of the page after the last, or none.</param>
        public InfiniteQueryOptions(
            QueryKey queryKey,
            Func<QueryFunctionContext<TPageParam>, VelvetTask<TQueryFnData>> queryFn,
            TPageParam initialPageParam,
            Func<TQueryFnData, IReadOnlyList<TQueryFnData>, TPageParam, IReadOnlyList<TPageParam>, PageParam<TPageParam>> getNextPageParam)
            : base(queryKey, queryFn, initialPageParam, getNextPageParam)
        {
        }
    }

    /// <summary>
    /// What <see cref="Hooks.UseInfiniteQuery{TQueryFnData, TPageParam, TData}"/> returns, TanStack Query's
    /// <c>UseInfiniteQueryResult</c>: the <see cref="QueryResult{T}"/> properties, whether there is a page either
    /// side of those held, which end a request in flight or the last failure was for, and the calls that fetch the
    /// next and previous page.
    /// </summary>
    /// <typeparam name="TData">What the result's data holds.</typeparam>
    public sealed class InfiniteQueryResult<TData> : QueryResult<TData>
    {
        private readonly IInfiniteResultSource<TData> _pager;

        internal InfiniteQueryResult(QuerySnapshot<TData> snapshot, IQueryResultSource observer, IInfiniteResultSource<TData> pager)
            : base(snapshot, observer)
        {
            _pager = pager;
        }

        /// <summary>Whether <see cref="InfiniteQueryOptions{TQueryFnData, TPageParam, TData}.GetNextPageParam"/> names a
        /// page after the last the entry holds, TanStack Query's <c>hasNextPage</c>; false while it holds none.</summary>
        public bool HasNextPage => Read(QueryProperties.HasNextPage, Snapshot.HasNextPage);

        /// <summary>Whether <see cref="InfiniteQueryOptions{TQueryFnData, TPageParam, TData}.GetPreviousPageParam"/> names
        /// a page before the first, TanStack Query's <c>hasPreviousPage</c>.</summary>
        public bool HasPreviousPage => Read(QueryProperties.HasPreviousPage, Snapshot.HasPreviousPage);

        /// <summary>Whether the request in flight fetches the next page, TanStack Query's
        /// <c>isFetchingNextPage</c>.</summary>
        public bool IsFetchingNextPage => Read(QueryProperties.IsFetchingNextPage,
            Snapshot.FetchStatus == QueryFetchStatus.Fetching && Snapshot.FetchDirection == FetchDirection.Forward);

        /// <summary>Whether the request in flight fetches the previous page, TanStack Query's
        /// <c>isFetchingPreviousPage</c>.</summary>
        public bool IsFetchingPreviousPage => Read(QueryProperties.IsFetchingPreviousPage,
            Snapshot.FetchStatus == QueryFetchStatus.Fetching && Snapshot.FetchDirection == FetchDirection.Backward);

        /// <summary>Whether the result is an error from fetching the next page, TanStack Query's
        /// <c>isFetchNextPageError</c>.</summary>
        public bool IsFetchNextPageError => Read(QueryProperties.IsFetchNextPageError,
            Snapshot.Status == QueryStatus.Error && Snapshot.FetchDirection == FetchDirection.Forward);

        /// <summary>Whether the result is an error from fetching the previous page, TanStack Query's
        /// <c>isFetchPreviousPageError</c>.</summary>
        public bool IsFetchPreviousPageError => Read(QueryProperties.IsFetchPreviousPageError,
            Snapshot.Status == QueryStatus.Error && Snapshot.FetchDirection == FetchDirection.Backward);

        /// <summary>
        /// Fetches the page after the last, TanStack Query's <c>fetchNextPage</c>: the entry gains the page
        /// <see cref="InfiniteQueryOptions{TQueryFnData, TPageParam, TData}.GetNextPageParam"/> names, and nothing is
        /// fetched when it names none. A request in flight over pages the entry holds is cancelled and this one
        /// started instead unless <paramref name="cancelRefetch"/> is false, which joins it. An entry with no pages
        /// fetches its first page. Does nothing before the component's effects have run, and after it unmounts.
        /// </summary>
        /// <param name="cancelRefetch">False joins a request in flight rather than starting over.</param>
        /// <param name="throwOnError">True makes the task fault with the request's failure.</param>
        /// <returns>A task of the result once the request has settled.</returns>
        public VelvetTask<InfiniteQueryResult<TData>> FetchNextPage(bool cancelRefetch = true, bool throwOnError = false)
            => _pager.FetchPage(FetchDirection.Forward, cancelRefetch, throwOnError);

        /// <summary>
        /// Fetches the page before the first, TanStack Query's <c>fetchPreviousPage</c>, on the terms of
        /// <see cref="FetchNextPage"/> with <see cref="InfiniteQueryOptions{TQueryFnData, TPageParam, TData}.GetPreviousPageParam"/>.
        /// </summary>
        /// <param name="cancelRefetch">As <see cref="FetchNextPage"/>'s.</param>
        /// <param name="throwOnError">As <see cref="FetchNextPage"/>'s.</param>
        /// <returns>A task of the result once the request has settled.</returns>
        public VelvetTask<InfiniteQueryResult<TData>> FetchPreviousPage(bool cancelRefetch = true, bool throwOnError = false)
            => _pager.FetchPage(FetchDirection.Backward, cancelRefetch, throwOnError);
    }

    // What an InfiniteQueryResult fetches its pages through.
    internal interface IInfiniteResultSource<TData>
    {
        VelvetTask<InfiniteQueryResult<TData>> FetchPage(FetchDirection direction, bool cancelRefetch, bool throwOnError);
    }

    // What the query machinery reads of infinite options, whatever their page types.
    internal interface IInfiniteQueryOptions<T>
    {
        IQueryBehavior<T> CreateBehavior(int? pages);
        bool HasNextPage(T data);
        bool HasPreviousPage(T data);
    }

    // TanStack's query behavior: what a request runs in place of the query function.
    internal interface IQueryBehavior<T>
    {
        // The function each attempt of a request runs, built when the request starts from the data the entry held then
        // and the end the request was asked for, null for a refetch of every page.
        Func<QueryFunctionContext, VelvetTask<T>> OnFetch(bool hasData, T data, FetchDirection? direction);
    }
}
