### Fixed

- The insertion effects a render queued inside the children of a `V.Suspense` showing its fallback now run once
  when the boundary reveals them. A second render made while hidden, or a tree that never showed and was rendered
  again with unchanged deps, dropped them before. An effect whose deps changed and changed back while hidden
  still runs at the reveal, since Velvet renders the hidden children where React does not.
