### Changed

- A `Label` — a `V.Label`, a `V.Text`, and any `Label` UI Toolkit builds inside its own controls — starts with
  zero margin and padding, as Tailwind v4's preflight gives every element. It used to keep the margin and
  padding the panel's theme gives a `Label`, so text centred in a box now sits on the box's centre and a
  layout that counted on that spacing loses it. A `p-*`, `m-*` or `px-[..]` on the label still applies, and a
  rule of your own against `.unity-label` ties with the new baseline and is decided by sheet order. The
  reset the flexbox guide used to suggest for centred labels is no longer needed.

- A `Label` or `Button` Velvet creates cannot shrink below its content along its parent's main axis, as a
  CSS flex item cannot (`min-width: auto` / `min-height: auto`): in a row its minimum width is the widest
  word, or the whole text under `whitespace-nowrap`; in a column its minimum height is the height the text
  takes at the width it was given. A long label beside a fixed-width sibling used to wrap down to a sliver,
  and a label in a column shorter than its text used to be squeezed; both now keep their text's size and
  overflow instead. An item that is clipped (`overflow-hidden`, `truncate`) or declares its own `min-w-*` /
  `min-h-*` or its own size on the axis keeps its cascade's value, as does one with children.
