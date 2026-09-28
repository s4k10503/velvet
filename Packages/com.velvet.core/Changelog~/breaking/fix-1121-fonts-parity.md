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

- `text-balance` alone makes text wrap, as CSS's `text-wrap: balance` sets the wrap mode to `wrap`.
  It sets only the wrap mode: text that inherits `whitespace-pre` or `whitespace-pre-wrap` keeps its
  spaces and newlines. It used to set no white-space, so on a label that did not already wrap it had no
  effect without a wrapping `whitespace-*` or `text-wrap` beside it. A white-space class on the same
  element, or on one nearer the text, still decides the white-space. `!text-balance` and
  `text-balance!` balance too, and a later `text-pretty` on the same element turns balancing off.

- `leading-[…]` takes CSS `line-height`'s values. A unitless number (`leading-[1.5]`) multiplies each
  text's own font size; an `em` length or a percentage (`leading-[1.5em]`, `leading-[150%]`) is taken
  against the size of the element that declares it, when that element sets an inline pixel size of its
  own (`text-[20px]`) or the text is at that size; `rem` is 16px. Only `px` used to be read, and every other value was ignored.

### Added

- `text-pretty`, which sets the wrap mode the way `text-balance` does and keeps the engine's own line
  breaks.
