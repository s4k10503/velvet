#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // TanStack's infiniteQueryBehavior: what a request for an infinite query's entry runs in place of the query
    // function, one page at a time.
    internal sealed class InfiniteQueryBehavior<TPage, TPageParam> : IQueryBehavior<InfiniteData<TPage, TPageParam>>
    {
        private static readonly InfiniteData<TPage, TPageParam> s_empty = new(Array.Empty<TPage>(), Array.Empty<TPageParam>());

        private readonly Func<QueryFunctionContext<TPageParam>, VelvetTask<TPage>> _pageFn;
        private readonly TPageParam _initialPageParam;
        private readonly Func<TPage, IReadOnlyList<TPage>, TPageParam, IReadOnlyList<TPageParam>, PageParam<TPageParam>> _getNext;
        private readonly Func<TPage, IReadOnlyList<TPage>, TPageParam, IReadOnlyList<TPageParam>, PageParam<TPageParam>>? _getPrevious;
        private readonly int _maxPages;
        private readonly int? _pages;

        internal InfiniteQueryBehavior(
            Func<QueryFunctionContext<TPageParam>, VelvetTask<TPage>> pageFn,
            TPageParam initialPageParam,
            Func<TPage, IReadOnlyList<TPage>, TPageParam, IReadOnlyList<TPageParam>, PageParam<TPageParam>> getNext,
            Func<TPage, IReadOnlyList<TPage>, TPageParam, IReadOnlyList<TPageParam>, PageParam<TPageParam>>? getPrevious,
            int maxPages,
            int? pages)
        {
            _pageFn = pageFn;
            _initialPageParam = initialPageParam;
            _getNext = getNext;
            _getPrevious = getPrevious;
            _maxPages = maxPages;
            _pages = pages;
        }

        // TanStack's onFetch. What a request has fetched so far, and how many pages, outlive an attempt, so a retry of
        // a refetch carries on from the page that failed, as v5's fetchFn closes over them.
        public Func<QueryFunctionContext, VelvetTask<InfiniteData<TPage, TPageParam>>> OnFetch(
            bool hasData, InfiniteData<TPage, TPageParam> data, FetchDirection? direction)
        {
            var old = hasData ? data : s_empty;
            if (direction != null && old.Pages.Count > 0)
            {
                var previous = direction == FetchDirection.Backward;
                return context => FetchMore(context, old, previous);
            }
            var progress = new Progress();
            return context => RefetchAll(context, old, progress);
        }

        private sealed class Progress
        {
            internal InfiniteData<TPage, TPageParam> Result = s_empty;
            internal int CurrentPage;
        }

        private VelvetTask<InfiniteData<TPage, TPageParam>> FetchMore(QueryFunctionContext context, InfiniteData<TPage, TPageParam> old, bool previous)
        {
            var param = previous ? PreviousParam(_getPrevious, old) : NextParam(_getNext, old);
            return FetchPage(context, old, param, previous);
        }

        // The first page is fetched with the parameter it was fetched with before, or the initial one, and each
        // later one with what getNextPageParam names, until it names none or as many pages as the entry held, or as
        // `pages` asks, have been fetched.
        private async VelvetTask<InfiniteData<TPage, TPageParam>> RefetchAll(
            QueryFunctionContext context, InfiniteData<TPage, TPageParam> old, Progress progress)
        {
            var remaining = _pages ?? old.Pages.Count;
            do
            {
                var param = progress.CurrentPage == 0
                    ? (old.PageParams.Count > 0 ? new PageParam<TPageParam>(old.PageParams[0]) : new PageParam<TPageParam>(_initialPageParam))
                    : NextParam(_getNext, progress.Result);
                if (progress.CurrentPage > 0 && !param.HasValue) break;
                progress.Result = await FetchPage(context, progress.Result, param, previous: false);
                progress.CurrentPage++;
            }
            while (progress.CurrentPage < remaining);
            return progress.Result;
        }

        // TanStack's fetchPage: no parameter over pages already held fetches nothing.
        private async VelvetTask<InfiniteData<TPage, TPageParam>> FetchPage(
            QueryFunctionContext context, InfiniteData<TPage, TPageParam> data, PageParam<TPageParam> param, bool previous)
        {
            if (context.IsCancellationRequested) throw new OperationCanceledException();
            if (!param.HasValue && data.Pages.Count > 0) return data;
            var page = await _pageFn(new QueryFunctionContext<TPageParam>(
                context, param.Value, previous ? FetchDirection.Backward : FetchDirection.Forward));
            return new InfiniteData<TPage, TPageParam>(
                AddTo(data.Pages, page, previous), AddTo(data.PageParams, param.Value, previous));
        }

        // TanStack's addToEnd and addToStart: past maxPages the page at the other end goes.
        private IReadOnlyList<T> AddTo<T>(IReadOnlyList<T> items, T item, bool atStart)
        {
            var added = new List<T>(items.Count + 1);
            if (atStart) added.Add(item);
            added.AddRange(items);
            if (!atStart) added.Add(item);
            if (_maxPages <= 0 || added.Count <= _maxPages) return added.ToArray();
            added.RemoveAt(atStart ? added.Count - 1 : 0);
            return added.ToArray();
        }

        // TanStack's getNextPageParam: none without pages.
        internal static PageParam<TPageParam> NextParam(
            Func<TPage, IReadOnlyList<TPage>, TPageParam, IReadOnlyList<TPageParam>, PageParam<TPageParam>> getNext,
            InfiniteData<TPage, TPageParam> data)
        {
            var last = data.Pages.Count - 1;
            return last < 0 ? PageParam<TPageParam>.None : getNext(data.Pages[last], data.Pages, data.PageParams[last], data.PageParams);
        }

        // TanStack's getPreviousPageParam: none without pages or without the function.
        internal static PageParam<TPageParam> PreviousParam(
            Func<TPage, IReadOnlyList<TPage>, TPageParam, IReadOnlyList<TPageParam>, PageParam<TPageParam>>? getPrevious,
            InfiniteData<TPage, TPageParam> data)
        {
            return data.Pages.Count == 0 || getPrevious == null
                ? PageParam<TPageParam>.None
                : getPrevious(data.Pages[0], data.Pages, data.PageParams[0], data.PageParams);
        }
    }
}
