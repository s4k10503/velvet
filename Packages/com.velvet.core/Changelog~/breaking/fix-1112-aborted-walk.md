### Changed

- An error that an element's `onCreated` or `wrapElement` throws while the element is created is caught, by an
  error boundary the same render is rendering above it, the way an error thrown while rendering is: the
  boundary shows its fallback and the rest of that render commits around it, as React commits the siblings of
  a boundary that caught. Rows the render adds, drops or moves beside the boundary, and the siblings after it,
  take that render's content. The catch used to abort that render, which left some of those rows as the
  previous render had committed them: a sibling after the boundary, a row the render added, dropped or moved,
  or the row of a child that an `AnimatePresence` after the boundary was removing, which stayed on screen. A
  boundary catching such an error as it mounted showed neither its fallback nor a sibling of the failing element.

### Fixed

- When an element callback fails during a component's own update and the error boundary that catches it is
  above that component, the rest of the update's render no longer writes its later rows onto the rows the
  boundary's fallback left in their place. A sibling after the boundary showed the updating component's row
  instead of its own.
