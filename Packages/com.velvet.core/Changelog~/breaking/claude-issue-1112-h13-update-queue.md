### Changed

- `Hooks.UseState` and `Hooks.UseReducer` queue their updates per hook, each with the lane it was written on, and
  a render folds in only the updates of its own lane and the lanes above it, as React's update queues do. A
  write made inside `startTransition` is no longer on screen before the transition commits: an urgent or Normal
  render of the component in between, and a pass of an ancestor that re-renders it, shows the state from before
  the write, and the Transition-lane render that follows applies it. A component an ancestor's non-transition
  pass re-renders keeps its queued transition writes and its `isPending` lit until that Transition-lane render
  commits them, where the ancestor's pass used to commit them and settle the transition. Code that read a
  transition's write from a render outside the transition, or that took such a pass for the transition's commit,
  now sees the earlier state until the Transition lane drains.
- A functional updater (`setValue.Invoke(prev => next)`) written while another update of the same hook is
  queued runs in the render that applies it, over the state the updates before it produce, where it ran at the
  call. One written while nothing is queued still runs at the call. A skipped update that a later render
  replays runs its updater or reducer again.
- A `Hooks.UseReducer` dispatch computed at the call is used by the render only when the render's reducer is the
  same delegate, that is the same method and target. A reducer rebuilt by each render, such as a lambda
  closing over a value the component reads, is applied to the dispatch again by the render that folds it, so
  the render's own reducer decides the state, as React's does.
