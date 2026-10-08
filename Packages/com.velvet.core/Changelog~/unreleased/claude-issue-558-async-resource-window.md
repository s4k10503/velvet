### Fixed

- A memoized component that suspended on `Hooks.Use` under a `V.Suspense`, and had an update queued while it
  waited, renders once when its resource resolves rather than twice. The render the resolve gives it now
  satisfies the update, so the boundary's reveal reuses that render instead of finding the component still
  marked for one. A component holding a transition's work, and the component a `V.VirtualList` item renderer
  returns, keep the earlier behaviour.
