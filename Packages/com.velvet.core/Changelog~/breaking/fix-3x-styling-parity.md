### Changed

- `hue-rotate-*`, `-hue-rotate-*`, `hue-rotate-[…]` and `animate-hue` render the Filter Effects hue-rotation
  matrix on the pixel's sRGB value, as CSS `hue-rotate()` does, through a first-party `Velvet/FilterHueRotate`
  custom filter. They used UI Toolkit's own hue-rotate filter type, whose colours differ from CSS:
  `hue-rotate-90` turned `#ff0000` into `(0, 111, 0)` where CSS gives `(0, 91, 0)`. The element's `style.filter`
  now holds a `FilterFunctionType.Custom` function for them rather than `FilterFunctionType.HueRotate`, and the
  shader is one more that a player build includes unless Project Settings ▸ Velvet leaves it out.
- A `V.Label`, a text leaf or a `V.Button` is as tall as CSS makes it: one line box of its line height per line,
  single lines included. A single line used to take the font's own content height, so in the runtime theme's font
  `text-base` was 20px tall rather than 24px, `text-base leading-8` 20px rather than 32px, and `text-4xl` 44px
  rather than 40px. Text no `leading-*` or `text-<size>` reaches takes Tailwind's preflight line height of 1.5,
  where it used to take the font's own, and an `lh` length under it is 1.5 times the font size; an `rlh` length is
  24px, the preflight's line height at the 16px a rem is taken at, where it used to be the font's own line height.
  Once such text takes more than one line, by a newline, a `<br>` or wrapping, the string it shows carries a
  `<line-height=1.5em>` tag where it used to be the text as written; `leading-[normal]` takes it away. A
  single-line `V.TextField`'s input text is one line box tall too.
- The glyphs of a label's or a text leaf's text sit half the leading below the top of the first line box at any
  box height, as in CSS, including a box a `max-h-*` caps below its lines, whose text starts at its top. The
  element is aligned `middle-*` with the horizontal part of the alignment its rules or its parent give it, so
  `text-top-left`, `text-top-center` and the default alignment now place text as `text-left` and `text-center` do,
  and text in a box taller than its lines, which a `middle-*` alignment used to centre, sits in the first line
  box. A `V.Button`'s lines stay centred in the button, as a browser centres a button's content.
- A `<voffset>` in the application's own rich text is shown that much above or below the line box placement, and
  its own `</voffset>` or `</line-height>` no longer ends the placement or the line height Velvet writes around it.

### Fixed

- `brightness-*`, `saturate-*` and `hue-rotate-*` filter a translucent pixel's colour divided by its alpha, as CSS
  filters a colour that is not premultiplied. They filtered the colour multiplied by the alpha, so the next filter
  read a value CSS clamps (`brightness-150 saturate-0` over half-transparent green gave a gray of 128 over black
  where CSS gives 91), and in a Linear project the colour space conversion darkened a translucent colour.
