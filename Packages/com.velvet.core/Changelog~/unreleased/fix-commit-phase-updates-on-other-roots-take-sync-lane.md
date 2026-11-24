### Fixed

- A state update made on a component of another root by a `refCallback:` cleanup, run as its element was removed, as
  its ref was set to `null` on an element that stays, or as a `V.Suspense` hid the children it had shown, or by a
  `UseLayoutEffect` cleanup run as such a `V.Suspense` hid it or as its `V.VirtualList` row left the range, rendered at
  that root's next update drain, so a frame could be painted in between. It now renders before control returns to
  whatever ran the commit, as React runs these cleanups in its mutation phase, which gives an update made there outside
  a transition the sync lane whichever root it targets. So does an update such a `V.VirtualList` row's
  `UseLayoutEffect` cleanup makes on the list's own root, which also waited for that root's next update drain.
- A state update that a `UseLayoutEffect` made, in a commit a `startTransition` callback ran as it mounted a root, took
  the Transition lane. It now takes the sync lane, as React clears the transition for a commit.
- A state update that a `UseEffect` made, in the passive effect flush a `V.Mount` called inside a `startTransition`
  callback starts with, took the Transition lane. It now takes the lane it takes outside a transition, as React clears
  the transition for its passive flush.
