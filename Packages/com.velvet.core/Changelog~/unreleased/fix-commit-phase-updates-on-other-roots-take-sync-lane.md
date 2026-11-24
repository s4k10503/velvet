### Fixed

- A state update made on a component of another root by a `refCallback:` cleanup, run as its element was removed, as
  its ref was set to `null` on an element that stays, or as a `V.Suspense` hid the children it had shown, or by a
  `UseLayoutEffect` cleanup run as such a `V.Suspense` hid it or as its `V.VirtualList` row left the range, rendered at
  that root's next update drain, so a frame could be painted in between. It now renders before control returns to
  whatever ran that commit, as React runs these in its mutation phase, which gives an update made there outside a
  transition the sync lane whichever root it targets.
- A state update that a `UseLayoutEffect`, a `refCallback:` or the cleanup of either made, in a commit a
  `startTransition` callback ran as it mounted or unmounted a root, took the Transition lane. It now takes the sync lane,
  as React clears the transition for a commit.
