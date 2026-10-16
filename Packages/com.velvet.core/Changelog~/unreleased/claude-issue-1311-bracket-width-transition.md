### Fixed

- Swapping one bracketed width (`w-[300px]`) for another (`w-[10px]`, `!w-[10px]`) on an element with a
  `transition-*` utility now runs the transition, instead of the new width landing at once.
