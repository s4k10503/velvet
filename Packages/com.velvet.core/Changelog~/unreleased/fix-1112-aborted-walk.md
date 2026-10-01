### Fixed

- An error boundary that catches an element callback's error, such as one an `onCreated` throws, while a
  component below it renders an update keeps its fallback on screen: the rest of that update's render no longer
  rewrites a row it matched by position, which after the catch is a row behind the boundary, as React keeps the
  rows around a boundary's fallback.
- An `AnimatePresence` that such an update's render reached after the catch keeps the children it had committed,
  and its `onExitComplete` does not run for that render. It used to drop a child it was removing from its record
  while the child's row stayed, leaving that row on screen for every later render.
