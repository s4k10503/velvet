### Changed

- Transition-lane work — an update a `startTransition` callback schedules, and a `UseDeferredValue`
  derivation — no longer waits 100 ms after it was scheduled. It renders in a later panel scheduler pass
  than the urgent render that asked for it: the next pass when the request is made inside one of Velvet's
  frame-boundary drains, as a `UseDeferredValue` reached by an urgent render is, and the pass after that
  when it is made anywhere else, as a `startTransition` in a click handler is. On an idle tree a transition
  started from a click therefore commits on the second scheduler pass after the click, so `isPending` reads
  true until then rather than for at least 100 ms. Normal- and Urgent-lane updates still queued when a transition drains commit first, in
  the same pass, and an urgent update to a component whose deferred value was queued for that pass moves the
  deferred value to the next one. A store mutation made between the two tiers' drains, inside a transition
  or outside one, now reaches both its readers by the end of the transition's drain; a mutation made inside a
  transition previously left the transition's readers on the snapshot the earlier drain had pinned until the
  store changed again. [`Documentation~/react-migration.md`](Documentation~/react-migration.md) says when
  Transition-lane work renders.

- A `UseDeferredValue` render on the Transition lane commits the value it is handed rather than only the value the urgent render queued. A value built afresh on every render — a new list or record
  each time — never matched the queued one, so it never committed and re-requested a render every time the
  Transition tier drained; it now commits on that drain. A value that changes again between the urgent
  render and the transition render commits the newer one directly instead of deferring once more.

### Fixed

- A `UseStore` reader rendered in the same drain pass as a store mutation it was already queued for no longer
  stays on the snapshot that pass pinned. Its render read the pinned value, consistent with the readers
  rendered before the mutation, and nothing asked for another render once the mutation had notified; it now
  renders again in a later pass and shows the store's current value.
