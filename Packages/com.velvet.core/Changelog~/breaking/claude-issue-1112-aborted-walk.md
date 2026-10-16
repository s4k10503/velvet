### Changed

- An error that an element's `onCreated` or `wrapElement` throws while the element is created is a render error,
  as these callbacks are reads the element's render takes: an error boundary the same render is rendering at or
  above the element catches it, and the rest of that render commits around the fallback, as React commits the
  siblings of a boundary whose children threw while rendering. Rows the render adds, drops or moves beside the
  boundary, and the siblings after it, take that render's content, and nothing of the failed subtree commits. The
  catch used to abort that render, which left some of those rows as the previous render had committed them: a
  sibling after the boundary, a row the render added, dropped or moved, or the row of a child that an
  `AnimatePresence` after the boundary was removing, which stayed on screen. A boundary catching such an error as
  it mounted showed neither its fallback nor a sibling of the failing element.

- An error boundary catches the error of an element it renders itself, from the element's `onCreated`,
  `wrapElement` or `refCallback`, as React captures an element's error from the element's parent. The search used
  to start above that boundary. An element of the boundary's fallback still leaves its error to the boundary
  above.

- An `AnimatePresence`'s `onExitComplete` runs once the render that removed its last child has ended, not while
  that render is still going on. An error it throws goes to the nearest error boundary at or above the component
  rendering the presence, as Framer Motion's `PresenceChild` calls it from an effect. Where an exit played, or a
  `V.Motion` child is among the children removed, no boundary takes the error and it is logged, as Framer Motion
  calls it from that exit's promise. It used to go to the nearest boundary above the component rendering the
  presence, and a render removing a child with no exit animation stopped at that catch.

- An error a `V.Motion`'s `onEnterComplete` throws reaches no error boundary and is logged, as Framer Motion's
  `onAnimationComplete` runs in a promise. It used to go to the nearest boundary above the component rendering the
  Motion.

### Fixed

- When an element callback fails during a component's own update and the error boundary that catches it is above
  that component, the rest of the update's render no longer writes its later rows onto the rows the boundary's
  fallback left in their place. A sibling after the boundary showed the updating component's row instead of its
  own.
