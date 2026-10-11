### Added

- Bracketed lengths read `calc()`, `min()`, `max()` and `clamp()`. A function that comes to one pixel length
  or one percentage (`top-[calc(1rem+4px)]`, `gap-[min(2rem,24px)]`, `w-[calc(100%/3)]`) is read wherever
  that plain length or percentage is, except that a border width takes no percentage (below). These classes
  were inert before. See
  `Documentation~/styling-arbitrary-lengths.md`.

### Fixed

- A percentage border width (`border-[50%]`, `border-t-[25%]`) is declined, as CSS declines it, rather than
  written as that many pixels.
