### Added

- `VelvetTask.Preserve()` and `VelvetTask<T>.Preserve()` consume a task once and return one that any
  number of awaiters may await and read, as .NET's ValueTask.Preserve() does; `AsTask()` returns a `Task` or
  `Task<T>` instead. Both settle on the thread that completes the original task. Any other task carrying a
  source, a `VelvetTaskCompletionSource`'s aside, still allows one consume, the rule a ValueTask carries.

- A `VelvetTask<T>` converts implicitly to a `VelvetTask` sharing its source, as a `Task<T>` is a
  `Task`, so members of different result types combine in one `VelvetTask.WhenAll` and are read from
  their preserved tasks afterwards.

- `await task.SuppressThrowing()` waits for a `VelvetTask` or `VelvetTask<T>` and throws neither its
  fault nor its cancellation, as ConfigureAwaitOptions.SuppressThrowing does for a `Task`, returning
  the task's `VelvetTaskStatus` in place of the result.

- `VelvetTask.WhenAll` keeps every member's fault in argument order, those of a faulted combination
  among its members included, as `Task.WhenAll` does. Awaiting it still throws the first;
  `AsTask()` hands all of them to the `Task`'s `Exception`.
