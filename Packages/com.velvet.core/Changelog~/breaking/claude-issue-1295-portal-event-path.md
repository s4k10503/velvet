### Changed

- An element that holds a portal's target without being a logical ancestor of the portal's call site, the element
  the portal renders into included, no longer runs its `events:` handlers for an event raised in the portal's
  content, in either the capture or the bubble phase, whichever reconciler rendered the element, as the container
  of a React portal never sees them. They used to run through the engine's own dispatch. A handler on a portal
  container that relied on hearing its content's pointer, wheel, key, focus, value-change or click events now has to
  sit on a logical ancestor of the `V.Portal` call site instead. A `Button`'s `onClick` runs unless the press and
  the release were both in that content, as a browser raises a click on the common ancestor of the two. An element that is both a physical and a logical
  ancestor still runs once in each phase, except for a pointer-down or pointer-up that a physical-only `Button`
  between the content and it consumes.
