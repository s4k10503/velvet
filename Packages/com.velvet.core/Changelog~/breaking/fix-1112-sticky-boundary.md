### Changed

- An error boundary — `V.ErrorBoundary`, or a `[Component(IsErrorBoundary = true)]` registering
  `Hooks.UseFallback` — keeps showing its fallback once it has caught, until it remounts, as React's does. A
  later render of the boundary, its parent's or its own, renders the fallback again with the error it caught.
  It used to render its children again on the next render that did not throw. To show the children again,
  give the boundary a new `key`, which remounts it.

- An error thrown below a route's `element` — while rendering, from an effect, or from an element callback —
  renders the `errorElement` of the nearest route at or above it that declares one, and the root's default
  one where none does, as React Router's `RenderErrorBoundary` does; `Hooks.UseRouteError` returns it there.
  It used to propagate to the nearest error boundary above the router. The error clears when the router
  publishes a new location: a navigation, or a `Suspend` loader of the current location settling.

- After a throw while rendering its value, a `V.Await` keeps its `errorElement` for the deferred values it is
  handed next, as React Router's `AwaitErrorBoundary` does, until it remounts. It used to render the next
  value.
