### Changed

- An error boundary mounts its fallback afresh at each catch, as React's boundary reconciles the fallback against
  none of the children it replaces. A component of the fallback with the type and key of one the boundary showed
  starts from its initial state, an element of it with the key of one the boundary showed is mounted again, the
  shown one's ref let go, and a `V.Suspense` or `V.AnimatePresence` of the fallback starts with nothing the
  boundary's shown one recorded. That component's state used to be kept. Where the boundary caught outside a render
  expanding it — an error from a commit, or from the render of its own update or of a child's own update — that
  element used to be kept too, and that `V.Suspense` or `V.AnimatePresence` went on from the shown one; where it
  caught in such a render, a `V.Suspense` of the fallback whose children suspended could keep them, hidden, though
  it had never shown them.
- A boundary that catches what its own fallback swap throws, from a cleanup of what the swap unmounts, mounts the
  fallback for that error afresh too, where it used to update the fallback it had just mounted.
