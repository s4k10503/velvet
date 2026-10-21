### Fixed

- Two sibling components that share a key both render, each with its own state, under a warning. A later
  render in which the repeats keep their relative order keeps each sibling's elements. The repeat was skipped.
- `V.AnimatePresence` renders every child that shares a key, under a warning, and a later render in which
  the repeats keep their relative order keeps them all. When fewer children with that key remain, the surplus
  repeats leave at once without playing an exit. The later child replaced the earlier one.
