### Changed

- `hue-rotate-*`, `-hue-rotate-*`, `hue-rotate-[…]` and `animate-hue` render the Filter Effects hue-rotation
  matrix on the pixel's sRGB value, as CSS `hue-rotate()` does, through a first-party `Velvet/FilterHueRotate`
  custom filter. They used UI Toolkit's own hue-rotate filter type, whose colours differ from CSS:
  `hue-rotate-90` turned `#ff0000` into `(0, 111, 0)` where CSS gives `(0, 91, 0)`. The element's `style.filter`
  now holds a `FilterFunctionType.Custom` function for them rather than `FilterFunctionType.HueRotate`, and the
  shader is one more that a player build includes unless Project Settings ▸ Velvet leaves it out.
- A `V.Label`, a text leaf or a `V.Button` under a line height (`leading-*`, `text-<size>` or `text-<size>/<n>`)
  is as tall as CSS makes it: one line box of the line height per line, single lines included. A single line used
  to take the font's own content height, so in the runtime theme's font `text-base` was 20px tall rather than 24px, `text-base leading-8` 20px
  rather than 32px, and `text-4xl` 44px rather than 40px. The glyphs sit half the leading below the top of the
  first line box, as in CSS, under a `middle-*` alignment at any box height and under an `upper-*` alignment
  where the line height is at least the font's content height; under a `middle-*` alignment a box taller than
  its lines used to centre them.
