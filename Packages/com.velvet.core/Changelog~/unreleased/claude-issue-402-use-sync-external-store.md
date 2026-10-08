### Added

- `Hooks.UseSyncExternalStore<T>(subscribe, getSnapshot)`, React's `useSyncExternalStore`, reads a store
  Velvet does not own without mirroring it into a `Store<T>`. A notification re-renders when the
  snapshot is not `Object.is`-equal to the rendered one or `getSnapshot` throws; a `subscribe` that is
  not equal to the previous render's re-subscribes, and unmounting unsubscribes. The change callback
  must be invoked on the main thread and never schedules on the Transition lane, and readers with equal
  `getSnapshot` delegates share one snapshot across a frame's urgent and deferred renders.

### Fixed

- A `Hooks.UseStore` reader rendered on the Transition lane now reads the snapshot the frame's urgent
  readers pinned when that snapshot is `null`, as it already did for a non-null one, instead of the
  store's newer value.
