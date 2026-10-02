### Fixed

- A `VelvetTask.WhenAll` whose first cancelled member decides it throws that member's own
  `OperationCanceledException`, as `Task.WhenAll` does, where it threw a new one carrying the member's token.

- An `async VelvetTask` method that throws an `OperationCanceledException` before it first suspends
  returns a task that throws that same exception, as an `async Task` method's does, where it threw a new one
  carrying its token.

- `VelvetTaskMethodBuilder.Task` and `VelvetTaskMethodBuilder<T>.Task` return the same task on every read
  for one call, as `AsyncTaskMethodBuilder`'s does. A read after the task had been consumed returned a
  pending task, and once another call of the same method took the pooled state machine, that call's task.
