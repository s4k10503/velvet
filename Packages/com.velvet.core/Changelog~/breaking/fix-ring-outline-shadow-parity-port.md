### Changed

- A ring and an outline are two bands, as they are two properties in the browser: `ring-4 outline-2` draws a 4px ring
  and a 2px outline, each with its own colour and offset. They shared one band whose width and offset either family
  wrote, so `ring-4 outline-2` was a single 2px band and `outline-offset-*` moved a ring. `ring-none` is no Tailwind
  utility and no longer cancels a ring.
- `ring-offset-<n>` fills the gap in `--tw-ring-offset-color` (white unless `ring-offset-<colour>` names another), over
  the ring, whose spread is its width plus the offset. A `shadow-*`, `inset-ring` or `inset-shadow-*` beside a
  `ring-offset-*` draws the fill too. The gap was transparent.
- A ring, a ring-offset fill and an outline take the shape CSS gives them. A ring's corner grows by the spread by CSS
  Backgrounds 3's formula, so the ring of a square element is square where it was rounded by the ring's width; an
  outline's corner grows by its offset and width where the element is rounded and stays square where it is not; the ring's
  hole keeps the element's radii. A `ring-inset` band takes the padding box's corners, each axis less the border beside it.
- An outline paints after all the content of its stacking context, over a later sibling's face, and is clipped by its
  own ancestors' overflow only. It was clipped by the nearest ancestor that clips, and content after that ancestor covered
  it.
- A ring, ring-offset and outline colour takes an opacity modifier in the forms Tailwind generates (`/50`, `/2.25`,
  `/[0.33]`, `/[33.3%]`), and `ring-inherit`, `ring-offset-inherit` and `outline-inherit` take the parent's value. A
  modifier outside 0–100% on a ring, ring-offset, `shadow-*`, `inset-shadow-*` or `inset-ring-*` colour discards the
  element's whole box-shadow, as the browser does, and on an outline palette colour leaves it the text colour. A shadow size's
  `/N` outside that range beside a shadow colour discards the box-shadow, and on the size alone clamps its own colour's
  alpha. They drew as if the modifier were within range or were ignored.
- A colour modifier that is not a form Tailwind generates (`/05`, `/.5`, `/50.0`) names no utility. `/05` read as 5%.
- A change of a `shadow-*` colour re-paints the descendants that inherit it with `shadow-inherit`, though their own
  classes did not change.
- `text-current`, `bg-current`, `border-current`, their `/N` modifiers and `text-inherit`, `bg-inherit` and
  `border-inherit` read the element's text colour and the parent's value of the same property, and follow them as
  they change, as Tailwind v4 writes `currentcolor` and `inherit`. `inherit` takes no modifier. They matched no rule.
- A colour utility whose opacity modifier lies outside 0–100% and which Tailwind writes through a variable (a palette
  name, a bracketed `var()`) leaves its property unset, as the browser computes the invalid `color-mix()`:
  `bg-red-500/150` is transparent over an earlier `bg-blue-500` (it was declined, so the earlier colour stood),
  `text-red-500/150` inherits the text colour and `border-red-500/150` is `currentcolor`. Any other colour
  (`bg-[#f00]/150`, `bg-transparent/150`) is declined, as the browser drops it, and `bg-current/150` is `currentcolor`.
