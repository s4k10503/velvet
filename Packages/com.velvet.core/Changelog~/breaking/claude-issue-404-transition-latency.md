### Changed

- Transition-lane work — an update a `startTransition` callback schedules, and a `UseDeferredValue`
  derivation — renders at the next frame boundary instead of 100 ms after it was scheduled. On an idle tree
  a transition now commits on the next frame, so `isPending` reads true until that commit rather than for
  at least 100 ms, and a `UseDeferredValue` value catches up with its input at the frame boundary after the
  render that observed the change; a transition a Transition-lane render requests waits for the frame after
  it. Normal- and Urgent-lane updates still queued when the transition's drain runs commit first, in the
  same frame: a store mutation made outside a transition between the two drains now reaches both its readers
  in that frame, where the transition's reader previously rendered first at the snapshot the earlier drain
  had pinned. [`Documentation~/react-migration.md`](Documentation~/react-migration.md) says when
  Transition-lane work renders.
