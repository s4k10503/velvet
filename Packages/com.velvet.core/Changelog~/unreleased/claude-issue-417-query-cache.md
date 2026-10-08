### Added

- `Hooks.UseQuery` over a `QueryClient`, TanStack Query's `useQuery` and `QueryClient`: components reading
  one `QueryKey` share one cache entry and one request in flight, a component unmounting leaves the request
  running for the others, and the entry keeps its result after its last reader unmounts, so a screen
  navigated back to renders it at once instead of fetching from scratch. `StaleTime` and `GcTime` set when a
  result is fetched again and when an unread entry is dropped, `QueryClient.InvalidateQueries` fetches again
  the entries a key prefix matches — the step a `UseMutation` `OnSuccess` takes — and `QueryResult.Refetch`
  fetches one again. The client is provided through `QueryClientContext.Ref` or passed to the hook.
  `Hooks.Use` is unchanged and stays cache-less, as React's `use()` is.
