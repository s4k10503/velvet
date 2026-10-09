### Added

- Every `V.*` factory that returns an element node takes an `events:` array of `FiberEventBinding`s, as
  `V.Motion` already did, so a pointer, key, focus, wheel or geometry handler goes on a `V.Div`, a
  `V.SceneView` or a control directly, the way a React host element takes `onPointerDown`, instead of on a
  `V.Motion` wrapper that animates nothing. `V.VirtualList` takes it for its `ScrollView`, and `V.Link` and
  `V.NavLink` for their button. A factory's own `onClick:` or `onValueChanged:` is bound ahead of the array,
  and one delegate passed both ways is bound once and left bound across renders.
  The array is never written, and once the pool is warm a factory handed one allocates nothing the same
  call without it does not. The `Velvet.Experimental` builders carry it as `Events`. The migration guide's
  event-props section states the rule.
- `Capture = true` on a pointer, wheel, key, focus or geometry binding is React's `on…Capture`: capture
  handlers run outermost first and ahead of every bubble handler, the target's own included, and a logical
  ancestor of a portal's call site captures before the portal's content.
- `ClickedEventBinding` hands its handler the click as a `ClickedEvent`, shared by every such binding on the
  button for that click. `ClickedEvent.PreventDefault()` is React's `event.preventDefault()`, and `V.Link` and
  `V.NavLink` skip their navigation for a click a handler in their `events:` prevented, as React Router's
  `Link` does.
