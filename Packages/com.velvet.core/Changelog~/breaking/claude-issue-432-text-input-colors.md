### Changed

- A bracket the browser would reject in the CSS Tailwind writes for it now leaves the class inert, as it does
  in a browser: `rotate-[45]` and `rotate-[0]` (the `rotate` property takes no bare number), `bg-linear-[45]` (a
  bare angle other than `0`) and `bg-conic-[45deg]` (an angle without `from`). `-bg-linear-[…]` takes only a
  lower-case unit, as Tailwind's angle test does.
- A bare number other than `0` in a length bracket leaves the class inert, as the browser rejects the
  unitless length Tailwind writes for it: `w-[120]`, `p-[4]`, `rounded-[8]`, `translate-x-[10]`, `blur-[2]`,
  `gap-[8]`, `border-[2]`, `ring-[2]` and `shadow-[0_4_8_#000]` used to read it as pixels. `text-[16]` and
  `text-[0]` are a colour to Tailwind and inert too. `outline-[2]` and `outline-[50%]` draw a 3px outline, the
  width the browser falls back to when it drops theirs.
- A value Tailwind composes into an element's box-shadow and the browser rejects now discards the box-shadow,
  as the browser does: a ring colour or offset colour that is no colour (`ring ring-[5PX]`), a ring width that
  is no length (`ring-[length:2]`) and a `shadow-[…]` with a bare non-zero number take the ring and every
  `shadow-*` on the element with them, where the ring used to draw. A `from-` / `via-` / `to-` colour that is
  no colour leaves its stop transparent, and a rejected position returns the stop to its initial one.
- A value the browser rejects in one utility of a family Tailwind composes through one declaration — translate,
  per-axis and uniform scale, skew, the filters — leaves the family at its identity wherever the rejecting class
  applies, as the browser discards the shared declaration: `translate-x-4 translate-y-[10]` no longer translates
  along x, `translate-y-[10] hover:translate-x-4` does not translate on hover either, and
  `drop-shadow-lg blur-[2]` no longer draws its drop shadow. `blur-[-2px]` is declined.
- `ring` is 1px, Tailwind v4's default ring width, where it was 3px, and a ring or outline naming no colour is
  drawn in the element's text colour, as v4's `var(--tw-ring-color, currentcolor)` and `outline-color`'s
  initial value are, where it was blue-500 (at half alpha for a ring). A colour class alone (`ring-red-500`) no
  longer draws a ring, and a ring colour no longer colours an outline.
- `grid-cols-[5]` leaves the class inert, as the browser rejects the `grid-template-columns: 5` Tailwind writes
  for it; it used to lay out five columns. `grid-cols-[repeat(5,minmax(0,1fr))]`, the list `grid-cols-5` writes,
  is five columns.
