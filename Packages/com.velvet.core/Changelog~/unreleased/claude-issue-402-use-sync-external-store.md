### Added

- `Hooks.UseSyncExternalStore<T>(subscribe, getSnapshot)`, React's `useSyncExternalStore`, reads a store
  Velvet does not own without mirroring it into a `Store<T>`. A notification re-renders when the
  snapshot is not `Object.is`-equal to the rendered one or `getSnapshot` throws, on the Urgent lane and
  never the Transition lane, flushed from the main thread's posted work; a `getSnapshot` that builds a
  new snapshot per read re-renders until the update-depth limit drops the update. A `subscribe` that is
  not equal to the previous render's re-subscribes, and unmounting unsubscribes. The change callback
  must be invoked on the main thread, and readers with equal `getSnapshot` delegates share one snapshot
  within one batch drain pass, as `Hooks.UseStore` readers of one store do. The scheduler's resume of a
  parked time-sliced pass flushes such a re-render first.

### Changed

- The error for a component rendering more or fewer store hooks than before names the kind
  `UseStore / UseSyncExternalStore`, since the two hooks share one slot list.

### Fixed

- A `Hooks.UseStore` reader rendered later in a drain pass than a reader that pinned a `null` snapshot now
  reads that pinned snapshot, as it already did for a non-null one, instead of the store's newer value.
