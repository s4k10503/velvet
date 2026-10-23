### Changed

- A margin class a play shows on a child of a `gap-*` row, such as `EnterFromClass = "ml-[3px]"`, adds to
  the gap on that edge while it shows, as CSS adds a gap to an item's margin: under `gap-[10px]` the child
  starts the enter 13px from its neighbour and ends it 10px away. The gap used to replace it, so the
  child sat 10px away throughout.

- A wrapping `gap-*` container that a `clip-path-*` class clips adds the negated half-margins to its own
  margin and grows its declared size by the gap, as an unclipped one does. The half-margins used to take its
  own margin's place there, and its declared size stayed as written.
