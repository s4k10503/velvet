### Changed

- An error boundary — `V.ErrorBoundary`, or a `[Component(IsErrorBoundary = true)]` registering
  `Hooks.UseFallback` — keeps showing its fallback once it has caught, until it remounts, as React's does. A
  later render of the boundary, its parent's or its own, renders the fallback again with the error it caught.
  It used to render its children again on the next render that did not throw. To show the children again,
  give the boundary a new `key`, which remounts it.
