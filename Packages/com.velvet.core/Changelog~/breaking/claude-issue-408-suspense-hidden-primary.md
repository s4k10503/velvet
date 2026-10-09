### Changed

- A `V.Suspense` that has shown its children and suspends again keeps them in the tree, hidden with an
  inline `display: none` ahead of its fallback, as React hides them, and reveals the same elements when it
  stops waiting. The components in them keep their state, a component inside a host element of the children
  or a `V.VirtualList` row among them included, and their passive effects stay connected; their layout
  effects and imperative handles are still taken down until the reveal. It used to remove those elements and
  dispose every component inside a host element of them, so the reveal created them again. A query over the
  tree, such as `Q<Label>()`, now finds the hidden elements while the fallback shows.

### Fixed

- A render given up because a component outside every Suspense suspended no longer leaves a Suspense it
  rendered recorded as showing its fallback while the screen still shows its children. Updates of the
  components in those children outside any host element of them used to be held back as if they were hidden.
