### Added

- `MountOptions.OnUncaughtError`, React's `onUncaughtError` root option: called with the error and an
  `ErrorInfo` carrying the component stack once the tree has unmounted for an error no boundary caught. Without
  it the error is logged with `Debug.LogException`, as before, and in the Editor and a development build a warning
  naming the component that threw follows it, as React's development build warns.
