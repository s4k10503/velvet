### Changed

- A `Hooks.UseMutation` call whose component unmounts while it is in flight runs its callbacks, as TanStack
  Query v5's option callbacks outlive the observer, where it ran none. A mutation function that honours the
  token the unmount cancels ends in the `OperationCanceledException`, and `OnError` receives it — a request
  that may already have been applied, so a handler that rolls back should tell it apart by type; one that
  ignores the token runs `OnSuccess` or `OnError` when it completes. Neither writes the handle or re-renders
  the component, and `MutateAsync` still completes with default data on the cancellation.
