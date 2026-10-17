### Changed

- `Hooks.UseState` and `Hooks.UseReducer` queue their updates per hook, each with the lane it was written on, and
  a render folds in only the updates its lane renders, as React's update queues do. Urgent and Normal render
  together, so only a Transition update waits for a Transition-lane render. A write made inside `startTransition`
  is no longer on screen before the transition commits: an Urgent or Normal render of the component in between,
  and a pass of an ancestor that re-renders it, shows the state from before the write, and the Transition-lane
  render that follows applies it. A component an ancestor's non-transition pass re-renders keeps its queued
  transition writes and its `isPending` lit until that Transition-lane render commits them, where the ancestor's
  pass used to commit them and settle the transition. Code that read a transition's write from a render outside
  the transition, or that took such a pass for the transition's commit, now sees the earlier state until the
  Transition lane drains.
- A render outside any flush, such as the retry of a resolved `Hooks.Use` resource, no longer folds in a
  transition's queued writes.
- A functional updater (`setValue.Invoke(prev => next)`) written while another update of the same hook is
  scheduled runs in the render that applies it, over the state the updates before it produce, where it ran at
  the call. One written while none is scheduled still runs at the call, and a render that folds it replays the
  result rather than running it again. A replayed write that ran at the render runs again.
- A `Hooks.UseReducer` dispatch no longer runs the reducer at the call, and renders the component even when the
  result equals the state, as React's `dispatch` does. The reducer of the render that folds the dispatch runs it,
  so a reducer rebuilt by each render, such as a lambda closing over a value the component reads, decides the
  state with the values that render holds.
