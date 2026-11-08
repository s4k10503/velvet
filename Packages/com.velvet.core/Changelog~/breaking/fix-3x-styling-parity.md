### Changed

- `hue-rotate-*`, `-hue-rotate-*`, `hue-rotate-[…]` and `animate-hue` render the Filter Effects hue-rotation
  matrix on the pixel's sRGB value, as CSS `hue-rotate()` does, through a first-party `Velvet/FilterHueRotate`
  custom filter. They used UI Toolkit's own hue-rotate filter type, whose colours differ from CSS:
  `hue-rotate-90` turned `#ff0000` into `(0, 111, 0)` where CSS gives `(0, 91, 0)`. The element's `style.filter`
  now holds a `FilterFunctionType.Custom` function for them rather than `FilterFunctionType.HueRotate`, and the
  shader is one more that a player build includes unless Project Settings ▸ Velvet leaves it out.
