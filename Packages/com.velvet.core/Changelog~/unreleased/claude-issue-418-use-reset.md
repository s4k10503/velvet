### Added

- `Hooks.UseErrorBoundaryReset(resetKeys)`, react-error-boundary's `resetErrorBoundary` and `resetKeys` for a
  `[Component(IsErrorBoundary = true)]` component. The action it returns makes a boundary showing its fallback
  render its children again, and a render passing reset keys that differ from the previous render's does the
  same. The children mount again, so a failed `Hooks.Use` load among them runs again: this is how such a load
  is retried.
