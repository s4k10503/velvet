### Fixed

- A memoized component that suspended on `Hooks.Use` under a `V.Suspense`, and had an update queued while it
  waited, renders once when its resource resolves rather than twice, unless the render the resolve gives it
  asks for a further render of its own (a `Hooks.UseDeferredValue` whose input changed, for one). That render
  now satisfies the update, so the boundary's reveal reuses it instead of finding the component still marked
  for one; a lane the render itself asks for again survives it. A component holding a transition's work, and the component a `V.VirtualList` item renderer
  returns, keep the earlier behaviour.
