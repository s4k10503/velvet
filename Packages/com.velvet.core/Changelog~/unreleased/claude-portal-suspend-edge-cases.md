### Fixed

- A time-sliced update that mounts a `V.Portal` whose children suspend with no `V.Suspense` above it, in a
  render that parked before the Portal mounted, no longer places its rows twice. The retry used to diff from the
  older tree while the rows the parked pass had placed stood on the target, and created them again.
