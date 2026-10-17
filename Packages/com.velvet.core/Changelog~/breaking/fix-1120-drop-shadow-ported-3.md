### Changed

- `shadow-*` presets are Tailwind v4's, layers and all: `shadow-lg` is `0 10px 15px -3px` and `0 4px 6px -4px`,
  both black at 10%, where it was one soft tinted glow on Velvet's own scale. `shadow-2xs` and `shadow-xs` are
  added. Each layer is drawn as CSS draws it: the spread grows or shrinks the box with CSS's corner-radius
  rule, the blur is a Gaussian of half the blur radius, nothing is drawn inside the element's border box, and
  the first layer is on top. A translucent background no longer shows the shadow through it.
  `shadow-[…]` takes a comma-separated list of shadows, and rejects a negative blur. Each corner of the
  shadow follows its own corner of the element, where all four followed the top-left one.
- `shadow-inner` and an `inset` layer in `shadow-[…]` draw an inner shadow inside the padding box.

### Fixed

- A `shadow-*` on a `skew-y-*` element follows the y shear as well as the x one, composed as Tailwind's
  `skewX() skewY()` transform; it sheared on x alone. Its offset shears with the element, as CSS transforms a
  shadow, and a steep skew no longer cuts the shadow off at the edge of the area it is painted into.
