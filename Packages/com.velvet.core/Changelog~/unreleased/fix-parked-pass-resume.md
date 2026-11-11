### Fixed

- A component that a time-sliced render reaches after it first parks reads the `V.Provider` values above the
  component whose render parked, as the components it reached before parking do. A `Hooks.UseContext` call
  there returned the context's default value instead.

- An error a component throws when a time-sliced render reaches it after parking, and that an error boundary
  above the parked component catches, is reported once, by that boundary. A `NullReferenceException` was
  logged beside it, whether the scheduled resume reached the throwing component or a render of the parked
  component completed the parked render first; after the scheduled resume, the boundary's own report also
  waited for a later commit.
