### Changed

- The `UseEffect` cleanups of a component a render removes run after that render's commit, in the passive phase
  React runs a deletion's passive cleanups in, after the commit's layout effects; they used to run during the
  render that removed the component. A removed component's run ahead of those of the components it rendered, and
  among the commit's other `UseEffect` cleanups where React's walk enters the component or element the component was
  removed from. `MountedTree.Dispose` still runs them before it returns, as React's `root.unmount()` does, and
  `MountOptions.OnUncaughtError` is now called before them, as React calls `onUncaughtError`.
- A removed subtree is taken down in one walk in the order React's deletion takes it: a component's
  `UseInsertionEffect` and `UseLayoutEffect` cleanups run where the walk reaches the component, ahead of the
  components it rendered, and an element's `refCallback:` cleanup runs where the walk reaches the element, after
  the cleanups of the components above it and ahead of those inside it. The subtree's refs used to detach
  together, ahead of the cleanups of the components inside it that are not a `V.Portal`'s children, and a
  component that renders another directly cleaned up after it.
- The `ComponentStack` an error from a removed component's `UseEffect` cleanup is reported with runs from that
  component up to the root of what the render removed, as React's stack stops at a deletion's root; it used to run up
  to the mounted root.
- On mount, an element with a `refCallback:` that a `V.Portal`'s child renders after a component attaches its ref
  after that component's layout effects, as React's layout phase reaches it; it used to attach first.
- A `V.VirtualList` row inside a `V.Portal` runs its effects ahead of a component after the portal when both
  re-render in one commit, as React's commit reaches the portal's children where the portal sits; it used to run
  them after.
