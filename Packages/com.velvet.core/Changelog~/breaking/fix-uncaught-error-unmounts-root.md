### Changed

- An error no error boundary catches now unmounts the whole tree `V.Mount` mounted, as React's root does: its
  effect cleanups run as on any unmount, and the mount target is left without the tree's elements. It used to be
  logged while the tree stayed mounted, with the component that threw left showing its last committed output.
  This covers an error thrown while rendering, by an effect or its cleanup, by a ref callback, or by anything
  else Velvet hands to the nearest boundary; a click or other event handler's exception still reaches no
  boundary and unmounts nothing. Where a boundary's own fallback content throws and no boundary above catches
  that, only the fallback content's error is reported, as React discards the catch the boundary was showing.
  An effect cleanup that throws while `MountedTree.Dispose` runs is now reported rather than dropped.

### Added

- `MountOptions.OnUncaughtError`, React's `onUncaughtError` root option: called with the error and an
  `ErrorInfo` carrying the component stack once the tree has unmounted for an error no boundary caught. Without
  it the error is logged with `Debug.LogException`, as before.
