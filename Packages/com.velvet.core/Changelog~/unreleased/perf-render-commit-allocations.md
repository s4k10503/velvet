### Changed

- A component's re-render and commit allocate less. The layout commit and the passive-effect flush order the
  components they run in lists kept on the root rather than built for each commit, and order nothing where one
  component is staged. The drain of Normal and Urgent updates and the passive-effect flush each register a callback the
  root builds once, rather than a new one per registration.
- Scheduling a component's flush or its passive effects, retiring a render's previous output and resuming a
  time-sliced render no longer build the closure of a branch the call does not take.
- A `UseContext` read no longer allocates an object to record the context it read.
- A component re-rendering on its own under a Provider reuses, across renders, the list of its ancestors, the list of
  the Providers it pushes again, the table counting repeated component keys and the context cursor's per-context
  stacks, rather than allocating them for each render.
