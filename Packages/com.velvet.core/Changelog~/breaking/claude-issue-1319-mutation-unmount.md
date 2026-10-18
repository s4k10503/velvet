### Changed

- Unmounting the component of a `Hooks.UseMutation` call in flight no longer cancels the `CancellationToken`
  its `MutationFn` received, as TanStack Query v5 never cancels a mutation: the function runs to its own
  completion, and `OnSuccess`, `OnError` and `OnSettled` receive its real outcome instead of an
  `OperationCanceledException`. A write the server applied is no longer reported as failed, so a rollback in
  `OnError` that relied on the cancellation to find unmounted calls no longer sees them. `MutateAsync`
  resolves or rejects with the function's own outcome, and a `MutateAsync` called after the unmount runs
  instead of rejecting with an `OperationCanceledException`, writing no handle.

### Added

- `MutateOptions.CancellationToken` is the token a call's `MutationFn` receives, for a caller that wants to
  cancel a request: only the caller cancels it. A function that honours it ends in the
  `OperationCanceledException`, which fails the call like any other.
