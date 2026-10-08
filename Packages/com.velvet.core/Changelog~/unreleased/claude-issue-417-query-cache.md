### Added

- `Hooks.UseQuery` over a `QueryClient`, TanStack Query's `useQuery` and `QueryClient`: components reading
  one `QueryKey` share one cache entry and one request in flight, a component unmounting leaves the request
  running for the others, and the entry keeps its result after its last reader unmounts, so a screen
  navigated back to renders it at once instead of fetching from scratch. `StaleTime` and `GcTime` set when a
  result is fetched again and when an unread entry is dropped, `QueryClient.InvalidateQueries` marks the
  entries a key prefix matches stale and fetches again those a mounted query reads — the step a
  `UseMutation` `OnSuccess` takes — and `QueryResult.Refetch` fetches one again. The client is provided through `QueryClientContext.Ref` or passed to the hook.
  A failed request runs again three times by default, after one second, then two, then four
  (`QueryClientOptions.Retry` and `RetryDelay`, or `QueryOptions.Retry` and `RetryDelay` for one query), and
  `QueryResult.FailureCount` and `FailureReason` report the failures meanwhile. A result that lands keeps the
  instance of the data the entry held where the two are deeply equal, and the equal parts of it where they
  are not (`QueryOptions.StructuralSharing` replaces that), and a component that has read properties of the
  result re-renders only when one of them changes (`QueryOptions.NotifyOnChangeProps` lists them instead). A dictionary or set
  part of a `QueryKey` compares whatever order it was built in, and `InvalidateQueries` matches an array,
  list or dictionary part of its filter partially, as TanStack Query's `partialMatchKey` does.
  In the Editor, a key that is unequal on two commits running while printing the same — an array inside a
  record, which compares by reference — logs a warning, since each such commit fetches a new entry.
  `Hooks.Use` is unchanged and stays cache-less, as React's `use()` is.
