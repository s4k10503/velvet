### Fixed

- A state update made by a `refCallback:` cleanup, run as its element was removed, as its ref was set to `null` on an
  element that stays, or as a `V.Suspense` hid the children it had shown, or by a `UseLayoutEffect` cleanup run as such
  a `V.Suspense` hid it or as its `V.VirtualList` row left the range, rendered at its root's next update drain where
  that root was not the one committing, or where a `V.VirtualList` range update ran the cleanup, so a frame could be
  painted in between. It now renders before control returns to whatever ran the commit, and the `UseEffect`s of the
  commit that renders it run before then as well, as React runs these cleanups in its mutation phase, which gives an
  update made there outside a transition the sync lane whichever root it targets, and flushes a sync-lane commit's
  passive effects as that commit ends.
- A state update that a `UseLayoutEffect`, a `refCallback:` or the cleanup of either made, in a commit a
  `startTransition` callback ran as it mounted or unmounted a root, took the Transition lane. It now takes the sync lane,
  as React clears the transition for a commit.
