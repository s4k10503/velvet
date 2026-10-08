### Fixed

- `FiberPortalRegistry.Register` no longer warns that an id is "already registered" when it is given the element
  that id already points at. A different element still warns and overwrites.
