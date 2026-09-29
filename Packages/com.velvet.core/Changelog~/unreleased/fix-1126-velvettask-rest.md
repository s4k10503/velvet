### Fixed

- `AttachExternalCancellation` settles as .NET's Task.WaitAsync does. Over a `VelvetTask.WhenAll` it keeps
  every member's fault, which `AsTask()` then hands on, where it kept the first alone. A cancellation of the
  task it waits for comes out as that task's own `OperationCanceledException`, where it came out as a new one
  carrying the attached token. It settles on the thread that completes that task. Attached on the main thread
  to a task another thread completed, it settled only once Unity ran the main thread's posted work, so
  waiting there on its `AsTask()` never returned. A task already complete comes back as it stands when the attached token is already
  cancelled, where it came back cancelled. A fault arriving after the attached token has cancelled is logged,
  where it was dropped.

- `Forget()` logs every fault a `VelvetTask.WhenAll` holds, where it logged the first alone, and logs nothing
  for a cancelled task, where it logged its `OperationCanceledException`.

- A `VelvetTaskCompletionSource`'s `Task` may be awaited and read any number of times, before and after it
  completes, as a TaskCompletionSource's may. A second awaiter while it was pending threw, and so did an
  await or a status read of a held task that had already been consumed.
