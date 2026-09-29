### Added

- `grow-<N>` / `shrink-<N>` take a whole-number factor, as Tailwind's bare values do: `grow-3` sets
  `flex-grow: 3`. A fraction still takes the bracket form, `grow-[2.5]`.

### Fixed

- An element the reconciler removes from a `gap-*`, `grid-cols-*` or `divide-*` container no longer keeps
  the margin, width or divider that container wrote on it. The spacing stayed on an element that was
  discarded rather than pooled, so code that kept a reference to one and re-parented it carried its old
  container's gap along.

- Dropping `flex-wrap` from a `gap-*` container without writing `flex-nowrap` in its place ends the wrap
  spacing on that patch. The verdict fell back to a `resolvedStyle` that could still hold the removed
  class, so a fixed-size container kept its negative margin and its children's half-margins until
  something unrelated re-applied the gap.
