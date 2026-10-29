### Fixed

- A component that suspends while mounting inside a host element, below a `V.Suspense`, no longer leaves the fibers of
  that abandoned attempt registered after the boundary reveals; they are released when the reader's resource resolves.
