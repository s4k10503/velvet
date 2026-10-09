### Changed

- A mounted, enabled `Hooks.UseQuery` whose data is stale fetches again when the device comes back online,
  and, on a mobile platform or where `NetworkSignals.IsVisible` is set, when the application becomes visible
  after being hidden, as TanStack Query v5's `refetchOnReconnect` and `refetchOnWindowFocus` default to. Off
  a mobile platform the default visibility never changes, so no focus refetch fires there. Set
  `QueryClientOptions.RefetchOnWindowFocus` / `RefetchOnReconnect` to `QueryRefetchMode.Never` to keep the
  earlier behaviour.
