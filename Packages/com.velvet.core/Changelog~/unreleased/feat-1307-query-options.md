### Added

- `Hooks.UseSuspenseQuery`, TanStack Query's `useSuspenseQuery`: a pending result suspends to the nearest
  `V.Suspense`, its render starting the request, and an error the entry holds no data for is thrown to the nearest
  error boundary.
- `ThrowOnError` and `ThrowOnErrorFn` on `QueryOptions` and `QueryClientOptions`, v5's `throwOnError`, with `QueryErrorResetBoundary`,
  `QueryErrorResetBoundaryContext` and `Hooks.UseQueryErrorResetBoundary`, v5's `QueryErrorResetBoundary` and
  `useQueryErrorResetBoundary`.
- `QueryOptions.InitialData`, `InitialDataUpdatedAt` and `InitialDataUpdatedAtFn`, v5's `initialData` and
  `initialDataUpdatedAt`; `ResetQueries` returns an entry to the initial data it took.
- `QueryStaleTime.Static`, v5's `staleTime: 'static'`, and `StaleTimeFn` on `QueryOptions` and
  `QueryClientOptions`, v5's `staleTime` as a function.
- `RefetchOnMount` and `RefetchOnMountFn` on `QueryOptions` and `QueryClientOptions`, v5's `refetchOnMount`.
- `QueryOptions.EnabledFn`, v5's `enabled` as a function, and `QueryOptions.RetryOnMount` and `RetryOnMountFn`,
  v5's `retryOnMount`.
