# VelvetTask

Velvet ships `VelvetTask` / `VelvetTask<T>` as its awaitable type.

## What completes synchronously in EditMode

- `await VelvetTask.FromResult(value)` (and other already-completed tasks)
- `await` on a `VelvetTaskCompletionSource` completed on the same call stack before the await

## Frame-bound continuations in EditMode

`VelvetTask.Yield()` and any `async VelvetTask` method that awaits it resume on the next frame
boundary: `EditorApplication.update` while the editor is not in Play Mode, and a dedicated
PlayerLoop `Update` pass in Play Mode and in player builds.

An `Awaitable` awaited inside an `async VelvetTask` (for example `Awaitable.EndOfFrameAsync()`) is
driven by neither hook, and moving a test to PlayMode does not settle it on its own:
`AwaitableSecondConsumePlayModeTests` pins `Awaitable.EndOfFrameAsync()` as still not completed
after three PlayMode frames. Drive a frame-spanning test with `VelvetTask.Yield()`, which both hooks
do complete, from a `[UnityTest]` — `VelvetTask.ToCoroutine` turns an `async VelvetTask` body into
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

A continuation that resumed off the main thread must not call back into Velvet. The pools it rents
task sources and state-machine runners from, and the frame driver's queues, are plain collections
that nothing synchronizes, and `VelvetTask.Yield()` is not a way back out: scheduling one appends to the same list
the main-thread drain swaps and clears. Keep off-thread work inside the awaited `Task` and await it
without `ConfigureAwait(false)`, so the continuation is on the main thread before it reaches any of
that.

## Awaiting several tasks at once

`VelvetTask.WhenAll` takes a list of tasks and completes once every one of them has settled. Over
`VelvetTask` members it completes carrying nothing; over `VelvetTask<T>` members it completes with a
`T[]` holding each result at its own argument position, whatever order the members arrived in. Over
an empty list it is complete already. The generic form's members share one result type, so a loader
fetching two different types awaits them one at a time.

A member that fails does not end the wait — the others are still waited for. What awaiting the
combination then throws is the first fault in argument order, or, where no member faulted, the first
cancellation in argument order: a fault outranks a cancellation whichever arrived first. That one
exception is all the combination carries, so await the members separately where each failure matters.

The combination consumes each member, and one consume is all a `VelvetTask` carrying a source allows.
So a member must not also be awaited elsewhere, and must not be passed twice into a single call: that
throws out of the call rather than out of the await. A task that carries a value instead —
`VelvetTask.FromResult`, `VelvetTask.CompletedTask`, and an `async` method that returned without
suspending — has no version to consume, so the same one may sit at two argument positions. Consume
what the combination returns once as well.

The main-thread constraint above covers the combination too: it completes on whichever thread
completed its last member, so a member that resumed off the main thread publishes from there.

## Declining a cancellation

There is no await that suppresses the cancellation throw. A cancelled task throws
`OperationCanceledException` when it is awaited, and `catch` is what declines to propagate it. A
route loader is handed a token to pass down, and can decide there what an abandoned load leaves
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
