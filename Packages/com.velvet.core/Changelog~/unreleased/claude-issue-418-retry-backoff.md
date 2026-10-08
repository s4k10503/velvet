### Added

- `RetryPolicy`, TanStack Query's `retry` / `retryDelay` as a value: a retry count, an optional predicate
  over the failure count and exception, a delay defaulting to one second doubled per retry and capped at
  thirty, and a replaceable wait. `MutationOptions.Retry` opts a `Hooks.UseMutation` call into it — the
  call stays pending across attempts and its `OnSuccess` / `OnError` run once — and `RetryPolicy.RunAsync`
  wraps a `Hooks.Use` loader so the resource stays pending until the last attempt settles. Mutations still
  do not retry unless a policy is given, as TanStack's mutations default to `retry: 0`.
