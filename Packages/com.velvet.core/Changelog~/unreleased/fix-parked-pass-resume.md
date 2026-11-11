### Fixed

- A component that the scheduled resume of a parked time-sliced render reaches reads the `V.Provider` values
  above the component whose render parked, as the components the render reached before parking do. A
  `Hooks.UseContext` call there returned the context's default value instead.

- A time-sliced render parked inside the children of a `V.Suspense` that has hidden them to show its fallback
  waits for the Suspense to reveal them, and the render that reveals them completes it. Resumed while they were
  hidden, it rendered components that read a context's default value in place of a `V.Provider` above them, and
  where the parked component is `[Component(Memoize = true)]`, they still showed that value after the reveal.

- An error a component throws when the scheduled resume of a parked time-sliced render reaches it, or when an
  update of the parked component itself completes the parked render first, and that an error boundary above the
  parked component catches, is reported once, by that boundary. A `NullReferenceException` was logged beside
  it; after the scheduled resume, the boundary's own report also waited for a later commit.

- An error boundary's fallback reads the `V.Provider` values above the boundary when the error is thrown by a
  render that did not start at the boundary or above it: an update of a component below the boundary, or a
  time-sliced render resuming below it. It read the values where the error was thrown instead, so a Provider
  below the boundary, one the boundary renders included, took precedence, and under the scheduled resume a
  Provider above the boundary read as its context's default.
