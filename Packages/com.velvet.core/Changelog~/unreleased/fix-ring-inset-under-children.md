### Fixed

- A `ring-inset` band paints under the element's children, above the element's own background, as CSS's
  inset box-shadow does. It painted over an opaque full-bleed child. The band is now the element's own first
  child, over its padding box, and rounds to the padding box's corners; it was a sibling that followed the
  element.
