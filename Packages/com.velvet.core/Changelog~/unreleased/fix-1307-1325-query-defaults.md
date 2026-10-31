### Added

- `QueryOptions.NetworkMode` and `QueryClientOptions.NetworkMode` (`QueryClient.DefaultNetworkMode`), TanStack
  Query v5's `networkMode` for queries, with the paused state it produces: `QueryResult.FetchStatus` and
  `IsPaused`, `QueryFetchStatus.Paused`, `QueryInfo.FetchStatus`, `QueryState<T>.FetchStatus` and the
  `QueryProperties.FetchStatus` and `IsPaused` flags for `NotifyOnChangeProps`.
- `QueryOptions.QueryKeyHashFn` and `QueryClientOptions.QueryKeyHashFn` (`QueryClient.DefaultQueryKeyHashFn`),
  TanStack Query v5's `queryKeyHashFn`: keys the function maps to one string name one entry.
