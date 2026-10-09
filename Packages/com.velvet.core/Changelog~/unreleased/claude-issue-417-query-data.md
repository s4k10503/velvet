### Added

- `QueryOptions.Enabled`, `Select` and `PlaceholderData`, TanStack Query v5's `enabled`, `select` and
  `placeholderData`. A disabled query fetches nothing on its own and reports itself not stale, and fetches
  stale data when it is turned on; `Refetch` still fetches. `Select` turns the entry's data into the
  result's, possibly of another type through `QueryOptions<TQueryFnData, TData>` and
  `Hooks.UseQuery<TQueryFnData, TData>`, runs again over the entry's data only when that data's instance or
  the select's changes, and keeps the result's instance where what it returns is deeply equal.
  `PlaceholderData` shows data while the entry is pending, as a success with
  `QueryResult.IsPlaceholderData` true, and never writes it to the entry; `QueryPlaceholder.KeepPreviousData`
  keeps the previous key's data on screen across a key change.
- `QueryClient.GetQueryData` and `SetQueryData`, v5's `getQueryData` and `setQueryData`: a write lands as a
  request's result would, fresh and shared structurally with the data held, leaves a request in flight
  running, and creates the entry when there is none.

### Fixed

- `QueryClient.InvalidateQueries` refetches an entry with the options it was last handed — by the last query
  to commit over it or start a request for it — as TanStack Query v5 refetches a query with its own options.
  It used the first reader's, so where readers of one key pass different query functions, a different one
  can run now.
- A request that lands shares its data with the `StructuralSharing` of the options its entry holds when it
  lands, as v5's fetch does, rather than with those it started with.
