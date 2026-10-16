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
| `shadow-none` | none |
| `shadow-[<shadow>(,<shadow>)*]` | each shadow two to four lengths (x, y, blur, spread) and an optional colour, with `_` for spaces: `shadow-[0_4px_8px_-2px_#0004,0_1px_2px_#0002]` |

The presets are Tailwind v4's `--shadow-*` theme values. A layer of a bracketed shadow without a colour takes
`rgb(4 12 24 / 0.2)`, where CSS takes `currentColor`. A negative blur, a percentage, or any layer that does not
read rejects the whole bracket, as the browser drops a `box-shadow` value it cannot read. `inset` shadows,
`shadow-inner` and `inset-shadow-*` are not implemented.

## How a layer is drawn

Each layer follows CSS Backgrounds 3:

- **Shape.** The element's border box, moved by the offset and grown by a positive spread or shrunk by a
  negative one. Its corner radius is the element's plus the spread, floored at zero; where a positive spread
  exceeds the radius it is first scaled by `1 + (r − 1)³`, `r` being the radius over the spread.
- **Blur.** A Gaussian whose standard deviation is half the blur radius.
- **Outside only.** Nothing is drawn inside the element's border box, so a translucent background shows what is
  behind the element rather than its shadow.
- **Order.** The first layer of a list is drawn on top.
- **Transform.** On a `skew-*` element the whole layer shears with the element about its centre, offset
  included.

The element's corner radius is read from its top-left corner and used for all four, for both the shape and the
cut-out.
