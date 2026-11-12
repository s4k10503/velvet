### Changed

- An error boundary catching an error from a commit — a layout or passive effect, its cleanup, a ref callback — or
  from the render of its own update or of a child's own update mounts its fallback afresh, as React's boundary
  reconciles the fallback against none of the children it replaces. A component of the fallback with the type and
  key of one the boundary showed starts from its initial state, an element of it with the key of one the boundary
  showed is mounted again, the shown one's ref let go, and a `V.Suspense` or `V.AnimatePresence` of the fallback
  starts with nothing the boundary's shown one recorded. That component's state and that element used to be kept,
  and that `V.Suspense` or `V.AnimatePresence` went on from the shown one.
