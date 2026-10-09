### Changed

- A registered font family picks its weight in CSS's font matching order rather than by the nearest
  weight. A request from 400 to 500 now takes the lightest weight from the request up to 500, then the
  heaviest below the request, then the lightest above 500; a request under 400 the heaviest at or below
  it first, and one over 500 the lightest at or above it first. So `font-medium` over a family
  registering 400 and 600 now renders the 400 face, where it used to render the 600 one. The face's
  style also narrows before its weight, as in CSS: `font-bold italic` over a family whose only italic
  face is the regular one renders that italic with synthesized bold, where it used to render the bold
  upright with synthesized italic. Of two entries registered at one weight the later is used, as the
  later of two `@font-face` rules for one face is in CSS; the earlier used to be. `VelvetFontFamily.FindClosestWeight`
  returns the entry this order selects.

- `text-balance` alone makes text wrap, as CSS's `text-wrap: balance` sets the wrap mode to `wrap`.
  It used to set no white-space, so on a label that did not already wrap it had no effect without a
  wrapping `whitespace-*` or `text-wrap` beside it. `!text-balance` and `text-balance!` balance too.

- `text-wrap` and `text-nowrap` set the wrap mode and leave the collapse alone, as CSS's
  `text-wrap: wrap | nowrap` does, so
  text that inherits `whitespace-pre`, `whitespace-pre-wrap` or `whitespace-pre-line` keeps its spaces
  and newlines under them. They used to be `white-space: normal | nowrap`, which collapsed those too. The
  same holds for `text-balance` and `text-pretty`, and a white-space class on the same element, or on one
  nearer the text, still decides the white-space. Among `text-wrap`, `text-nowrap`, `text-balance` and
  `text-pretty` on one element, the later class wins.

- `truncate` now stops an ancestor's `whitespace-pre-line`, as its `white-space: nowrap` resets the
  collapse; the ancestor's pre-line used to override it on the text.

- `leading-[…]` takes CSS `line-height`'s values. A unitless number (`leading-[1.5]`) multiplies each
  text's own font size; an `em` length or a percentage (`leading-[1.5em]`, `leading-[150%]`) is a length
  computed from the font size of the element that declares it, which every text under it inherits, and
  it follows that size when it changes; `rem` is 16px. Only `px` used to be read, and every other value
  was ignored.

### Fixed

- `!whitespace-pre-line` and `whitespace-pre-line!` collapse spaces like `whitespace-pre-line`; the
  important modifier used to leave the class unread.

### Added

- `text-pretty`, which sets the wrap mode the way `text-balance` does and avoids a last line holding a
  single short word, on Chromium's rule: when that word is narrower than a third of the line, a word
  from the line above moves down to join it, without adding a line.
