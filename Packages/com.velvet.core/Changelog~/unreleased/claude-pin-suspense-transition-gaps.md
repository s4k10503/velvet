### Fixed

- A component that suspends while mounting inside a host element, below a `V.Suspense`, no longer leaves the fibers of
  that abandoned attempt registered after the boundary reveals: the components mounted beside the reader are released
  when the attempt is abandoned, and the reader with the components above it when its resource resolves.
