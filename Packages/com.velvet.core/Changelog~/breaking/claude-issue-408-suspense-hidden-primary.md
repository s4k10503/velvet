### Changed

- A `V.Suspense` that has shown its children and suspends again keeps them in the tree, hidden with an
  inline `display: none` ahead of its fallback, as React hides them, and reveals the same elements when it
  stops waiting. The components in them keep their state, a component inside a host element of the children
  or a `V.VirtualList` row among them included, and their passive effects stay connected; their layout
  effects, imperative handles and element refs are taken down until the reveal, as React 18 does, and a
  focused element inside them is blurred. A `V.Portal`'s children are hidden with them, and each hidden element
  gets back the inline `display` it had on the reveal. It used to remove those elements and dispose every
  component inside a host element of them, so the reveal created them again. A query over the tree, such as
  `Q<Label>()`, now finds the hidden elements while the fallback shows.

### Fixed

- A render given up because a component outside every Suspense suspended no longer leaves a Suspense it
  rendered recorded as showing its fallback while the screen still shows its children.
- A component written beside a `V.Suspense` in the component that renders it, rather than inside it, shows
  its resolved read once it resolves after the render it suspended was given up; it used to keep showing what
  it rendered before.
- A `V.Suspense` inside another one that the same component renders no longer makes the outer one show its
  fallback for a child only the inner one waits on.
- `gap-*` respaces a container's children at its next geometry change when one of them has become
  `display: none`, or stopped being so, with no child added or removed; it used to keep the spacing it had.
