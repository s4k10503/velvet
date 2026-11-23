### Fixed

- An exception a `DetachFromPanelEvent` callback threw while `MountedTree.Dispose` removed the tree's elements left
  `Dispose`, and the rest of the unmount did not run: the root kept what it held, and the errors its effect cleanups
  had thrown were never reported. A handle disposed while a root rendered, committed or ran its effects logged
  the exception instead. `Dispose` now runs the whole unmount and hands the exception to
  `MountOptions.OnUncaughtError` once it is done, beside the errors of those cleanups, as React reports what a
  removal throws during `root.unmount()` after the unmount commits. Two such exceptions are both reported.
- An exception a `DetachFromPanelEvent` callback threw as an update removed an element a `z-*` class relocates was
  thrown out of the update instead of reaching an error boundary, and the relocated elements the update removed after
  it stayed in their parent. It now goes where the error of any other removal in that update goes, and each of those
  elements is removed, as React captures what each removal throws and goes on with the rest.
