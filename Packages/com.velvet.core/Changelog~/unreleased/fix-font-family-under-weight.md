### Fixed

- A weight, italic or width class now keeps the `font-family` CSS gives the element: the nearest declaration wins, so
  a font an author's stylesheet rule gives the element itself, or an element between it and a `font-<name>`
  ancestor, or an inline font on such an element, decides the family instead of the `font-<name>` ancestor, and a
  weight class on an element whose own rule sets a font keeps that font's family rather than the parent's.
  An author font an element gains after the element resolved reaches it when it next resolves.
