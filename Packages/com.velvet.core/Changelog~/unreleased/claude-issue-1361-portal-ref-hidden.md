### Fixed

- A `V.Portal` among the children a `V.Suspense` hides is now hidden with them, its components along with its
  elements. The refs, layout effects and state updates of the components inside it wait until the boundary
  reveals the tree, for a portal mounted while the fallback shows as for one that was already mounted, where
  they used to run while the tree was hidden.
