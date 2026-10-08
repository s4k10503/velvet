### Changed

- Transition-lane work — an update a `startTransition` callback schedules, and a `UseDeferredValue`
  derivation — no longer waits 100 ms after it was scheduled. It renders in a later panel scheduler pass than
  the urgent render that asked for it; Velvet holds it to a later pass, not to a later frame. A request made
  inside Velvet's render drains, its scheduled `UseEffect` drain, a time-sliced render's resume, the notification
  of a resource that resolved while its reader was rendering, or a `UseFrame` callback, run by the panel the requesting tree is mounted on, renders on the
  next pass. A request made anywhere else — a discrete event handler, including the urgent render its
  synchronous flush commits, another panel's callbacks, Velvet's other scheduled work, or code outside every
  panel — renders on the pass after that, so a search box's `UseDeferredValue` list catches up two passes after the keystroke, and
  `isPending` reads true until then rather than for at least 100 ms. Normal- and Urgent-lane updates still
  queued when a transition drains commit first, in the same pass, and an urgent update to a component whose
  deferred value was queued for that pass moves the deferred value to a later one, up to
  `TransitionStarvationThreshold` (30) moves. A store mutation made inside a transition between the two tiers'
  drains now reaches both its readers by the end of the transition's drain; it previously left the
  transition's readers on the snapshot the earlier drain had pinned until the store changed again.
  [`Documentation~/react-migration.md`](Documentation~/react-migration.md) says when Transition-lane work
  renders.

- A `UseDeferredValue` render on the Transition lane commits the value it is handed rather than only the
  value the urgent render queued. A value built afresh on every render — a new list or record each time —
  never matched the queued one, so it never committed and re-requested a render every time the Transition
  tier drained; it now commits on that drain. A value that changes again between the urgent render and the
  transition render commits the newer one directly instead of deferring once more.

### Fixed

- A `UseDeferredValue` in a component that also had an update of its own queued — a store its parent reads
  too, or a state setter called alongside the parent's — committed its new value in the same pass as the
  parent's urgent render that passed the changed input down, so the deferral did nothing. That value now
  commits on the Transition tier, behind the urgent render, as one without an update of its own already did;
  the urgent render shows the previous value. A discrete event handler's synchronous flush no longer commits
  it either.
