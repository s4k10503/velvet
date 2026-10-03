### Changed

- An `OperationCanceledException` thrown by a `UseMutation` `OnError` handler is logged with
  `Debug.LogException`, as any other exception the handler throws is. It was not logged.

- `VelvetTaskCompletionSource.SetException` and `VelvetTask.FromException` handed an
  `OperationCanceledException` fault the task with it, as a TaskCompletionSource's `SetException` and
  Task.FromException do, where they cancelled it. `VelvetTask.WhenAll`, `AttachExternalCancellation`,
  `Preserve()` and `Forget()` treat such a task as faulted. An `async VelvetTask` method that throws one still
  ends cancelled, as an `async Task` method does.

- The `Task` `AsTask()` returns for a cancelled `VelvetTask` throws that task's own
  `OperationCanceledException`, as ValueTask.AsTask()'s does, where it threw a new `TaskCanceledException`.
