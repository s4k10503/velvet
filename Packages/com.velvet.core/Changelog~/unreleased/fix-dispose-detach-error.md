### Fixed

- An exception a `DetachFromPanelEvent` callback threw while `MountedTree.Dispose` removed the tree's elements left
  `Dispose`, and the rest of the unmount did not run: the root kept what it held, and the errors its effect cleanups
  had thrown were never reported. A handle disposed while a root rendered, committed or ran its effects logged
  the exception instead. `Dispose` now runs the whole unmount and hands the exception to
  `MountOptions.OnUncaughtError` once it is done, beside the errors of those cleanups, as React reports what a
  removal throws during `root.unmount()` after the unmount commits. Two such exceptions are both reported.
- An exception a `DetachFromPanelEvent` callback threw as an update removed an element a `z-*` class relocates, or a
  row of a `V.VirtualList` it removed, was thrown out of the update instead of reaching an error boundary, and the
  relocated elements or rows the update removed after it were not detached from the panel. It now goes where the
  error of any other removal in that update goes, and each of those elements is removed, as React captures what each
  removal throws and goes on with the rest.
- An element whose `DetachFromPanelEvent` callback threw as an update or `MountedTree.Dispose` removed it had the
  callback run a second time where Velvet took it out of something again afterwards — its `clip-path-*` or
  `wrapElement` wrapper, a pooled `V.Button` or `V.Label` it sat in, or, for a `z-*` element, the row the removal
  took — and that second throw left the update or the `Dispose`. The callback now runs once, and its error is reported
  once.
- A pooled element, a `V.Button` say, whose own `DetachFromPanelEvent` callback threw as it was removed went back to
  the element pool still naming the panel it had left, to be handed to a tree that rented one. It no longer goes back
  to the pool.
