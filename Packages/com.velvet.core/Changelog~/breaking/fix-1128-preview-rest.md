### Changed

- `VelvetPreviewRegistry.RunSetupFor` returns a `VelvetPreviewEnvironment`, whose `StyleSheets` holds the
  `VelvetStyleHints.PreviewStyleSheet` each setup published, in setup order, and whose `Dispose` runs the
  setups' teardowns in reverse order. It used to return an `IDisposable`, or `null` when no setup supplied a
  teardown, and left only the last setup's sheet in `VelvetStyleHints.PreviewStyleSheet` for its caller to
  read. The hint is now consumed as each setup returns, so it reads `null` after the call, and the method
  returns `null` only for a `null` assembly.
