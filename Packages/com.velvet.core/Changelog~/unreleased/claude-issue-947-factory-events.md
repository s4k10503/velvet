### Added

- Every `V.*` factory that returns an element node, other than the class-and-children shorthands, takes an
  `events:` array of `FiberEventBinding`s, as `V.Motion` already did, so a pointer, key, focus, wheel or
  geometry handler goes on a `V.Div`, a `V.SceneView` or a control directly, the way a React host element
  takes `onPointerDown`, instead of on a `V.Motion` wrapper that animates nothing. Those shorthands, of
  `V.Div`, `V.Custom<T>`, `V.ScrollView` and `V.Button`, each gain a sibling taking the array,
  `V.VirtualList` takes it for its `ScrollView`, and `V.Link` and `V.NavLink` for their button. A
  factory's own `onClick:` or `onValueChanged:` is bound ahead of the array, and one delegate passed both
  ways is bound once and left bound across renders. The array is never written, and once the pool is warm
  a factory handed one allocates nothing the same call without it does not. The `Velvet.Experimental`
  builders carry it as `Events`. The migration guide's event-props section states the rule.
- `Capture = true` on a pointer down, up or move, wheel, key, focus in or out, focus or blur binding is
  React's `on…Capture`: capture handlers run outermost first and ahead of every bubble handler, the target's
  own included. Across a portal, a logical ancestor of the call site captures before the portal's content
  and bubbles after it, once however many portals' content the target's physical path crosses, and a
  pointer-down or pointer-up the layer router hands to a layer portal's content reaches its logical
  ancestors in both phases.
- `ClickedEventBinding` hands its handler the click as a `ClickedEvent`, shared by every such binding on the
  button for that click. `ClickedEvent.PreventDefault()` is React's `event.preventDefault()`, and `V.Link` and
  `V.NavLink` skip their navigation for a click a handler in their `events:` prevented, as React Router's
  `Link` does.
