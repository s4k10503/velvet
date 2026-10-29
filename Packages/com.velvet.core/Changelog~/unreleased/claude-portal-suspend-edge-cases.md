### Fixed

- A time-sliced update that mounts a `V.Portal` whose children suspend with no `V.Suspense` above it, in a
  render that parked before the Portal mounted, no longer places its rows twice. The retry used to diff from the
  older tree while the rows the parked pass had placed stood on the target, and created them again.
- A time-sliced update that suspends with no `V.Suspense` above it no longer places more rows, or runs the commit
  of its final slice, before the retry: the retry resumes it. A slice that parks again and then suspends keeps the
  baseline the parked rows diff against instead of returning it to the pool.
