### Added

- `Deferred<T>`, `V.Await`, `Hooks.UseAsyncValue` and `Hooks.UseAsyncError`: React Router's deferred loader
  data, `<Await>`, `useAsyncValue` and `useAsyncError`. A loader returns a `Deferred<T>` inside its data so
  the navigation commits without waiting for it, and `V.Await` renders the value once it arrives,
  suspending to the nearest `V.Suspense` until then.

### Fixed

- A `Router`'s history keeps every entry, as a memory router's does. It used to drop its oldest entry
  once it held 50, so walking back from a long session stopped short of where it began.

- A `V.RouterProvider` handed a different router publishes that router's location in the render that
  hands it over. It used to publish the previous router's location until its effect subscribed to the
  new one.
