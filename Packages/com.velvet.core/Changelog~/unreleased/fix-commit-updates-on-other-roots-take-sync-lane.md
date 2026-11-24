### Fixed

- A state update made by a `UseLayoutEffect`, the cleanup an update runs before it, or a `refCallback:` as it attaches,
  as a root committed an update, on a component of another root rendered at that root's next update drain, so a frame
  could be painted in between. It now renders before control returns to whatever started that commit, as React gives an
  update made inside a commit outside a transition the sync lane whichever root it targets. An update a `UseEffect`
  makes on another root takes no sync lane: it renders at that root's next update drain, or with sync work that root
  renders before then.
- Such an update made on a component that already had an update waiting, on its own root or another, waited with that
  update for the root's next update drain, unless that drain was the one committing. It now renders before control
  returns as well, with the update that was waiting, as React renders a root's pending sync and default lanes together,
  and that root's pending `UseEffect`s then run before control returns, as React flushes those of a sync-lane commit as
  it ends. Where the root's own update drain was the one committing, that drain now runs the root's pending
  `UseEffect`s before it returns.
- Two roots whose layout effects or `refCallback:`s write each other a new value on every commit kept doing so, one
  update drain of a root at a time, with no end. With the updates above rendering before control returns, the
  exchange stops where one root is asked to render a 51st time by the other's commits before control returns: the
  updates queued for that root are dropped and "Maximum update depth exceeded" is logged.
