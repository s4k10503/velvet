### Changed

- A chain of layout commits that never settles — an error boundary whose fallback's layout effect throws back
  into it on every commit — stops after 50 follow-up commits, React's nested-update limit, where it stopped after
  100. The `InvalidOperationException` it throws reads "Maximum update depth exceeded", as React's error does.
- The passive effects (`Hooks.UseEffect`) of one commit, cleanups and then setups, run in tree order, children
  before their parent, as React's commit walks the tree. Components whose updates a drain flushed one by one ran
  in the order the drain flushed them, and a component its own update rendered ran after a later sibling its
  parent's pass rendered.
- Updates that keep coming from the commit phase — a callback ref or a layout effect writing a new value on every
  commit, or a `UseSyncExternalStore` whose `getSnapshot` builds a new value on every read — stop after 50 nested
  passes by throwing an `InvalidOperationException` reading "Maximum update depth exceeded" out of the drain, as
  React throws its error. The runaway update is still dropped. The drain used to log that error and return.
- After a keyed reorder moves elements holding components, a commit takes those components in the new tree order,
  as React's child list follows the move: their layout effects, refs and passive effects run in that order. They
  ran in the order the components were first created.
