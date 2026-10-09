### Added

- `RetryPolicy`, TanStack Query's `retry` / `retryDelay` / `networkMode` as a value. `Retry` takes a count, a
  bool or `RetryRule.When` over the failure count and exception, as TanStack's `retry` takes a number, a
  boolean or a function (a function replaces the count and `true` retries without end). `RetryDelay` takes a
  constant or a function and defaults to one second doubled per retry and capped at thirty. A retry waits for
  focus and, per `NetworkMode`, for a connection, and the wait is replaceable. `MutationOptions.Retry` opts a
  `Hooks.UseMutation` call into it — the call stays pending across attempts, its `OnMutate` runs once
  before them and its `OnSuccess` / `OnError` / `OnSettled` once after, and `MutationResult` reports `FailureCount`, `FailureReason` and `IsPaused` — and
  `RetryPolicy.RunAsync` wraps a `Hooks.Use` loader so the resource stays pending until the last attempt
  settles. Mutations still do not retry unless a policy is given, as TanStack's mutations default to
  `retry: 0`.

- `NetworkSignals`, with `IsOnline` and `IsVisible`, replaces the connectivity and visibility readings every
  `RetryPolicy` and `Hooks.UseMutation` call pauses on, for an application that knows better than
  `Application.internetReachability`.
