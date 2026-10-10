### Changed

- A mounted, enabled `Hooks.UseQuery` whose data is stale fetches again when the device comes back online
  and when the application becomes visible after being hidden, as TanStack Query v5's `refetchOnReconnect`
  and `refetchOnWindowFocus` default to. The default visibility reading is `Application.isFocused` on every
  platform, the Editor included, so a desktop application regaining focus refetches too. Set
  `QueryClientOptions.RefetchOnWindowFocus` / `RefetchOnReconnect` to `QueryRefetchMode.Never` to keep the
  earlier behaviour.
- When the last query reading an entry leaves while its request is in flight, the request is cancelled and
  the entry put back as it stood before it, as TanStack Query v5 does for a query function that read its
  `signal`. A query function taking the `CancellationToken` counts as having read it: its token is now
  cancelled when its last reader unmounts, where the request used to run on and land in the entry. Build the
  options with a function that takes no token (`new QueryOptions<T>(key, () => ...)`) to keep a request
  running after its readers leave.
