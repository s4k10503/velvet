### Changed

- While a `V.Motion` animates a length or a color, the animated value stands over the element's own
  variant layers (`dark:`, `sm:`, `hover:` and the rest) and over a `space-*`, `divide-*` or grid
  value, as an animation does in CSS, and a gap adds to an animated margin as CSS adds an item's margin to
  its gap. An important divide value (`!divide-gray-200`) and an important declaration of the element's
  own (`!w-[80px]`, or `hover:!w-[80px]` turning on during the animation) keep what they win, as
  `!important` outranks an animation. A plain variant layer toggling during the animation used to replace the animated value
  until the next frame, or for the rest of a delay, and a gap gave way to an animated margin.
