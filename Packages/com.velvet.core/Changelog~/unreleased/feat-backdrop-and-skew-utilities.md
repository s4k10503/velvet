### Added

- `skew-<n>`, `-skew-<n>` and `skew-[…]` skew both axes by one angle, as Tailwind writes them. A `skew-x-*` or
  `skew-y-*` class outranks them on its own axis wherever it is written, so `skew-x-12 skew-6` gives 12° on x and
  6° on y. A bare value is a whole number of degrees (`skew-1.5` and `skew-[8]` are declined), and a value the
  browser rejects in any skew class drops the others under the same variants with it.

### Fixed

- An important skew (`!skew-x-6`, `skew-x-6!`, `!skew-6`) applies and outranks every plain skew on its axes; it was
  ignored.
