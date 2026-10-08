# Styling notes: Gradients

USS has no gradient, so Velvet resolves the gradient utilities itself and bakes the result into a
texture it sets as the element's background image, stretched to the box and clipped to its
`rounded-*` radius. On a `skew-*` element the same gradient is drawn by the sheared silhouette instead
([player-builds.md](player-builds.md) covers the shader behind it).

A gradient is laid out over the box's real proportions, as CSS lays it out: a diagonal angle
(`bg-linear-45`), a conic and a radial circle are baked for the element's aspect, and baked again when a
layout changes it. A radial sized in pixels is baked for the element's size too. An angle along an axis,
a corner direction (`bg-gradient-to-tr`) and a radial ellipse sized by keyword or percentage come out the
same in every box, so they are baked once. Under `animate-gradient` the gradient is laid out over the
twice-as-large background the pan slides across.

## Shapes

| Utility | Shape |
|---|---|
| `bg-gradient-to-{dir}` / `bg-linear-to-{dir}` | linear, toward `t` `tr` `r` `br` `b` `bl` `l` `tl` |
| `bg-linear-{n}` / `-bg-linear-{n}` / `bg-linear-[{n}deg]` | linear at an angle in degrees, 0 pointing up, clockwise |
| `bg-radial` / `bg-radial-[at_{position}]` | radial from a centre (default the middle), an ellipse out to the farthest corner |
| `bg-radial-[{shape} {size} at_{position}]` | `circle` or `ellipse`; `closest-side`, `closest-corner`, `farthest-side`, `farthest-corner`, or radii: a circle's length, an ellipse's two lengths or percentages (`circle_40px`, `ellipse_50%_20px`) |
| `bg-conic` / `bg-conic-{n}` / `bg-conic-[from_{n}deg]` | conic, sweeping clockwise from a start angle |

A position is keywords (`top`, `left`, `center`, …) or percentages, which may lie outside the box, x before y: `at_top_left`,
`at_25%_75%`. A trailing `/srgb` (the default), `/oklab` or `/oklch` on the shape picks the space the
colours are interpolated in; `/oklch` interpolates in OKLab rather than along the OKLCH hue arc.

The last shape utility in the class list wins, and a stop utility with no shape utility is inert.

## Stops from `from-` / `via-` / `to-`

`from-{colour}`, `via-{colour}` and `to-{colour}` take a palette name or a bracketed value
(`from-[#0f172a]`); `from-{n}%`, `via-{n}%` and `to-{n}%` move that stop from its default of 0%, 50% or
100%, and a position behind an earlier stop is raised to it, as in CSS. A position may lie outside
0%–100%: the gradient line runs on past the box. With only one of `from-` and `to-` given, the other end
is that colour made transparent, and `via-` alone fades in from and out to its own transparent colour.

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
  for linear; a shape, a size and `at_{position}` for radial; `from_{n}deg`, `at_{position}`, or `from_{n}deg_at_{position}`
  for conic. Without one, a linear list runs to bottom, a radial one from the middle and a conic one
  from 0deg, as in CSS. An `in_srgb`, `in_oklab` or `in_oklch` at the start or end of it picks the
  interpolation space (`to_right_in_oklab`, or `in_oklab` alone). An angle is a number with a `deg`,
  `grad`, `rad` or `turn` unit, or a bare `0`.
- **Each stop** is a colour, then none, one or two positions. The colour is a palette name
  (`slate-900`), a bracketed value, or anything the arbitrary `bg-[…]` value takes: `#0f172a`,
  `rgb(15,23,42)`, or a basic colour name such as `red`. A position is a percentage, a length in pixels
  (`red_20px`; not on a conic), or on a conic an angle (`red_90deg`, in the units a first argument takes).
  Two positions make the colour hold between them (`red_0%_40%`) and count as two stops. A position in
  pixels is a share of the gradient line (a radial's ray), so it is placed once the element's size is
  known.
- **A bare position between two stops is a colour hint** (`red,30%,blue`): the half-way mix of those two
  stops is at the hint. A hint is not a stop, and it can be neither first, last nor beside another hint.
- **Between 2 and 64 stops**, counting the `from-` / `via-` / `to-` stops that follow the list.
- **`from-` / `via-` / `to-` follow the list.** When a `from-` or `to-` colour is given, its stops are
  placed after the list's, as Tailwind places them: `bg-linear-[to_right,red,blue] from-green to-white`
  is `red, blue, green 0%, white 100%`.
- **Missing positions are filled in as CSS fills them**, over the list and those stops together: the
  first stop defaults to 0% and the last to 100%, a position behind an earlier one is raised to it, and a
  run of stops without positions is spread evenly between the positioned stops on either side. Where
  stops share a position the later one starts there, so `red_0%,blue_0%` is blue throughout and a hard
  stop at 100% never shows its later colour.
- **Colours interpolate with their alpha**, as CSS interpolates them, so `to_right,#ff0000,transparent`
  stays red while it fades.
- **Underscores are spaces**, so the ones around an argument and doubled within it are ignored: a CSS
  list pasted with a space after each comma (`bg-linear-[90deg,_#0f172a_0%,_#ffffff_100%]`) reads as
  one without.
- **A malformed list leaves a linear or conic class inert**, as an unknown angle does, so a shape
  utility before it in the class list still applies. Malformed covers an argument that is neither a stop
  nor the shape's first argument, an unreadable colour or position, an angle that is a bare number other
  than `0`, an `at_` position with a token that is not `left`, `right`, `top`, `bottom`, `center` or a
  percentage (or with more than two), more than two positions on one stop, fewer than 2 or more than 64
  stops, and a `-` in front of `bg-linear-[…]`.
- **A `/` modifier after a stop list leaves the class inert** on every shape, since Tailwind takes none
  after a bracketed shape. Name the space inside the bracket instead (`in_oklab`).
- **A radial bracket is read as CSS reads it**, with or without stops: `bg-radial-[circle_at_top]` takes its stops from
  `from-` / `via-` / `to-`, and a body that is not a shape, size and position CSS reads (an unknown token, an
  ellipse with one radius) leaves the class inert.
- **A `/` inside the brackets belongs to the bracket**; only one after the closing `]` is a modifier.

A list does not change what `animate-gradient` and `animate-shimmer` do; [motion.md](motion.md) covers
both.

## Where this differs from CSS and Tailwind

- `in_{space}` takes only the three spaces above, and no hue-interpolation method (`longer_hue`).
- The gradient is a 128 by 128 texture stretched to the box, so a hard edge or a narrow band is as sharp
  as 1/128 of the box along each axis allows.
