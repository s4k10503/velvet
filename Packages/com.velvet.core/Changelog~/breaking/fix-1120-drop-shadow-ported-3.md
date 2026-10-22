### Changed

- `shadow-*` presets are Tailwind v4's, layers and all: `shadow-lg` is `0 10px 15px -3px` and `0 4px 6px -4px`,
  both black at 10%, where it was one soft tinted glow on Velvet's own scale. `shadow-2xs` and `shadow-xs` are
  added. A layer's shape, spread, blur and stacking follow CSS Backgrounds 3: the spread grows or shrinks the
  box with CSS's corner-radius rule, the blur is a Gaussian of half the blur radius, an outer layer is cut out
  where the element's border box lies, and the first layer is on top. A translucent background no longer
  shows the shadow through it. Corner radii stay circular where CSS's can be elliptical.
  `shadow-[…]` takes a comma-separated list of shadows, and rejects a negative blur. Each corner of the
  shadow follows its own corner of the element, where all four followed the top-left one.
- `shadow-inner` and an `inset` layer in `shadow-[…]` draw an inner shadow inside the padding box.
- An element with both `skew-x-*` and `skew-y-*` is sheared as Tailwind's `skewX() skewY()` composes them, for
  its face, its bounds, its gradient fill and the seating of its children. It was sheared as the simultaneous
  matrix `[[1, tanX], [tanY, 1]]`, which differs from the product by `tanX·tanY` on the x axis and is singular
  where `tanX·tanY` is 1.

### Fixed

- A `shadow-*` on a `skew-y-*` element follows the y shear as well as the x one, composed as Tailwind's
  `skewX() skewY()` transform; it sheared on x alone. Its offset shears with the element, as CSS transforms a
  shadow, and a steep skew no longer cuts the shadow off at the edge of the area it is painted into.
- A shadow layer on an element under 8px tall or wide, or a layer larger than 2048px, is drawn at its real
  size; its quad was clamped to between 8px and 2048px on each side, so the layer no longer matched the rect
  it was baked for.
