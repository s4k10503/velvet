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
do complete.

## Awaiting something that is not a VelvetTask

An `async VelvetTask` body awaits any awaiter C# accepts: `VelvetTaskMethodBuilder` and
`VelvetTaskMethodBuilder<T>` each declare both of the constraints the compiler emits an await
against — `AwaitOnCompleted` for `INotifyCompletion` and `AwaitUnsafeOnCompleted` for
`ICriticalNotifyCompletion`. So `await Awaitable.NextFrameAsync()` and `await someTask` compile and
resume inside one with no conversion and no wrapper, which
`VelvetTaskAwaiterInteropPlayModeTests` pins.

Where an `Awaitable` and a `Task` part is who resumes the continuation. A BCL `Task` resumes through the synchronization
context captured at the `await`: awaited on Unity's main thread it comes back on the main thread, and
the same await written `ConfigureAwait(false)` comes back off it. That fixture pins both of those too.

There is no combinator for awaiting several tasks at once, and no await that suppresses the
cancellation throw.

See [react-migration.md](react-migration.md) for how async hooks and routing loaders map from React.
