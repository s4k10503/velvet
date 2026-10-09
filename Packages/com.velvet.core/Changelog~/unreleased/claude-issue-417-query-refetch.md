### Added

- `QueryOptions.RefetchInterval` and `RefetchIntervalInBackground`, TanStack Query v5's `refetchInterval` and
  `refetchIntervalInBackground`: an enabled query fetches again each time the interval has passed since its
  entry last changed or the interval last came round, waiting while the application is not visible unless
  it runs in the background.
- `QueryOptions.RefetchOnWindowFocus` and `RefetchOnReconnect` (`QueryRefetchMode`, or for every query on
  `QueryClientOptions`): a mounted query fetches again when the application becomes visible or the device
  comes back online, by default when its data is stale. `NetworkSignals` holds both readings, with an
  override for an application that measures them itself.

### Fixed

- A `QueryClient` entry nothing reads is no longer removed while a request for it is in flight, which
  cancelled that request: as in TanStack Query v5 it stays, and readable, until the first `GcTime` after the
  request settles.
