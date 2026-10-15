### Fixed

- A callback ref on an element that a `V.Portal` mounts while its `V.Suspense` shows the fallback no longer
  attaches until the boundary reveals the tree, as the refs of the hidden children outside a portal already did.
