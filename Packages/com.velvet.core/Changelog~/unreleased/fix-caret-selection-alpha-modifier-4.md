### Added

- The translate utilities that write both axes: `translate-<n>`, `-translate-<n>`, `translate-px`, `translate-full`,
  the fractions (`translate-1/2`) and `translate-[…]`. Each axis holds against `translate-x-*` / `translate-y-*`
  by importance, variant and Tailwind's order, an axis utility of the same rank holding over it.
  A `V.Motion` Spring or Bezier play drives both translate channels from the pixel forms, as it drives
  `translate-x-*` and `translate-y-*`; the percent forms land with the swap.

### Fixed

- A uniform `scale-<n>` keeps the axis a `scale-x-*` or `scale-y-*` variant above it leaves, where a class
  variant on the same element used to drop it to 1.
