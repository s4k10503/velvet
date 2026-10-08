### Changed

- A `Label` Velvet creates — a `V.Label`, a `V.Text`, a `V.Custom<Label>` — starts with zero margin and
  padding, as Tailwind v4's preflight gives every element, through the class `velvet-label`. It used to keep
  the margin and padding the panel's theme gives a `Label`, so text centred in a box now sits on the box's
  centre and a layout that counted on that spacing loses it. A `p-*`, `m-*` or `px-[..]` on the label still
  applies. A `Label` UI Toolkit builds inside its own control, such as a `V.TextField`'s label, and a `Button`
  keep the theme's spacing. The reset the flexbox guide used to suggest for centred labels, which also set
  `flex-shrink: 0`, is no longer needed: the minimum size below replaces that half.

- A `Label` or `Button` Velvet creates cannot shrink below its content along its parent's main axis, as a
  CSS flex item cannot (`min-width: auto` / `min-height: auto`). In a row its minimum width is the widest
  run of text with no break opportunity, so a long label beside a fixed-width sibling still wraps, down to
  that run, and keeps the whole text under `whitespace-nowrap`; in a column its minimum height is the height
  the text takes at the width it was given, so a label in a box shorter than its text overflows instead of
  being squeezed. A declared `w-*` / `h-*` caps the minimum at that size and a `max-w-*` / `max-h-*` at the
  maximum. An item that is clipped (`overflow-hidden`, `truncate`), declares its own `min-w-*` / `min-h-*`
  on the axis, sits in a `grid-cols-*` column or has children keeps its cascade's value.
