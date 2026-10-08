# Styling notes: Gradients

USS has no gradient, so Velvet resolves the gradient utilities itself and bakes the result into a
texture it sets as the element's background image, stretched to the box and clipped to its
`rounded-*` radius. On a `skew-*` element the same gradient is drawn by the sheared silhouette instead
([player-builds.md](player-builds.md) covers the shader behind it).

## Shapes

| Utility | Shape |
|---|---|
| `bg-gradient-to-{dir}` / `bg-linear-to-{dir}` | linear, toward `t` `tr` `r` `br` `b` `bl` `l` `tl` |
| `bg-linear-{n}` / `-bg-linear-{n}` / `bg-linear-[{n}deg]` | linear at an angle in degrees, 0 pointing up, clockwise |
| `bg-radial` / `bg-radial-[at_{position}]` | radial from a centre (default the middle) out to the farthest corner |
| `bg-conic` / `bg-conic-{n}` / `bg-conic-[from_{n}deg]` | conic, sweeping clockwise from a start angle |

A position is keywords (`top`, `left`, `center`, …) or percentages, x before y: `at_top_left`,
`at_25%_75%`. A trailing `/srgb` (the default), `/oklab` or `/oklch` on the shape picks the space the
colours are interpolated in; `/oklch` interpolates in OKLab rather than along the OKLCH hue arc.

The last shape utility in the class list wins, and a stop utility with no shape utility is inert.

## Stops from `from-` / `via-` / `to-`

`from-{colour}`, `via-{colour}` and `to-{colour}` take a palette name or a bracketed value
(`from-[#0f172a]`); `from-{n}%`, `via-{n}%` and `to-{n}%` move that stop from its default of 0%, 50% or
100%. With only one of `from-` and `to-` given, the other end is that colour made transparent.

## Stop lists in the shape's brackets

A shape bracket holding a comma is read as the argument list of the matching CSS function, with `_` for
a space, as Tailwind v4 reads the arbitrary value of `bg-linear-[…]`, `bg-radial-[…]` and
`bg-conic-[…]`. Five stops put a narrow highlight on a steady background:

```text
bg-linear-[90deg,#0f172a_0%,#0f172a_45%,#ffffff_50%,#0f172a_55%,#0f172a_100%]
bg-linear-[to_right,slate-900,white,slate-900]
bg-radial-[at_top_left,#ffffff,#0f172a_60%]
bg-conic-[from_90deg_at_25%_75%,red,yellow,red]
```

- **The optional first argument** sets the shape: an angle (`90deg`) or `to_{side}` / `to_{side}_{side}`
  for linear; `at_{position}` for radial; `from_{n}deg`, `at_{position}`, or `from_{n}deg_at_{position}`
  for conic. Without one, a linear list runs to bottom, a radial one from the middle and a conic one
  from 0deg, as in CSS. An `in_srgb`, `in_oklab` or `in_oklch` at the start or end of it picks the
  interpolation space (`to_right_in_oklab`, or `in_oklab` alone), and wins over a `/` modifier on the
  same class.
- **Each stop** is a colour, then none, one or two percentages. The colour is a palette name
  (`slate-900`), a bracketed value, or anything the arbitrary `bg-[…]` value takes: `#0f172a`,
  `rgb(15,23,42)`, or a basic colour name such as `red`. Two percentages make the colour hold between
  them (`red_0%_40%`) and count as two stops.
- **Between 2 and 16 stops.**
- **Missing positions are filled in as CSS fills them**: the first stop defaults to 0% and the last to
  100%, a position behind an earlier one is raised to it, and a run of stops without positions is spread
  evenly between the positioned stops on either side. Two stops at one position make a hard edge.
- **A malformed list leaves the class inert**, as an unknown angle does, so a shape utility before it
  in the class list still applies. Malformed covers an argument that is neither a stop nor the shape's
  first argument, an unreadable colour or position, more than two positions on one stop, fewer than 2
  or more than 16 stops, and a `-` in front of `bg-linear-[…]`.

A list does not change what `animate-gradient` and `animate-shimmer` do; [motion.md](motion.md) covers
both.

## Where this differs from CSS and Tailwind

- **The list owns the stops.** While the winning shape utility carries a list, `from-` / `via-` / `to-`
  utilities on the element are not read; a later shape utility without a list hands the stops back to
  them. Tailwind instead appends the `from-` / `via-` / `to-` stops after the bracketed arguments.
- **The interpolation modifier is accepted after a list** (`bg-linear-[to_right,red,blue]/oklch`), where
  Tailwind drops a bracketed shape that carries a modifier.
- **`from-` / `via-` / `to-` positions are not fixed up.** The utilities paint as they did before stop
  lists existed: at or before the `from` position the `from` colour, at or after the `to` position the
  `to` colour, and in between `from` to `via` short of the `via` position and `via` to `to` from it
  on. Out of order, such as `from-60%` with `via-` at its 50% default, that is not what CSS paints for
  the same stops.
- Positions are percentages only, and are clamped to 0%–100%. A length (`20px`), a conic stop at an
  angle (`red_90deg`) and a colour hint (a bare position between two stops) make the list malformed.
- A radial shape or size keyword (`circle`, `ellipse`, `closest-side`, …) is not read, so a list that
  opens with one is malformed. `in_{space}` takes only the three spaces above, and no hue-interpolation
  method (`longer_hue`).
- The first argument is read as leniently as the shape's bracket without a list: an angle may be a bare
  number (`45`), while `turn`, `rad` and `grad` make the list malformed, and an `at_` position ignores a
  token it does not recognise rather than rejecting the list.
- Exactly at the first stop's position the first colour shows, even where a second stop shares that
  position: `red_0%,blue_0%` keeps a red edge at the gradient's very start.
- The texture is stretched to the box, so an angle is laid out as if the box were square rather than at
  its physical angle, and a radial gradient is an ellipse matching the box's aspect. Its 128 texels per
  side set how sharp a hard edge or a narrow band can be.
