### Changed

- `drop-shadow-*` is a CSS `drop-shadow()` filter, as Tailwind writes it. The shadow follows what the element
  draws, children and transparency included, rather than its box. Its third length is the standard deviation,
  as CSS reads it. The sizes are Tailwind v4's: `xs` to `2xl`, and the bare `drop-shadow`'s two shadows. It
  composes after the other built-in filter utilities and before `filter-[name]` customs, and it no longer shares
  a slot with `shadow-*`, so the two render together and neither one's `-none` removes the other. On a field
  that sends `shadow-*` to its input box, it lands on the control, as the other filter utilities do. It was the
  painted box shadow on its own scale.
- `drop-shadow` is a reserved name in `VelvetFilters.Register`.

### Added

- `drop-shadow-<color>`, `drop-shadow-[<color>]`, `drop-shadow-current` and a `/N` modifier on a size or a
  colour, which combine as Tailwind's do. `drop-shadow-[…]` takes a comma-separated list of shadows.

### Fixed

- Removing a named filter utility that shared a filter with a bracketed one, such as `blur-none` beside
  `blur-[2px]`, brings the bracketed value back instead of leaving no filter.
