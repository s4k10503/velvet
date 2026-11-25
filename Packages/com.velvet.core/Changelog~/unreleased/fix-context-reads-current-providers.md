### Fixed

- A component in a `V.VirtualList` row, or a `V.Portal`'s child, that renders on its own — for its own update, a
  context it reads changing, a time-sliced render of it resuming, or an error boundary's fallback in it — reads a
  `V.Provider` above the list or Portal with the value that Provider has now. It read the value the Provider had when
  the row or child was created where a `Memoize` component between them had bailed out of a change to that Provider,
  where the component rendering the list or Portal had since put a new value around it, and where the list's renderer
  had since put a new value around the row. The children of a `V.Portal` that such a render mounts read those values
  too.
- A row a scroll mounts reads a `V.Provider` the component rendering the `V.VirtualList` put around it, as React reads
  the nearest Provider, where it read a farther `V.Provider` of the same context above that component. Where that
  component had changed the Provider's value while handing back the same element instance around the list, and no
  mounted component below read the Provider, the row read the value from before the change.
