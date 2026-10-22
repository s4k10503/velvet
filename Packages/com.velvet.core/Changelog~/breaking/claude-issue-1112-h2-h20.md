### Changed

- Calling the `startTransition` function `Hooks.UseTransition` returns renders the declaring component with
  `isPending` true at once, on the Urgent lane and ahead of the transition's own render, as React does through
  the sync-lane optimistic update it dispatches before running the callback. The writes the callback queued
  stay on the Transition lane, so that render shows the state from before them, and the render that commits
  them follows. Before, nothing rendered purely because `isPending` turned true: a component showing a pending
  branch saw it only in a render something else produced, and otherwise in the render that committed the
  transition, so the indicator now appears earlier and a transition that leaves the flag lit costs the
  component one more render. A synchronous callback that queues nothing still settles as it returns and renders
  nothing.
- The passive effects (`Hooks.UseEffect`) of a component that a time-sliced transition pass has rendered wait for
  the commit that completes the pass. They ran between slices, before the transition committed, where React
  runs no passive effect for a render that has not committed.
