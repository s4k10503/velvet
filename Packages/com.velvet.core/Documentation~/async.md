# VelvetTask and EditMode

Velvet ships `VelvetTask` / `VelvetTask<T>` as its awaitable type.

## What completes synchronously in EditMode

- `await VelvetTask.FromResult(value)` (and other already-completed tasks)
- `await` on a `VelvetTaskCompletionSource` completed on the same call stack before the await

## Frame-bound continuations in EditMode

`VelvetTask.Yield()` and any `async VelvetTask` method that awaits it resume on the next frame
boundary: `EditorApplication.update` while the editor is not in Play Mode, and a dedicated
PlayerLoop `Update` pass in Play Mode and in player builds.

Other `Awaitable` continuations wrapped by `VelvetTask` (for example `Awaitable.EndOfFrameAsync()`)
are driven by neither hook, and moving a test to PlayMode does not settle them on its own:
`AwaitableSecondConsumePlayModeTests` pins `Awaitable.EndOfFrameAsync()` as still not completed
after three PlayMode frames. Drive a frame-spanning test with `VelvetTask.Yield()`, which both hooks
do complete.

See [react-migration.md](react-migration.md) for how async hooks and routing loaders map from React.
