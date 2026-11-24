### Fixed

- In the Editor with `FiberStrictMode.Enabled`, a render whose output held a value whose `ToString` throws, such as a
  component's props, threw, because the StrictMode diagnostic read that value outside the guard around its second
  render. A component rendered within its parent's render passed the error to its parent: the nearest error boundary
  showed its fallback, or, with none, the tree unmounted. A component rendered for an update queued on it threw out of
  the update pass, and the components queued behind it in that pass were left unrendered; a later update of the same
  priority to one of them did not queue it again. The diagnostic now reads such an output as unreadable instead of
  throwing, and reports it as impure only when the other of its two renders produced a readable output. Separately, an
  exception that leaves an update pass as it renders a component now puts the components queued behind it back on the
  queue, with the updates they received since, as React keeps a root's updates pending until a render that takes them
  commits. Updates queued in a transition are kept the same way.
