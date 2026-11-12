### Fixed

- A `V.VirtualList` row that a scroll or a change of the list's size mounts reads a `V.Provider` that the
  component rendering the list puts around the list over one of the same context above that component, and so
  does the child of a `V.Portal` such a row holds. They read the farther Provider's value. After a render of that
  component that an error boundary aborted, the row reads the value the component kept rather than the one the
  aborted render gave the Provider.

- A `V.Portal`'s child and a `V.VirtualList` row read the `V.Provider` values around them as the tree holds them
  when they render again on their own, as does a component below either. They read the values from when they
  mounted wherever a render since had changed one: a Provider above the Portal or the list, one inside the
  Portal's children, or one the component rendering the list puts around it. So did the rows a resumed
  time-sliced render reached below such a child, and an error boundary's fallback there.

- A component in a row of a keyed list whose time-sliced render parked reads the `V.Provider` value its own row
  last committed when it renders again on its own before the render reaches that row. It read the value the
  parked render gave the Provider of the first row holding a component at the same position.
