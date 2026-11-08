### Added

- A `QueryCache` on each `QueryClient`, TanStack Query's: every entry is a `Query` in it, found through `Find` /
  `FindAll` / `GetAll`, and removed with `Remove` / `Clear`. `Subscribe` hears entries added, removed and updated
  (with the `QueryActionType` that changed them), and mounted queries added, removed, their results updated and
  their options updated. The cache's `OnSuccess`, `OnError` and `OnSettled` run as a request lands or fails. Hand
  one in through `QueryClientOptions.QueryCache`; `QueryClient.GetQueryCache` reads it.
- `QueryOptions.Meta`, v5's `meta`, carried by the entry as `Query.Meta`, and a `QueryOptions` constructor
  whose query function takes a `QueryFunctionContext` (client, key, meta, token), v5's `QueryFunctionContext`.
  Its request is cancelled with its last reader only once it has read `QueryFunctionContext.CancellationToken`.
- `Hooks.UseIsFetching` and `QueryClient.IsFetching`, TanStack Query's `useIsFetching` and `isFetching`.
- `Hooks.UseQueries` and `Hooks.UseSuspenseQueries`, TanStack Query's `useQueries` and `useSuspenseQueries`,
  with and without a `combine`: a list of queries read in one hook, each keeping its observer while some query
  of the list reads its entry.

### Changed

- `QueryInfo` is replaced by `Query`: the function forms of the query options (`EnabledFn`, `StaleTimeFn`,
  `RefetchIntervalFn` and the rest), `PlaceholderData`'s `previousQuery`, `QueryPlaceholder.KeepPreviousData`
  and `QueryFilters.Predicate` are handed the entry's `Query`, which reads the entry as it stands rather than
  as it stood at the call. A nullable `previousQuery` is tested with `!= null` rather than `HasValue`.
- `QueryFilters.Exact` is a `bool?`; `QueryCache.Find` reads null as true, as v5's `find` defaults `exact`.
- A query function lambda whose untyped parameter its body never uses, such as `_ => ...`, no longer compiles,
  being ambiguous between the token-taking and the context-taking constructors: write
  `(CancellationToken _) => ...`.
