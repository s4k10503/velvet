### Fixed

- A border-width utility (`border`, `border-2`, `border-x-4`, `border-[3px]`, under any variant) now draws in the
  element's text colour, as Tailwind v4's preflight `border-color: currentcolor` does, and follows the text colour as it
  changes. It gives way, side by side, to any rule of a stylesheet that is not a control theme's that sets that side's
  border colour: a `border-*` colour utility or variant, and a USS rule of any selector, `:hover` and
  `border-color: transparent` included. A control theme's border does not hold it back, as preflight outranks a browser's
  default styles.
- Bundled border widths (`border`, `border-2`, `border-x`, `border-t-4`, …) are snapped to device pixels at a panel
  scale that is not a whole number, as a browser snaps them, and again when the scale changes: `border` is two thirds of a
  point at 1.5 device pixels a point. A stylesheet that follows the bundled utilities on the root they are attached to
  carries the snapped rules; a project that reads widths off the root's style sheet list sees one more entry there.
