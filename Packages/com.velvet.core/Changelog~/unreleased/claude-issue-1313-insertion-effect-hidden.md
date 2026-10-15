### Fixed

- Where a `V.Suspense` had shown its children and is showing its fallback, a component that renders or mounts
  among them now runs the `Hooks.UseInsertionEffect`s whose deps changed in that commit, as React does for a
  hidden tree. They waited for the reveal before, along with the tree's layout effects, which still wait. A tree
  that never showed runs its insertion effects at the reveal, one whose deps did not change in between included.
