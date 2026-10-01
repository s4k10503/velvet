### Fixed

- An error boundary that catches a render error below it, while another component's render is rendering the
  boundary, replaces only its own output with its fallback, and the rest of that render commits, as React
  commits the rest of a render around a boundary's fallback. The catch used to abort the whole render: on mount
  a boundary inside an element left that element and everything around it unrendered and a sibling's passive
  effect never ran, and on an update the siblings the render had not reached kept their previous output. A
  boundary that is a `V.VirtualList` row, mounting in its host's render, no longer stops that render either.
  The fallback takes over no element the failed children held, and an `AnimatePresence` or a Portal target
  the failed children rendered into keeps nothing of them. One catch still stops the other render: a boundary
  that catches while finishing an update of its own a time-sliced pass had parked, which that render reached,
  discards that render as before. The catch is reported once the fallback has
  rendered. Where the fallback's own content throws, that error goes to the boundary above, as in React; the
  original error goes on after it only where no boundary above caught the content's, and is logged where there
  is none.

- A `V.AnimatePresence` child's enter that plays nothing — under `initial: false`, a child inheriting its
  enter from a `V.Motion` above the presence included, or a variant `V.Motion` with no `initial` — calls its
  `onEnterComplete` once the render that reached it has ended rather than while it is rendering, so a render
  an error boundary above it catches calls none, whether the boundary catches inside another component's
  render or on its own. A `V.VirtualList` row rendered
  as the list scrolls still calls it before the range update returns.
