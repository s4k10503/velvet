### Fixed

- Two sibling components that share a key both render, each with its own state, under a warning. A later
  render that repeats the key in the same order keeps each sibling's elements. The repeat was skipped.
- `V.AnimatePresence` renders every child that shares a key, under a warning, and a later render in the
  same order keeps them all. When a single child with that key remains, the later occurrence is the one
  that exits. The later child replaced the earlier one.
