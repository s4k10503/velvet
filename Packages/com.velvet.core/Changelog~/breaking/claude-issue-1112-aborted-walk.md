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
  to start above that boundary. An element of the boundary's own fallback still leaves its error to the boundary
  above, where React's capture would offer it to the same boundary: that boundary would mount the same fallback
  again.

- An `AnimatePresence`'s `onExitComplete` for children removed with no exit to play runs from the passive-effect
  flush after the render that removed them, after that commit's layout effects, and no longer while that render
  is still going on. It does not run for a removal that render never committed. One whose exit plays runs when
  the last exit's animation ends. An error it throws goes to the nearest error boundary at or above the component
  rendering the presence, as Framer Motion's `PresenceChild` calls it from an effect. Where an exit played, or a
  `V.Motion` child is among the children removed, no boundary takes the error and it is logged, as Framer Motion
  calls it from that exit's promise. It used to go to the nearest boundary above the component rendering the
  presence, and a render removing a child with no exit animation stopped at that catch.

- An error a `V.Motion`'s `onEnterComplete` throws reaches no error boundary and is logged, as Framer Motion's
  `onAnimationComplete` runs in a promise. It used to go to the nearest boundary above the component rendering the
  Motion.

- An error a `V.Outlet`'s route-scope factory throws is a render error of the Outlet, as the Outlet's render reads the
  factory: an error boundary the same render is rendering at or above the Outlet catches it, and the rest of that
  render commits around the fallback. It used to stop that render at the catch, which left the rows after the
  boundary as the previous render had committed them.

### Fixed

- When an element callback fails during a component's own update and the error boundary that catches it is above
  that component, the rest of the update's render no longer writes its later rows onto the rows the boundary's
  fallback left in their place. A sibling after the boundary showed the updating component's row instead of its
  own.

- A render an abort stopped before its removals ran no longer leaves an `AnimatePresence` believing a child it could not
  remove is gone: the next render takes that removal, so the child's element leaves and `onExitComplete` runs once.
  It used to leave the element on screen with no later render removing it.

- An error thrown while a boundary's catch releases what the failed output mounted, such as a store subscription's
  unsubscribe, goes to the boundaries above that boundary, or is logged where there is none, and the boundary's fallback
  shows. It used to be caught by the boundary itself on the aborting path, which released the element being mounted
  around it and left the fallback unshown.
