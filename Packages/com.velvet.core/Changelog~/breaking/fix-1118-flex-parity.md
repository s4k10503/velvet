### Changed

- `space-x-*` / `space-y-*` follow Tailwind v4's margin rule rather than CSS `gap`: `margin-right` /
  `margin-bottom` on every in-flow child except the last, moved to `margin-left` / `margin-top` by
  `space-x-reverse` / `space-y-reverse`, whatever the container's direction and whether or not it wraps.
  They used to put `margin-left` / `margin-top` on every child except the first, move to the trailing
  edge on their own in a reversed container, and switch to the half-margin strategy under `flex-wrap`. A
  reversed row that relied on `space-x-4` alone adds `space-x-reverse`, as it would with Tailwind.

- `divide-x-*` / `divide-y-*` follow Tailwind v4's divider rule: `border-right` / `border-bottom` on every
  in-flow child except the last, moved to `border-left` / `border-top` by `divide-x-reverse` /
  `divide-y-reverse`. They used to rule the left / top edge of every child except the first and move to
  the trailing edge on their own in a reversed container. A `divide-{color}` colors all four edges of a
  divided child, as Tailwind's `border-color` does, where it colored only the divider's edge.

- A `space-*` margin and a divider give way to a class of the child's own that sets the same edge — `mr-2`,
  `mr-[5px]`, `border-r-4`, `border-red-500` — as Tailwind's zero-specificity rules do. They used to
  overwrite it.

- A `gap-*`, `gap-x-*` or `gap-y-*` no longer reads a `space-x-reverse` / `space-y-reverse` marker, since
  CSS `gap` has none. `flex flex-col gap-4 space-y-reverse` spaced its children on `margin-bottom`; it
  spaces them on `margin-top`, as `flex flex-col gap-4` does. A reversed container still moves a gap to the
  trailing edge.

- A `gap-*` and a `space-*` on one element both apply, as in Tailwind, adding up on an edge both write;
  the later of the two used to replace the other. On a `grid` container, `space-x-*` is a margin on the
  children taken out of their column width rather than the grid's column gap, and `space-y-*` a margin
  rather than its row gap.
