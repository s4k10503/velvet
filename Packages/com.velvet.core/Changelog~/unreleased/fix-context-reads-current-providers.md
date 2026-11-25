### Fixed

- A component in a `V.VirtualList` row, or a `V.Portal`'s child, that renders on its own — for its own update, a
  context it reads changing, a time-sliced render of it resuming, or an error boundary's fallback in it — reads the
  value a `V.Provider` above the list or Portal has now where it read the value that Provider had when the row or child
  was created: where a `Memoize` component between them had bailed out of a change to that Provider, where the
  component rendering the list or Portal had since put a new value around it, and where the list's renderer had since
  put a new value around the row. The children of a `V.Portal` that such a render mounts read those values too. This
  holds where no component on the way down places one node instance at two places.
- A row a scroll mounts reads a `V.Provider` the component rendering the `V.VirtualList` put around it, as React reads
  the nearest Provider, where it read a farther `V.Provider` of the same context above that component. Where that
  component had changed the Provider's value while handing back the same element instance around the list, and no
  mounted component below read the Provider, the row read the value from before the change.
- A row a scroll mounts in a `V.VirtualList` whose host sits in a `V.Suspense` primary hidden behind the fallback reads
  a `V.Provider` inside that primary, where it read a farther `V.Provider` of the same context above the `V.Suspense`.
- A component inside a child a `V.AnimatePresence` is still animating out that renders on its own reads a
  `V.Provider` inside that child or one the component rendering the `V.AnimatePresence` put around it, where it read
  a farther `V.Provider` of the same context above that component, or the context's default value where there was
  none. This holds where the component rendering it places that `V.AnimatePresence` node once.
- Once a `V.Provider` a `V.VirtualList`'s renderer returns around a row had changed value with no render walking
  around that row — a component whose output is the list and nothing else is one such, a scroll that renders the row
  is another — a component under the same `V.Mount` that reads a context was told of at most one later change to a
  `V.Provider` it reads, so a `Memoize` component kept the value it read then. React tells a context's consumers of
  every change.
