### Changed

- `space-x-*` / `space-y-*` follow Tailwind's margin rule rather than CSS `gap`. The margin no longer moves
  to the trailing edge on a `flex-row-reverse` / `flex-col-reverse` container on its own: it stays on the
  leading edge until `space-x-reverse` / `space-y-reverse` moves it, so a reversed row that relied on
  `space-x-4` alone adds `space-x-reverse`, as it would with Tailwind. A wrapping container no longer puts
  `space-*` through the half-margin strategy `gap-*` uses: every in-flow child but the first takes the whole
  margin on one edge, and the container's own margin is left alone.

- A `gap-*`, `gap-x-*` or `gap-y-*` no longer reads a `space-x-reverse` / `space-y-reverse` marker, since
  CSS `gap` has none. `flex flex-col gap-4 space-y-reverse` spaced its children on `margin-bottom`; it
  spaces them on `margin-top`, as `flex flex-col gap-4` does. A reversed container still moves a gap to the
  trailing edge.
