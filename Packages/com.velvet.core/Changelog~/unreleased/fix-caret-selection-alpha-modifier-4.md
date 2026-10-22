### Added

- The translate utilities that write both axes: `translate-<n>`, `-translate-<n>`, `translate-px`, `translate-full`,
  the fractions (`translate-1/2`) and `translate-[…]`. Each axis holds against `translate-x-*` / `translate-y-*`
  by importance, variant and Tailwind's order, an axis utility of the same rank holding over it.
  A `V.Motion` Spring or Bezier play drives both translate channels from the pixel forms, as it drives
  `translate-x-*` and `translate-y-*`; the percent forms land with the swap.

### Fixed

- A uniform `scale-<n>` keeps the axis a `scale-x-*` or `scale-y-*` variant above it leaves, where a class
  variant on the same element used to drop it to 1.
- A variant's translate or scale of one axis no longer drops the other axis's value once a class variant on the
  element is active: `translate-8 hover:translate-x-2 hover:bg-red-500` keeps its y at 32 while hovered.
- A `V.Motion` pose whose translate holds a value the browser rejects (`translate-x-[10]`) plans no translate
  channel, so the swap lands at once rather than springing one axis and then dropping the translation.
- A `V.Motion` swap adding a `translate-<n>` beside a resting `translate-x-*` or `translate-y-*` leaves that axis
  where the element draws it, important or not, instead of springing it to the both-axis value and snapping back;
  beside an important one the other axis now animates where it used to land.
- A `V.Motion` swap no longer animates a slot that a resting class or value in force under a variant holds
  (`focus:scale-50`, `focus:translate-8`, `focus:opacity-50`, `focus:bg-red-500`); the element keeps drawing that
  value, so the slot lands. A pose class the swap adds stays off the element while such a holder outranks it, as
  in CSS, where it used to be put back and drawn over the holder.
- A `V.Motion` swap beside a resting translate or scale axis holding a value the browser rejects
  (`translate-x-[10]`) lands, where it used to animate the other axis and then drop the translation.
