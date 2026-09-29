### Added

- `grow-<N>` / `shrink-<N>` take a whole-number factor, as Tailwind's bare values do: `grow-3` sets
  `flex-grow: 3`. A fraction still takes the bracket form, `grow-[2.5]`.

- Tailwind v4's `flex` shorthand utilities: `flex-<N>` (`flex: <N>`), `flex-<a>/<b>`
  (`flex: calc(<a>/<b> * 100%)`) and `flex-[…]`, which takes the CSS shorthand with `_` for each space,
  such as `flex-[2_1_120px]` or `flex-[none]`.

- Negative space utilities, `-space-x-*` / `-space-y-*`.

### Fixed

- An element the reconciler removes from a `gap-*`, `grid-cols-*`, `divide-*` or `[&>*]:` container no
  longer keeps the margin, width, divider or payload that container wrote on it. The value stayed on an
  element that was discarded rather than pooled, so code that kept a reference to one and re-parented it
  carried its old container's styling along.

- Dropping `flex-wrap` from a `gap-*` container without writing `flex-nowrap` in its place ends the wrap
  spacing on that patch, and dropping every direction class (`flex` included) moves a plain `gap-*` to the
  column axis the container then lays out on. Both verdicts fell back to a `resolvedStyle` that could still
  hold the removed class, so a fixed-size container kept the old spacing until something unrelated
  re-applied the gap.
