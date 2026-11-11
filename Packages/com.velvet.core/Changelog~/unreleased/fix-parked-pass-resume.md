### Fixed

- A component that the scheduled resume of a parked time-sliced render reaches reads the `V.Provider` values
  above the component whose render parked, as the components the render reached before parking do. A
  `Hooks.UseContext` call there returned the context's default value instead.

- A time-sliced render parked inside the children of a `V.Suspense` that has hidden them to show its fallback
  waits for the Suspense to reveal them before it continues. Continued while they were hidden, it rendered
  components that read a context's default value in place of a `V.Provider` above them, and where the reveal did
  not render the parked component again, they kept that value.

- An error a component throws when the scheduled resume of a parked time-sliced render reaches it, or when an
  update of the parked component itself completes the parked render first, and that an error boundary above the
  parked component catches, is reported once, by that boundary. A `NullReferenceException` was logged beside
  it; after the scheduled resume, the boundary's own report also waited for a later commit.

- An error boundary's fallback reads the `V.Provider` values above the boundary when the error it catches is
  thrown by a `Hooks.UseEffect`, or by a render that did not start at the boundary or above it, such as an
  update of a component below the boundary or a time-sliced render resuming below it. For an effect's error it
  read each context's default value. For a render's, it read the values where the error was thrown: a Provider
  below the boundary, one the boundary renders included, took precedence, and under the scheduled resume a
  Provider above the boundary read as its context's default.
