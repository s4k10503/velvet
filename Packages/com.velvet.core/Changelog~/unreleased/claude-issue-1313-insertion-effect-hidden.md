### Fixed

- A component inside the children of a `V.Suspense` showing its fallback now runs the `Hooks.UseInsertionEffect`s
  whose deps changed while it was hidden when the boundary reveals it. A second render made while hidden, or a
  tree that never showed and was rendered again with unchanged deps, dropped them before.
