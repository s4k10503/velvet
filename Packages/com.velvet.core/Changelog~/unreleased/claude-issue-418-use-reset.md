### Added

- `Hooks.UseErrorBoundaryReset(resetKeys, onReset)`, react-error-boundary's `resetErrorBoundary`, `resetKeys` and
  `onReset` for a `[Component(IsErrorBoundary = true)]` component. The `ErrorBoundaryReset` it returns makes a
  boundary showing its fallback call `onReset` with the arguments it was invoked with and render its children
  again; reset keys that differ from the previous render's do the same from the commit, as the library does
  from `componentDidUpdate`. The children mount again, so a failed `Hooks.Use` load among them runs again: this
  is how such a load is retried.
- `Hooks.UseErrorBoundary()`, react-error-boundary's `useErrorBoundary()`: `ShowBoundary(error)` makes the
  nearest error boundary above the caller catch `error`, from an event handler or an async continuation, and
  `ResetBoundary()` resets that boundary.
- `V.ErrorBoundary(fallbackRender: (error, reset) => …, children, resetKeys, onReset)`, react-error-boundary's
  `fallbackRender` with `resetErrorBoundary`.
