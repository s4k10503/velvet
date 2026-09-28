### Fixed

- `AttachExternalCancellation` settles as .NET's Task.WaitAsync does. Over a `VelvetTask.WhenAll` it keeps
  every member's fault, which `AsTask()` then hands on, where it kept the first alone. A cancellation of the
  task it waits for carries that task's own token rather than the attached one, and a task already complete
  comes back as it stands when the attached token is already cancelled, where it came back cancelled.

- `Forget()` logs every fault a `VelvetTask.WhenAll` holds, where it logged the first alone, and logs nothing
  for a cancelled task, where it logged its `OperationCanceledException`.

- `VelvetTaskCompletionSource.Task` and `VelvetTaskCompletionSource<T>.Task` are the same task on every read,
  so once it has been consumed a later read is consumed too. Each read built a new task that reported the
  old outcome and could be consumed again.
