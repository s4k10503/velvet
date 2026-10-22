# Styling notes: Box shadows

UI Toolkit has no `box-shadow`, so Velvet paints `shadow-*` itself: each layer of the shadow is baked once by
the `Velvet/BoxShadow` shader and drawn behind the element's content in its own `generateVisualContent`
([player-builds.md](player-builds.md) owns how the shader ships). The shadow does not take part in layout and
follows a transform on the element, as a CSS box-shadow does. `drop-shadow-*` is a filter, not a box shadow
([styling-filters.md](styling-filters.md#drop-shadow-)).

## Utilities

| Utility | Layers, first on top |
|---|---|
| `shadow-2xs` | `0 1px` black at 5% |
| `shadow-xs` | `0 1px 2px 0` black at 5% |
| `shadow`, `shadow-sm` | `0 1px 3px 0` and `0 1px 2px -1px`, both black at 10% |
| `shadow-md` | `0 4px 6px -1px` and `0 2px 4px -2px`, both black at 10% |
| `shadow-lg` | `0 10px 15px -3px` and `0 4px 6px -4px`, both black at 10% |
| `shadow-xl` | `0 20px 25px -5px` and `0 8px 10px -6px`, both black at 10% |
| `shadow-2xl` | `0 25px 50px -12px` black at 25% |
| `shadow-inner` | `inset 0 2px 4px 0` black at 5% |
| `shadow-none` | none |
| `shadow-[<shadow>(,<shadow>)*]` | each shadow two to four lengths (x, y, blur, spread), an optional colour and an optional `inset`, with `_` for spaces: `shadow-[0_4px_8px_-2px_#0004,inset_0_1px_2px_#0002]` |
| `inset-shadow-2xs` | `inset 0 1px` black at 5% |
| `inset-shadow-xs` | `inset 0 1px 1px` black at 5% |
| `inset-shadow-sm` | `inset 0 2px 4px` black at 5% |
| `inset-shadow-none` | none |
| `inset-shadow-[<shadow>(,<shadow>)*]` | as `shadow-[…]` without the `inset`, which is written ahead of every shadow: `inset-shadow-[0_2px_4px_#0004]` |
| `inset-ring`, `inset-ring-<n>`, `inset-ring-[<length>]` | `inset 0 0 0` and the width: 1px, `<n>` px, or the bracket's length |

## Colours

| Utility | Colour |
|---|---|
| `shadow-<color>`, `inset-shadow-<color>`, `inset-ring-<color>` | a palette name, `transparent`, `current` (the element's text colour) or a bracketed colour (`shadow-[#f00]`), taking the place of the colour of every layer of its own family; `/N` scales its alpha (`shadow-red-500/40`) |
| `shadow-<size>/N`, `inset-shadow-<size>/N` | sets the alpha of every layer of the size to N%, or, beside a colour class, scales that colour's alpha by it (`shadow-lg/50`) |
| `shadow-inherit`, `inset-shadow-inherit`, `inset-ring-inherit` | the colour its family's classes declare on the parent element, with the parent's size `/N` folded in, or the grandparent's where the parent's is itself `-inherit`; each layer keeps its own colour where that element declares none. It takes no `/N` |

An `inset-ring-*` with no colour class is drawn in the element's text colour, as CSS's `currentColor`.
The parent `-inherit` reads is the element's parent in the UI Toolkit hierarchy, which for a child of a
`V.ScrollView` is the scroll view's content container rather than the scroll view, and declares no colour. The
inherited colour is read when the element's shadow is drawn.

The presets are Tailwind v4's `--shadow-*` and `--inset-shadow-*` theme values. An `inset-shadow-*`, an
`inset-ring-*` and a `shadow-*` on one element are all drawn, listed in that order, and each `-none` (or
`inset-ring-0`) removes its own family's layers alone. A layer of a bracketed shadow without a colour takes
`rgb(4 12 24 / 0.2)`, where CSS takes `currentColor`. A negative blur, a percentage, or any layer that does not
read rejects the whole bracket, as the browser drops a `box-shadow` value it cannot read.

## How a layer is drawn

Each layer follows CSS Backgrounds 3:

- **Shape.** For an outer shadow, the element's border box moved by the offset and grown by a positive spread
  or shrunk by a negative one. Each corner's radius is the element's plus the spread, floored at zero; where a
  positive spread exceeds the radius it is first scaled by `1 + (r − 1)³`, `r` being the radius over the
  spread.
- **Inset.** An `inset` shadow is drawn inside the padding box only, around a hole: the padding box moved by the
  offset and shrunk by a positive spread, its corner radii taken by the same rule with the spread negated. It
  is drawn above the element's background and below its border.
- **Blur.** A Gaussian whose standard deviation is half the blur radius.
- **Outside only.** An outer shadow is cut out where the element's border box lies, down to a half-pixel
  anti-aliased edge, so a translucent background shows what is behind the element rather than its shadow.
- **Order.** The first layer of a list is drawn on top.
- **Transform.** On a `skew-*` element the whole layer shears with the element about its centre, offset
  included. With both axes skewed, by `skew-x-*` and `skew-y-*` or by one `skew-<n>`, the shear is Tailwind's
  `skewX() skewY()` product, which is also how the element's own face, its bounds and its children's seating are
  sheared.

Each corner keeps its own radius. A padding-box corner is the border-box radius less the wider of the two borders
meeting there. Corner radii are circular: a corner has one radius, where CSS's `border-radius` takes a horizontal
and a vertical one. Where the two borders at a corner differ, the wider is taken off both axes, where CSS takes
each border off its own axis and leaves an elliptical corner.
