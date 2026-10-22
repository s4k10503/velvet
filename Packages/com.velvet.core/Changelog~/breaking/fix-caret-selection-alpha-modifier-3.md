### Changed

- `ring` is 1px, Tailwind v4's default ring width, where it was 3px, and a ring or outline naming no colour is
  drawn in the element's text colour, as v4's `var(--tw-ring-color, currentcolor)` and `outline-color`'s
  initial value are, where it was blue-500 (at half alpha for a ring). A colour class alone (`ring-red-500`) no
  longer draws a ring, and a ring colour no longer colours an outline.
- `grid-cols-[5]` leaves the class inert, as the browser rejects the `grid-template-columns: 5` Tailwind writes
  for it; it used to lay out five columns. `grid-cols-[repeat(5,minmax(0,1fr))]`, the list `grid-cols-5` writes,
  is five columns.
