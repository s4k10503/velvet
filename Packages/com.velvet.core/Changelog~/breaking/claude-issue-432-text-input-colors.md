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
  wins its property, as the browser discards the shared declaration, while one that an important class, a class
  behind a variant or a class Tailwind emits later outranks takes nothing (`skew-x-[8] !skew-x-6` skews): `translate-x-4 translate-y-[10]` no longer translates
  along x, `translate-y-[10] hover:translate-x-4` does not translate on hover either, nor under a `V.Motion`
  pose or a `whileHoverClass` naming the family unless a pose outranks the rejected value (a `translate-y-4`
  pose over `translate-y-[10]` translates), and
  `drop-shadow-lg blur-[2]` no longer draws its drop shadow. `blur-[-2px]` is declined.
- `ring` is 1px, Tailwind v4's default ring width, where it was 3px, and a ring or outline naming no colour is
  drawn in the element's text colour, as v4's `var(--tw-ring-color, currentcolor)` and `outline-color`'s
  initial value are, where it was blue-500 (at half alpha for a ring). A colour class alone (`ring-red-500`) no
  longer draws a ring, and a ring colour no longer colours an outline.
- A ring and an outline on one element draw two bands, as the browser draws box-shadow and outline, where
  they shared one band, its width and its offset: `ring-4 outline-2` is a 4px ring under a 2px outline where
  it was a single 2px band, and `outline-offset-*` no longer moves a ring.
- `ring-offset-<n>` fills the offset in white, Tailwind's default `--tw-ring-offset-color`, where it left the
  gap transparent, and the ring's colour spreads under that fill as Tailwind's box-shadow composes it. A
  `shadow-*` beside `ring-offset-<n>` draws the fill too. `ring-none`, which Tailwind does not generate, no
  longer cancels a ring.
- Two of an element's own ring, outline or `shadow-*` utilities writing one property resolve in the order
  Tailwind emits them, where the class written last won: `ring-4 ring-2` is 4px, `outline-none outline-2`
  draws no outline, `shadow-none shadow-lg` draws no shadow, and `ring ring-[5PX] ring-[#ff0000]` discards
  the box-shadow, as `ring-[5PX]` is emitted last.
- A `shadow-[…]` naming no colour is drawn in the element's text colour, as Tailwind's
  `var(--tw-shadow-color, currentcolor)` is, where it took the bare `shadow`'s tint; so is a `drop-shadow-[…]`
  naming none, as `drop-shadow()` defaults to `currentcolor`.
- A ring's and its offset fill's outer corners grow by the spread as CSS grows a box-shadow's, so a square
  element's ring is square where it was rounded by the ring's width, and an outline's corners stay square on a
  square element.
- An outline paints over the content around it, as CSS paints it after the rest of its stacking context, where
  a later sibling's face covered it. `ring-inset` paints inside the padding box under the element's children
  and text, where it painted over them across the border box.
- `grid-cols-[5]` leaves the class inert, as the browser rejects the `grid-template-columns: 5` Tailwind writes
  for it; it used to lay out five columns. `grid-cols-[repeat(5,minmax(0,1fr))]`, the list `grid-cols-5` writes,
  is five columns.
