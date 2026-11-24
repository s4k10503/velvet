### Fixed

- A state update made by a `UseLayoutEffect`, the cleanup an update runs before it, or a `refCallback:`, as one root
  committed an update, on a component of another root that had no update waiting, rendered at that other root's next
  update drain, so a frame could be painted in between. It now renders before control returns to whatever started that
  commit, as React gives an update made inside a commit outside a transition the sync lane whichever root it targets.
  An update a `UseEffect` makes on another root renders where it did before.
