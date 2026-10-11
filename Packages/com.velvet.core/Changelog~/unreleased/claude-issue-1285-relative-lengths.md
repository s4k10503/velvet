### Added

- The inline length utilities — sizing, position, padding, margin, `text-[…]` and `tracking-[…]` — read the
  bracketed lengths only an element can measure: `em`, `vw` / `vh` / `vmin` / `vmax` and their `s` / `l` /
  `d` forms, and a math function mixing a percentage with a length (`w-[calc(50%-1rem)]`). Each is measured
  against the element's font size, the panel and the parent's box, and measured again when those change.
  These classes were inert before. See `Documentation~/styling-arbitrary-lengths.md`.
