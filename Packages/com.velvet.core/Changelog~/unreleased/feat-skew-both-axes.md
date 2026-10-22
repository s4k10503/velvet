### Added

- `skew-<n>`, `-skew-<n>` and `skew-[…]` skew both axes by one angle, as Tailwind writes them, and compose with
  `skew-x-*` and `skew-y-*` as Tailwind's `skewX() skewY()`, a `shadow-*` on the element included. Each axis takes
  the class Tailwind emits last: a `skew-x-*` or `skew-y-*` class takes its axis over `skew-<n>` whichever is
  written first and whatever their signs (`skew-x-12 skew-6` and `-skew-x-12 skew-6` keep their x angle), a class
  behind a variant takes it over the base ones (`skew-x-12 hover:skew-6` gives 6° on both axes while hovered), and
  an important class takes it over every plain one. A bare value is a whole number of degrees without a
  leading zero (`skew-1.5` and `skew-07` are declined), and a bracket an angle or `0`.
