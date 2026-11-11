### Fixed

- A component that the scheduled resume of a parked time-sliced render reaches reads the `V.Provider` values
  above the component whose render parked, as the components the render reached before parking do. A
  `Hooks.UseContext` call there returned the context's default value instead.

- A time-sliced render parked inside the children of a `V.Suspense` that has hidden them to show its fallback
  is not continued on its own while they are hidden; the next render that reaches it continues it under the
  `V.Provider` values above it. Continued on its own while they were hidden, it rendered components that read a
  context's default value in place of a `V.Provider` above them, and where the reveal did not render the parked
  component again, they kept that value.

- An error a component throws when the scheduled resume of a parked time-sliced render reaches it, or when an
  update of the parked component itself completes the parked render first, and that an error boundary above the
  parked component catches, is reported once, by that boundary. A `NullReferenceException` was logged beside
  it; after the scheduled resume, the boundary's own report also waited for a later commit.

- An error boundary's fallback reads the `V.Provider` values above the boundary when the error it catches is
  thrown by a `Hooks.UseEffect`, or by a render that did not start at the boundary or above it, such as an
  update of a component below the boundary, a time-sliced render resuming below it, or the first render of a
  `V.Portal`'s child. For an effect's error it read each context's default value. For a render's, it read the
  values where the error was thrown: a Provider below the boundary, one the boundary renders included, took
  precedence; under the scheduled resume a Provider above the boundary read as its context's default; and for a
  Portal's child, a Provider that another boundary's fallback had just placed above the boundary gave way to one
  of the same context above that other boundary.

- A component that a `V.Portal` mounts reads, in that first render, the `V.Provider` value around the Portal
  rather than one of the same context farther up, whether the Portal mounts with the component rendering it or
  when that component updates, where no other Portal and no `V.VirtualList` item encloses the Portal. It read the
  farther Provider's value on an update, and at the mount where a component above rendered it.
