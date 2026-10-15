### Fixed

- A component that renders inside the children of a `V.Suspense` showing its fallback now runs the
  `Hooks.UseInsertionEffect`s whose deps changed in that commit, as React's mutation phase does for a hidden
  tree. They waited for the reveal before, along with the tree's layout effects, which still wait.
