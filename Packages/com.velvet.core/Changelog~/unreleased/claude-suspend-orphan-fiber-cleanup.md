### Fixed

- A component that suspends with no `V.Suspense` above it, inside a host element a retry creates again (a
  `V.Div` child of a `V.Portal`, for one), no longer leaves the fiber of each abandoned attempt registered. The
  components mounted beside the reader are released when the attempt is given up, and the reader and the
  components above it under that element are released once its resource resolves and asks for the retry.
A suspend a `V.Suspense` above the host element catches is unchanged.
