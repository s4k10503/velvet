### Changed

- `V.Mount` logs a warning, once per run, when its target panel's next update — or the next update after
  the target is added to a panel — finds no bundled utility stylesheet on the target or any ancestor,
  directly or through an `@import`.
  A panel without the sheet used to resolve every utility the sheet declares to nothing with no diagnostic,
  while the families Velvet realises in C# kept working. A test that asserts no unexpected log around a
  mount onto a panel without the sheet sees this one when that panel updates inside the test and the mount
  is the run's first such mount, so its outcome can hang on test order; attach the sheet with `VelvetStyleUtilities.AttachTo` first. A
  project that leaves the sheet's holder out of its builds under **Project Settings ▸ Velvet** gets no
  warning.
