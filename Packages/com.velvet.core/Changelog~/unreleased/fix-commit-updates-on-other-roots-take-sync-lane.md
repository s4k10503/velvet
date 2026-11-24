### Fixed

- A state update made by a `UseLayoutEffect`, the cleanup an update runs before it, or a `refCallback:`, as a root
  committed an update, on a component of another root rendered at that root's next update drain, so a frame could be
  painted in between. It now renders before control returns to whatever started that commit, as React gives an update
  made inside a commit outside a transition the sync lane whichever root it targets. An update a `UseEffect` makes on
  another root renders where it did before.
- Such an update made on a component that already had an update waiting, on its own root or another, waited with that
  update for the root's next update drain, unless that drain was the one committing. It now renders before control
  returns as well, with the update that was waiting, as React renders a root's pending sync and default lanes together;
  and that root's pending `UseEffect`s run before control returns, as React flushes those of a sync-lane commit as it
  ends.
