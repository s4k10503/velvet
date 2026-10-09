### Added

- `Hooks.UseErrorBoundaryReset(resetKeys, onReset)`, react-error-boundary's `resetErrorBoundary`, `resetKeys` and
  `onReset` for a `[Component(IsErrorBoundary = true)]` component. The `ErrorBoundaryReset` it returns makes a
  boundary showing its fallback call `onReset` with the arguments it was invoked with and queue its reset as a
  state update, on the lane the call is made on, after which it renders its children again. Reset keys that
  differ from the last committed render's do the same from the commit, as the library does from
  `componentDidUpdate`. The children mount again, so a failed `Hooks.Use` load among them runs again: this is
  how such a load is retried.
- `Hooks.UseErrorBoundary()`, react-error-boundary's `useErrorBoundary()`. `ShowBoundary(error)`, from an event
  handler or an async continuation, makes the caller throw `error` on its next render, which reaches the
  boundaries above it as any render error does: the first that shows a fallback for it catches it, and one that
  declines passes it up. `ResetBoundary()` resets the nearest error boundary above the caller, passing over the
  ones Velvet renders for routing.
- `V.ErrorBoundary(fallbackRender: (error, reset) => …, children, resetKeys, onReset)`, react-error-boundary's
  `fallbackRender` with `resetErrorBoundary`.
