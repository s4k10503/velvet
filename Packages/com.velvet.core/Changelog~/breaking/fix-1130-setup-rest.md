### Changed

- The panel Velvet creates for a `V.Portal(layer:)` or a `V.WorldSpace` gets the bundled utility stylesheet on
  its root when the element the tree was mounted on, or an ancestor of it, reaches the sheet and the host does
  not reach it another way, looked at when the portal mounts, on each patch of the portal and at each update
  of the panel the tree was mounted on. Its root also takes a `dark` class, following `VelvetTheme.IsDark`
  when one of those elements is a root `VelvetStyleUtilities.BindThemeTo` bound. These hosts took
  the declaring panel's theme and no other stylesheet, so a sheet attached with `AttachTo` did not reach a layer or world-space
  portal's children, and the utility classes the sheet declares resolved to nothing there. Children styled
  around that gap now resolve those classes.
- A `V.Portal` into an element the app passes or registers logs the warning `V.Mount` gives for a panel
  without the utility stylesheet, when that element's panel lacks it. The warning is still made once per run.
- The missing-sheet warning `V.Mount` gives is decided at the target panel's next update, and again at the
  next update after the target is added to a panel, rather than when `V.Mount` is called. A sheet attached
  after `V.Mount` but before that update no longer draws the warning, and a test asserting no unexpected log
  sees it only when that panel updates inside the test.
