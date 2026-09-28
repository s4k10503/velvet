### Fixed

- `MountOptions.OnCaughtError` is called in the commit that shows the catching boundary's fallback, after
  the fallback's layout effects and before those of the boundary's ancestors, as React calls
  `onCaughtError`. It was called as soon as the fallback was reconciled, before any of its layout effects. A
  boundary that an ancestor boundary replaces before that commit no longer reports the error it caught,
  matching React. A boundary that a time-sliced transition mounted or re-rendered, catching before the
  transition completes, reports in the commit that completes it, after its own layout effects.
- A fallback an error boundary shows after catching an error thrown from a `UseEffect`, a `UseFrame`
  callback, or a `V.VirtualList` row that a scroll or a resize renders runs its layout effects in that same
  commit. They waited for the next commit of some other component.
- When a scroll or a resize renders new `V.VirtualList` rows, the components inside an element a row
  returns run their layout effects before that range update returns. They waited for the next commit of
  some other component.
- A component that suspended with no `V.Suspense` above it commits the render its resolved resource asks
  for: that render's layout effects run, and so do those of the components it mounts. Only its imperative
  handles ran, and none of those layout effects ran in that commit.
- Components mounted while a commit runs its layout effects — the fallback an error boundary shows for a
  layout effect's error, for one — commit their own layout effects as a commit of their own, after every
  commit of the same flush. They ran inside the commit that mounted them, ahead of the flush's other commits.
  A chain of such commits that does not settle — a fallback whose layout effect throws into its own boundary
  on every mount, for one — stops after 100 of them with an `InvalidOperationException`, and the next update
  does not run it again.
- A batch drain that re-renders several components commits their layout effects as one commit, matching
  React's all-cleanups-before-all-setups across it: the layout-effect cleanups of every component the drain
  re-rendered run before any of their setups, each pass child before parent and sibling components in tree
  order, whatever order their updates were made in. The first component's commit ran
  the layout effects of every inline child the drain had re-rendered, whichever component it belonged to, and
  each component then ran its own cleanup and setup before the next one's cleanup.
- A component mounted into a wrapper element, as a `V.VirtualList` item is, commits the layout effects of its
  own subtree only. Its mount also ran those of the components the enclosing reconcile had mounted and not yet
  committed, before that reconcile had attached their refs.
- After an error boundary caught an error thrown outside a reconcile — from a layout effect, a `UseEffect`, a
  `UseFrame` callback or a component's own re-render — the next reconcile to start in the tree was discarded,
  so that component's update did not show until it rendered again, and a second boundary catching before then
  kept the content that had thrown in place of its fallback. Both now commit.
