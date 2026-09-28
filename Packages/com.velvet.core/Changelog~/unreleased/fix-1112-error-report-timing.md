### Fixed

- `MountOptions.OnCaughtError` is called in the commit that shows the catching boundary's fallback, after
  the fallback's layout effects and before those of the boundary's ancestors, as React calls
  `onCaughtError`. It was called as soon as the fallback was reconciled, before any of its layout effects. A
  boundary that an ancestor boundary replaces before that commit no longer reports the error it caught,
  matching React.
- A fallback an error boundary shows after catching an error thrown from a `UseEffect` or a `UseFrame`
  callback runs its layout effects in that same commit. They waited for the next commit of some other
  component.
- A component that suspended with no `V.Suspense` above it commits the render its resolved resource asks
  for: that render's layout effects run, and so do those of the components it mounts. Only its imperative
  handles ran, and none of those layout effects ran in that commit.
- Components mounted while a commit runs its layout effects — the fallback an error boundary shows for a
  layout effect's error, for one — commit their own layout effects as a commit of their own, after every
  commit of the same flush. They ran inside the commit that mounted them, ahead of the flush's other commits.
