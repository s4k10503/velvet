### Changed

- A mounted `Hooks.UseQuery` fetches again when the application becomes visible after being hidden, or the
  device comes back online, while its data is stale, as TanStack Query v5's `refetchOnWindowFocus` and
  `refetchOnReconnect` default to. With the default `StaleTime` of zero that is every enabled one; set
  `QueryClientOptions.RefetchOnWindowFocus` / `RefetchOnReconnect` to `QueryRefetchMode.Never` to keep the
  earlier behaviour.
