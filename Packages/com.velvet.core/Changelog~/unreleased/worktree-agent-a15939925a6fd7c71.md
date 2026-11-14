### Changed

- A component's re-render and commit allocate less. The layout commit and the passive-effect flush order the
  components they run in lists kept on the root rather than built for each commit, and order nothing where one
  component is staged. The batch scheduler and the passive-effect flush each register a callback the root builds once,
  rather than a new one per registration.
- Scheduling a component's flush or its passive effects, and retiring a render's previous output, no longer build the
  closure of a branch the call does not take.
- A `UseContext` read no longer allocates an object to record the context it read.
- A component re-rendering on its own under a Provider reuses, across renders, the lists, the key counts and the
  per-context stacks it rebuilds that Provider's value with.
