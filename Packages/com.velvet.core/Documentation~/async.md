# VelvetTask

Velvet ships `VelvetTask` / `VelvetTask<T>` as its awaitable type.

## What completes synchronously in EditMode

- `await VelvetTask.FromResult(value)` (and other already-completed tasks), except inside a
  `startTransition` callback, where it resumes later — the `useTransition` row of
  [react-migration.md §1](react-migration.md#1-hooks-mapping) says when
- `await` on a `VelvetTaskCompletionSource` completed on the same call stack before the await

## Frame-bound continuations in EditMode

`VelvetTask.Yield()` and any `async VelvetTask` method that awaits it resume on the next frame
boundary: `EditorApplication.update` while the editor is not in Play Mode, and a dedicated
PlayerLoop `Update` pass in Play Mode and in player builds.

An `Awaitable` awaited inside an `async VelvetTask` resumes the method when Unity completes the
`Awaitable`, not on either hook: an `AwaitableCompletionSource` set in EditMode resumes it before any
editor update, which `VelvetTaskEditModeContinuationTests` pins. Velvet completes no `Awaitable`
itself, so one Unity leaves pending stays pending: `AwaitableSecondConsumePlayModeTests` creates
`Awaitable.EndOfFrameAsync()` with no `VelvetTask` involved and pins it as still not completed after
three PlayMode frames. Drive a frame-spanning test with `VelvetTask.Yield()`, which both hooks do
complete, from a `[UnityTest]` — `VelvetTask.ToCoroutine` turns an `async VelvetTask` body into
the `IEnumerator` such a test returns. A synchronous `[Test]` body does not return to either hook
between its own await and its assertion, so a `Yield()` awaited there is still pending when the
assertion reads it.

## Awaiting something that is not a VelvetTask

An `async VelvetTask` body awaits any awaiter C# accepts: `VelvetTaskMethodBuilder` and
`VelvetTaskMethodBuilder<T>` each declare both of the constraints the compiler emits an await
against — `AwaitOnCompleted` for `INotifyCompletion` and `AwaitUnsafeOnCompleted` for
`ICriticalNotifyCompletion`. So `await Awaitable.NextFrameAsync()` and `await someTask` compile and
resume inside one with no conversion and no wrapper, which
`VelvetTaskAwaiterInteropPlayModeTests` pins.

A BCL `Task` resumes through the synchronization context captured at the `await`: awaited on Unity's
main thread it comes back on the main thread, and the same await written `ConfigureAwait(false)`
comes back off it. That fixture pins both of those too.

## Leaving the main thread and coming back

A `VelvetTask` awaited on the main thread resumes there, whichever thread completes it. A completion
arriving from another thread does not run the continuation on that thread: it hands it to Unity's
main-thread synchronization context, and it runs the next time Unity executes the work posted there.
So a route loader that returns after awaiting `ConfigureAwait(false)` still hands its result to the
router on the main thread. `VelvetTaskMainThreadEditorTests` and `VelvetTaskMainThreadPlayModeTests`
pin this.

What stays off the main thread is the rest of your own method after such an await. It may await
other `VelvetTask`s there, `VelvetTask.Yield()` among them: off the main thread it resumes where
`Task.Yield()` does — through that thread's synchronization context, else through a task scheduler other
than the default, else on the thread pool — which `VelvetTaskMainThreadEditorTests` pins. The rest of Velvet — hooks,
stores, `V.Mount` — is for the main thread only. Await
`VelvetTask.SwitchToMainThread()` before reaching any of them: on the main thread it completes at
once, and elsewhere it resumes the method on the main thread the way a completion from another thread
does.

## Awaiting several tasks at once

`VelvetTask.WhenAll` takes a list of tasks and completes once every one of them has settled. Over
`VelvetTask` members it completes carrying nothing; over `VelvetTask<T>` members it completes with a
`T[]` holding each result at its own argument position, whatever order the members arrived in. Over
an empty list it is complete already.

Members of different result types go to the form carrying nothing, as `Task<T>` members go to
`Task.WhenAll(Task[])`: a `VelvetTask<T>` converts implicitly to a `VelvetTask` sharing its source, so
consuming either consumes both. So preserve each member, pass the preserved tasks to
`VelvetTask.WhenAll`, and once the combination has been awaited, await each preserved task for its
result.

A member that fails does not end the wait — the others are still waited for, as `Task.WhenAll` waits.
Awaiting the combination then throws what awaiting a `Task.WhenAll` throws: the first fault in argument
order — the member's own exception — or, where no member faulted, an `OperationCanceledException`
carrying the token of the first cancelled member in argument order, so a fault outranks a cancellation
whichever arrived first. The combination keeps every member's fault in argument order, a member that is
itself a faulted combination contributing all of its own, and a cancelled member contributing none.
`AsTask()` hands them to the `Task`'s `Exception` as `Task.WhenAll` holds them, so await
`VelvetTask.WhenAll(…).AsTask()` where each failure matters. `AttachExternalCancellation` keeps all of them,
as Task.WaitAsync does, and `Forget()` logs each one. `Forget()` logs nothing for a cancelled task.
`AttachExternalCancellation` settles on the thread that completes the task it waits for, and a
cancellation of that task comes out as the task's own `OperationCanceledException`. A fault that arrives
after the attached token has cancelled is logged, as `Forget()` logs it.

The combination consumes each member, and a `VelvetTask` carrying a source allows one consume, the rule
.NET's ValueTask carries — unless `Preserve()` returned it or it is a `VelvetTaskCompletionSource`'s. So
any other member must not also be awaited elsewhere, and must not be passed twice into a single call:
that throws out of the call rather than out of the await. A task that carries a value instead —
`VelvetTask.FromResult`, `VelvetTask.CompletedTask`, and an `async` method that returned without
suspending — has no version to consume, so the same one may sit at two argument positions, as a
preserved one may. Consume what the combination returns once as well.

A `VelvetTaskCompletionSource`'s `Task` is the same task on every read, and any number of awaiters may
await and read it, before and after it completes, as they may a TaskCompletionSource's.
`VelvetTaskDoubleConsumeEditorTests` pins it.

Where a task has to be consumed more than once, `Preserve()` — the counterpart of
ValueTask.Preserve() — consumes it and returns one that any number of awaiters may await and read, a
continuation registered on the main thread resuming there as any other does. `AsTask()` returns a
`Task` or `Task<T>` instead. Each settles on the thread that completes the original task, whichever
thread asked for it, so a preserved task's status moves and an `AsTask()` result can be waited on from
the main thread before Unity runs anything posted there. `VelvetTaskPreserveEditorTests` and `VelvetTaskAsTaskEditorTests` pin both.

## Declining a cancellation

`await task.SuppressThrowing()` waits for the task and throws neither its fault nor its cancellation,
the await ConfigureAwaitOptions.SuppressThrowing gives a `Task`. It returns the task's
`VelvetTaskStatus`; on a `VelvetTask<T>` the result is dropped. A misuse still throws — a task consumed elsewhere, or one read
before it completes. `VelvetTaskSuppressThrowingEditorTests` pins it.

Awaited without it, a cancelled task throws `OperationCanceledException`, and `catch` is what declines
to propagate it. A route loader is handed a token to pass down, and can decide there what an abandoned load leaves
behind:

```csharp
static async VelvetTask<object> LoadDashboard(RouteLoaderContext context, CancellationToken cancellationToken)
{
    try
    {
        var id = context.Params["id"];
        var pages = await VelvetTask.WhenAll(
            FetchJson($"/users/{id}", cancellationToken),
            FetchJson($"/users/{id}/feed", cancellationToken));
        return new Dashboard(pages[0], pages[1]);
    }
    catch (OperationCanceledException)
    {
        return Dashboard.Empty;
    }
}
```

See [react-migration.md](react-migration.md) for how async hooks and routing loaders map from React.
