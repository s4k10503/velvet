### Added

- A `QueryCache` on each `QueryClient`, TanStack Query's: every entry is a `Query` in it, found through `Find` /
  `FindAll` / `GetAll`, or by its `Query.QueryHash` through `Get`, and removed with `Remove` / `Clear`.
  `Subscribe` hears entries added, removed and updated (with the `QueryActionType` that changed them), and
  mounted queries added, removed, their results updated and their options updated. The cache's `OnSuccess`,
  `OnError` and `OnSettled` run as a request lands or fails. Hand one in through `QueryClientOptions.QueryCache`;
  `QueryClient.GetQueryCache` reads it.
- `QueryOptions.Meta`, v5's `meta`, carried by the entry as `Query.Meta`, and a `QueryOptions` constructor
  whose query function takes a `QueryFunctionContext` (client, key, meta, token), v5's `QueryFunctionContext`.
  A request is cancelled with its last reader only once its function has read
  `QueryFunctionContext.CancellationToken`.
- `Hooks.UseIsFetching` and `QueryClient.IsFetching`, TanStack Query's `useIsFetching` and `isFetching`.
- `Hooks.UseQueries` and `Hooks.UseSuspenseQueries`, TanStack Query's `useQueries` and `useSuspenseQueries`,
  with and without a `combine`: two to four queries as their own arguments, each keeping its data types, or a
  list built at run time, each query keeping its observer while some query of the list reads its entry.
- `Hooks.UseInfiniteQuery` and `Hooks.UseSuspenseInfiniteQuery`, TanStack Query's `useInfiniteQuery` and
  `useSuspenseInfiniteQuery`, over `InfiniteQueryOptions` (`QueryFn`, `InitialPageParam`, `GetNextPageParam`,
  `GetPreviousPageParam`, `MaxPages`) and an entry holding `InfiniteData`. The `InfiniteQueryResult` adds
  `FetchNextPage`, `FetchPreviousPage`, `HasNextPage`, `HasPreviousPage`, `IsFetchingNextPage`,
  `IsFetchingPreviousPage`, `IsFetchNextPageError` and `IsFetchPreviousPageError`, each a `QueryProperties` flag
  for `NotifyOnChangeProps`. A refetch fetches the pages again from the first, as many as the entry holds.
  `QueryClient.FetchInfiniteQuery`, `PrefetchInfiniteQuery` and `EnsureInfiniteQueryData` fill the cache ahead
  of it.
- `QueryResult.IsRefetching` and `IsRefetchError`, v5's `isRefetching` and `isRefetchError`, with their
  `QueryProperties` flags.

### Changed

- The read-only QueryInfo view is replaced by `Query`: the function forms of the query options (`EnabledFn`, `StaleTimeFn`,
  `RefetchIntervalFn` and the rest), `PlaceholderData`'s `previousQuery`, `QueryPlaceholder.KeepPreviousData`
  and `QueryFilters.Predicate` are handed the entry's `Query`, which reads the entry as it stands rather than
  as it stood at the call. A nullable `previousQuery` is tested with `!= null` rather than `HasValue`.
- The options every query shares move to `QueryOptionsBase`, which `QueryOptions` and `InfiniteQueryOptions`
  derive from. `QueryOptions.QueryFn` is a `QueryFunction<T>`, holding a function that takes the context or
  nothing; a `with { QueryFn = ... }` hands it `QueryFunction.Of(...)`.
- The `QueryOptions` constructors taking a function of a `CancellationToken` are removed: a query function takes
  the `QueryFunctionContext`, as v5's does, and reads `context.CancellationToken`, or takes nothing. Reading the
  token is what makes the last reader leaving cancel the request.
- `QueryFilters.Exact` is a `bool?`; `QueryCache.Find` reads null as true, as v5's `find` defaults `exact`.
- Invalidating an entry tells the queries reading it, as v5's invalidate dispatch does, so a result reading
  `IsStale` re-renders with the invalidation.
