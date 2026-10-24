### Changed

- `MutationOptions`' `OnMutate`, `OnSuccess`, `OnError` and `OnSettled`, on all four option records, return
  a `VelvetTask` (`OnMutate` a `VelvetTask<TContext>`) instead of `Action`s and a plain context, and the
  call waits for each before the next stage, as TanStack Query v5 awaits the promise they return: the context
  is the task's result, a faulted task fails the call, and the handle is committed after the last callback.
  A callback with nothing to wait for returns `VelvetTask.CompletedTask`, or is written `async`.
- `QueryOptions.PlaceholderData` is handed `(previousData, previousQuery)`, the previous entry as a nullable
  `QueryInfo`, instead of the previous `QueryKey`; the key is `previousQuery.QueryKey`. The signature of
  `QueryPlaceholder.KeepPreviousData` follows.
- `QueryOptions.StructuralSharing` is a `Func<object?, object, object>`, as v5's takes `unknown`, and a
  function given there is now also handed what a `Select` returns, which it was not when the type changed.
- Structural sharing reaches into the classes and records an application declares: an instance whose
  fields are all equal to the held one's is the held instance, and a changed one is a copy carrying the held
  instance of each equal field. Data that compared as a new instance, an equal record from a refetch for
  one, now keeps the earlier instance, so a component comparing it by reference no longer re-renders.
- `QueryClient.InvalidateQueries` returns a `VelvetTask` that completes once the refetches it started have
  settled.
