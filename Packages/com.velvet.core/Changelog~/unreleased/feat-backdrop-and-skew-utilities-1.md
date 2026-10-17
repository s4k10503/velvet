### Added

- `skew-<n>`, `-skew-<n>` and `skew-[…]` skew both axes by one angle, as Tailwind writes them. Each axis takes the
  class Tailwind emits last: a `skew-x-*` or `skew-y-*` class takes its axis over `skew-<n>` whichever is written
  first (`skew-x-12 skew-6` gives 12° on x and 6° on y), a class behind a variant takes it over the base ones
  (`skew-x-12 hover:skew-6` gives 6° on both axes while hovered), and an important class takes it over every plain
  one. A bare value is a whole number of degrees (`skew-1.5` and `skew-[8]` are declined).

### Fixed

- An important skew (`!skew-x-6`, `skew-x-6!`, `!skew-6`, `hover:!skew-6`) applies and outranks every plain skew on
  its axes; it was ignored.
