### Added

- The `backdrop-*` utilities filter what lies behind an element, inside its rounded border box, and leave the
  element's own background, border and children unfiltered: `backdrop-blur-*`, `backdrop-brightness-*`,
  `backdrop-contrast-*`, `backdrop-grayscale-*`, `backdrop-hue-rotate-*`, `backdrop-invert-*`,
  `backdrop-opacity-*`, `backdrop-saturate-*` and `backdrop-sepia-*`, their bracket forms included, composed in
  Tailwind's order, and `backdrop-filter-none` and `backdrop-filter-[…]`, a list run in the order written. They
  work under variants, the important modifier, the custom-property forms, an ancestor's rounded clip and a
  `clip-path-*`, and transition under `transition-all` by CSS's filter-list interpolation. Each class was previously
  inert. styling-filters.md lists the values and where the result differs from a browser's.
- `brightness-*`, `contrast-*`, `saturate-*`, `grayscale-*`, `invert-*`, `sepia-*` and `hue-rotate-*` take any
  whole number, as Tailwind v4 does (`brightness-80`, `hue-rotate-45`); only the old preset values were read before.
- `blur-[…]` and the lengths of a `drop-shadow-[…]` read `em`, the viewport units and a math function over them,
  measured on the element and measured again when its font size or the panel's size moves, as the sizing and
  spacing utilities do. A math function coming below zero gives a blur or a deviation of zero.

### Fixed

- A filter change that lands at once no longer ends a running transform-origin transition on its element.
- A gradient, a clip path or a projected corner face sizing an element's background no longer ends a running
  transform-origin transition on it.
- A `z-*` element whose skew or shadow paints its border on an overlay keeps that overlay directly after it when
  the layer resorts; the overlay went to the end of the layer, over the elements stacked above.
