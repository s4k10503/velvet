### Fixed

- The components one batch of updates renders now render before their element changes, removals and removed
  components' cleanups apply, and their effects commit together, as React commits one render; so do the components
  one flush, mount or resumed time-sliced render renders. Before, each component's changes applied as soon as it had
  rendered, so a component rendering later in the batch could read an earlier one's new label text, or a value an
  earlier one's removed component wrote in its layout cleanup. A component that mounts a `V.Portal`'s or a
  `V.WorldSpace`'s children still applies the changes rendered so far before the next component renders.
- A component a transition's last work owes a render, to take its `UseOptimistic` entries down, now commits with the
  update that settled the transition: the refs that update replaces detach before that component's layout effects run.
  Where that component was also the one the transition updated, a layout effect whose dependency the transition
  changed now runs; before, the second render found the dependency already committed and ran neither the cleanup nor
  the setup.
- An error an element throws as it is added — from a panel-attach callback, say — now reaches the error boundary above
  the component that rendered it in a follow-up commit, as React captures an error its commit meets. Two such errors
  under one boundary are both reported to `OnCaughtError`, and the boundary renders its fallback once. Where a later
  component's render error replaced the first component with the boundary's fallback in the same batch, only that
  render error is reported, as in React; before, the first component's error was reported and the later component
  never rendered. An element that throws so during a mount now reaches the root once the mount's layout effects have
  run, and the root then unmounts what the mount committed, running its cleanups; before, the mounted elements were
  left in place.
