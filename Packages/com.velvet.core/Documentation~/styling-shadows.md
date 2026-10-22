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

The presets are Tailwind v4's `--shadow-*` theme values. A layer of a bracketed shadow without a colour takes
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
  included. With both `skew-x-*` and `skew-y-*` the shear is Tailwind's `skewX() skewY()` product, which is also
  how the element's own face, its bounds and its children's seating are sheared.

Each corner keeps its own radius. A padding-box corner is the border-box radius less the wider of the two borders
meeting there. Corner radii are circular: a corner has one radius, where CSS's `border-radius` takes a horizontal
and a vertical one — except on a `V.Motion` a `layoutId` move draws at a different scale on each axis, whose
corners take one per axis ([motion.md](motion.md)). Where the two borders at a corner differ, the wider is taken
off both axes, where CSS takes each border off its own axis and leaves an elliptical corner.

## Not supported

These classes read as no shadow, so they are inert and an earlier `shadow-*` stays in force:

- `shadow-<color>` and `shadow-lg/50`: the colour of a preset layer cannot be changed. Write the layers out in
  `shadow-[…]` with a colour.
- `shadow-[#f00]`: a bracket must hold at least the two offset lengths.
- `inset-shadow-*`: write an `inset` layer in `shadow-[…]`, or use `shadow-inner`.
