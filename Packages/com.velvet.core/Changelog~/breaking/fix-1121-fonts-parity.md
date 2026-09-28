### Changed

- A registered font family picks its weight in CSS's font matching order rather than by the nearest
  weight. A request from 400 to 500 now takes the lightest weight from the request up to 500, then the
  heaviest below the request, then the lightest above 500; a request under 400 the heaviest at or below
  it first, and one over 500 the lightest at or above it first. So `font-medium` over a family
  registering 400 and 600 now renders the 400 face, where it used to render the 600 one. The face's
  style also narrows before its weight, as in CSS: `font-bold italic` over a family whose only italic
  face is the regular one renders that italic with synthesized bold, where it used to render the bold
  upright with synthesized italic. `VelvetFontFamily.FindClosestWeight` returns the entry this order
  selects.

- `text-balance` alone makes a label's text wrap, as CSS's `text-wrap: balance` sets the wrap mode to
  `wrap`. It used to set no white-space, so on a label that did not already wrap it had no effect
  without a wrapping `whitespace-*` or `text-wrap` beside it. A `whitespace-*` class on the same element
  still decides the white-space.

- `leading-[…]` takes CSS `line-height`'s values: a unitless number, an `em` length and a percentage
  are relative to the font size (`leading-[1.5]`, `leading-[1.5em]` and `leading-[150%]` each set 1.5
  times it), and `rem` is 16px. Only `px` used to be read, and every other value was ignored.

### Added

- `text-pretty`, which makes the text wrap the way `text-balance` does and keeps the engine's own line
  breaks.
