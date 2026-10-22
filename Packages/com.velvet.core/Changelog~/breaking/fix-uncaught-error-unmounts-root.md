### Changed

- An error no error boundary catches now unmounts the whole tree `V.Mount` mounted, as React's root does: its
  effect cleanups run as on any unmount, and the mount target is left without the tree's elements. It used to be
  logged while the tree stayed mounted, with the component that threw left showing its last committed output.
  This covers an error thrown while rendering, by an effect or its cleanup, by a ref callback, or by anything
  else Velvet hands to the nearest boundary; a click or other event handler's exception still reaches no
  boundary and unmounts nothing. Where a boundary's own fallback content throws and no boundary above catches
  that, only the fallback content's error is reported, as React discards the catch the boundary was showing.
  An effect cleanup that throws while `MountedTree.Dispose` runs is now reported, once every cleanup has run,
  rather than dropped.
- An error a cleanup or ref throws while an error boundary swaps its fallback in, or while it disposes the output
  that failed, is now that boundary's own, as React's commit-phase capture starts at the nearest mounted ancestor:
  it is reported to `OnCaughtError` after the error the boundary caught, and the fallback renders again with it.
  It used to go to the boundaries above, replacing the boundary that had just caught.
- An error a cleanup throws in a component whose ancestor is already being torn down reaches the nearest
  boundary still mounted. It was dropped without a report, which happened to the children of a `V.VirtualList` row.
- `MountOptions` takes every option as an `init` property, as React's `createRoot` takes them as properties of one
  object: `new MountOptions(handler)` is written `new MountOptions { OnCaughtError = handler }`.
