### Changed

- `QueryOptions.Retry` and `QueryClientOptions.Retry` take a `RetryRule`, so a query retries as v5's `retry` given
  as `true`, `false` or `(failureCount, error) => boolean` as well as a number; a number still converts.
  `QueryClient.DefaultRetry` is a `RetryRule?`, null where the client's options set none, and
  `QueryClientOptions.Retry` defaults to null, which a mounted query reads as three retries. `FetchQuery`,
  `PrefetchQuery` and `EnsureQueryData` now retry as the client's `Retry` says where the options set none, as v5's
  `fetchQuery` does; they used to ignore it. A failed request asks `RetryDelay` for its wait before it decides
  whether to retry, as v5's retryer does, so the last failure asks it too, and one that throws there fails the
  request with its own exception.
- `QueryOptions.StaleTime`, `QueryClientOptions.StaleTime` and `QueryClient.DefaultStaleTime` are a
  `QueryStaleTime`, to which a `TimeSpan` converts, so that they can hold v5's `'static'`.
- A query's render builds the entry it reads when there is none, as v5's observer builds its query, so the
  client holds the entry from the first render on, a disabled query's included, and the client's commands and
  `GetQueryState` find it before the component commits. A render used to read the cache without writing to it.
