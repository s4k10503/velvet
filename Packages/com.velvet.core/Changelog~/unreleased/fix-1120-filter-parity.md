### Added

- The `backdrop-*` utilities filter what lies behind an element, inside its rounded border box, and leave the
  element's own background, border and children unfiltered: `backdrop-blur-*`, `backdrop-brightness-*`,
  `backdrop-contrast-*`, `backdrop-grayscale-*`, `backdrop-hue-rotate-*`, `backdrop-invert-*`,
  `backdrop-opacity-*`, `backdrop-saturate-*` and `backdrop-sepia-*`, their bracket forms included, composed in
  Tailwind's order. Each class was previously inert. styling-filters.md lists the values and where the result
  differs from a browser's.
- `blur-[…]` and the lengths of a `drop-shadow-[…]` read `em`, the viewport units and a math function over them,
  measured on the element and measured again when its font size or the panel's size moves, as the sizing and
  spacing utilities do. A math function coming below zero gives a blur or a deviation of zero.

### Fixed

- A filter change that lands at once no longer ends a running transform-origin transition on its element.
- A gradient, a clip path or a projected corner face sizing an element's background no longer ends a running
  transform-origin transition on it.
