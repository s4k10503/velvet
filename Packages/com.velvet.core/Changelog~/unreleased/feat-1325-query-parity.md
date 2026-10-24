### Added

- `QueryClient` commands from TanStack Query v5: `RefetchQueries`, `CancelQueries`, `RemoveQueries`,
  `ResetQueries`, `FetchQuery`, `PrefetchQuery`, `EnsureQueryData`, `GetQueryState`, `GetQueriesData` and
  `SetQueriesData`, and an `InvalidateQueries` overload taking `QueryFilters` and `RefetchOptions`.
  `QueryFilters` selects entries by key prefix or exact key, active or inactive type, staleness, fetch status
  and predicate. `RefetchQueries`, `InvalidateQueries` and `ResetQueries` return a task that completes once
  their requests have settled, so a mutation callback that returns it waits for the fresh data, and
  `CancelQueries` returns a completed task for an `OnMutate` to await before it writes an optimistic value.
