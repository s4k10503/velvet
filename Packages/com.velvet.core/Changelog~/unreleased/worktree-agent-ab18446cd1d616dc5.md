### Added

- Every `V.*` factory that returns an element node takes an `events:` array of `FiberEventBinding`s, as
  `V.Motion` already did, so a pointer, key, focus, wheel or geometry handler goes on a `V.Div`, a
  `V.SceneView` or a control directly, the way a React host element takes `onPointerDown`, instead of on a
  `V.Motion` wrapper that animates nothing. A factory's own `onClick:` or `onValueChanged:` is bound ahead of
  the array. The array is never written, and once the pool is warm a factory handed one allocates nothing
  the same call without it does not. The `Velvet.Experimental` builders carry it as `Events`. The migration
  guide's event-props section states the rule.
