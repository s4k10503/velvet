### Fixed

- A component in a row of a keyed list whose time-sliced render parked reads the `V.Provider` value its own row
  last committed when it renders again on its own before the render reaches that row. It read the value the
  parked render gave the Provider of the first row holding a component at the same position.
