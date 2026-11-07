### Fixed

- A border-width utility (`border`, `border-2`, `border-x-4`, `border-s-2`, `border-[3px]`, under any variant) now draws
  in the element's text colour, as Tailwind v4's preflight `border-color: currentcolor` does, and follows the text colour
  as it changes. So do the children of a `divide-*` parent (dashed dividers included), the target of a child-scoped width
  (`*:border`, a field's input box), and an element a USS rule of a stylesheet that is not a control theme's gives a border width when the rule selects it by a
  class, its name, its type or `*`. It gives way, side by side, to any rule of a stylesheet that is not a control theme's that sets
  that side's border colour (a `border-*` colour utility or variant, a USS rule of any selector, `:hover` and
  `border-color: transparent` included) and to a colour your code writes inline, unless that is the colour Velvet last
  wrote to the side. A control theme's border does not hold it back, as preflight outranks a browser's default styles.
  A width written inline by code gives no preflight colour to an element no rule selects. A stylesheet added to an
  ancestor of an element that has attached is noticed within a frame, and the elements under it are asked again.
- Bundled border widths (`border`, `border-2`, `border-x`, `border-t-4`, …) are snapped to device pixels at a panel
  scale that is not a whole number, as a browser snaps them, and again when the scale changes: `border` is two thirds of
  a point at 1.5 device pixels a point. A stylesheet placed directly after the bundled utilities on the root they are
  attached to (`VelvetStyleUtilities.AttachTo`, a portal host, or a root a project attached them to itself, found from
  its first element with a border-width utility) carries the snapped rules, so a project that reads the root's style
  sheet list sees one more entry there.
