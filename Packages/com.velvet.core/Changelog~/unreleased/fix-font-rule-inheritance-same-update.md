### Fixed

- A label with a weight, italic or width class now follows a font that a class, a pseudo-state or a stylesheet gives an
  element above it in the update that shows the font on that element, where it followed one update later: as a class
  is added, the element is hovered, or a stylesheet is attached or removed. A label mounted under an element a rule
  gives a font starts in that font's family, where it took the family of the font above that element for its first
  update. An element Velvet renders with such a class and no family class of its own carries the
  `velvet-font-inherit` class and a stylesheet Velvet builds for it while it does.
