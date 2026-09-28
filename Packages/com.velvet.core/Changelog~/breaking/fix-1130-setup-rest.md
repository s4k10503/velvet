### Changed

- The panel Velvet creates for a `V.Portal(layer:)` or a `V.WorldSpace` gets the bundled utility stylesheet on
  its root, with the dark-theme binding `VelvetStyleUtilities.AttachTo` performs, when the portal's position on
  its declaring panel reaches the sheet and the host does not already. These hosts took the declaring panel's
  theme and no other stylesheet, so a sheet attached with `AttachTo` did not reach a layer or world-space
  portal's children, and the utility classes the sheet declares resolved to nothing there. Children styled
  around that gap now resolve those classes.
- A `V.Portal` into an element the app passes or registers logs the warning `V.Mount` gives for a panel
  without the utility stylesheet, when that element's panel lacks it. The warning is still made once per run.
