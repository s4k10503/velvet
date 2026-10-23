### Changed

- A chain of layout commits that never settles — an error boundary whose fallback's layout effect throws back
  into it on every commit — stops after 50 follow-up commits, React's nested-update limit, where it stopped after
  100. The `InvalidOperationException` it throws reads "Maximum update depth exceeded", as React's error does.
- The passive effects (`Hooks.UseEffect`) one flush runs, cleanups and then setups, run in tree order, children
  before their parent, as React's commit walks the tree. Components whose updates a drain flushed one by one ran
  in the order the drain flushed them, and a component its own update rendered ran after a later sibling its
  parent's pass rendered.
