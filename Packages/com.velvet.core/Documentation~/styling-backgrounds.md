# Styling notes: background repeat and nine-slice

A background image reaches an element through `StyleOverrides.BackgroundImage` (the `styles:` parameter of
the element factories) or through the `bg-[addr:<key>]` class. Two utility families say how that image fills
the element: whether it repeats, and how it is nine-sliced.

## Repeat: `bg-repeat-*`

Tailwind's `bg-repeat` family, declared in the bundled stylesheet, so it needs
`VelvetStyleUtilities.AttachTo` like any other stylesheet utility ([setup.md](setup.md)):

| Class | `background-repeat` |
|-------|---------------------|
| `bg-repeat` | `repeat` |
| `bg-no-repeat` | `no-repeat` |
| `bg-repeat-x` | `repeat-x` |
| `bg-repeat-y` | `repeat-y` |
| `bg-repeat-round` | `round` |
| `bg-repeat-space` | `space` |

## Nine-slice: `slice-*`

Nine-slicing cuts the background image into four corners, four edges and a centre along four insets. On the
web that is `border-image-slice` with `border-image-repeat` and `border-image-width`; UI Toolkit has no
border image and slices the background image instead, through its `-unity-slice-*` properties. Tailwind has
no utility for either, so Velvet's family follows Tailwind's naming for an edge family (`border-x-*`,
`border-t-*`) and its bracket syntax for an arbitrary value:

| Class | Writes |
|-------|--------|
| `slice-[N]` | `-unity-slice-top`, `-unity-slice-right`, `-unity-slice-bottom`, `-unity-slice-left` |
| `slice-x-[N]` | `-unity-slice-left`, `-unity-slice-right` |
| `slice-y-[N]` | `-unity-slice-top`, `-unity-slice-bottom` |
| `slice-t-[N]` / `slice-r-[N]` / `slice-b-[N]` / `slice-l-[N]` | the one edge |
| `slice-scale-[N]` | `-unity-slice-scale` |
| `slice-sliced` / `slice-tiled` | `-unity-slice-type` |

An inset is a whole, non-negative number, written bare as `border-image-slice` writes it (`slice-[12]`) or
in pixels (`slice-[12px]`), or a non-negative percentage of the background image's size on that axis
(`slice-[25%]`): its height for the top and bottom, its width for the left and right, rounded to a whole
pixel. A percentage is resolved against the image the element shows — a `StyleOverrides.BackgroundImage`, a
`bg-[addr:…]` image, a baked gradient, or else the one its stylesheet resolves — and again whenever Velvet
writes a new one; a vector image has no pixel size, and a percentage of it is 0. A fraction of a pixel,
another unit or a negative number is declined.
`slice-[…]` also takes `border-image-slice`'s two, three and four values, with `_` for the space between
them: `slice-[12_8]` sets the top and bottom to 12 and the right and left to 8, `slice-[12_8_4]` the top,
the right and left, and the bottom, and `slice-[12_8_4_2]` the top, right, bottom and left. Pixels and
percentages mix (`slice-[25%_8]`). The edge classes take one value.
`slice-scale-[N]` takes a non-negative number with no unit and declines a negative one or one with a unit.
A declined class writes nothing.

The insets and the scale are resolved in C# and written inline, so they work without the stylesheet;
`slice-sliced` and `slice-tiled` are stylesheet rules. Both kinds take variants and the `!` modifier like the
other utilities (`hover:slice-[4]`, `md:slice-tiled`, `!slice-[8]`), and a later edge class overrides the
same edge of an earlier `slice-[N]` and gives it back when it is removed, as `pt-[…]` does beside `p-[…]`.

On a field control the family goes to the input box with the other background utilities
([styling-variants.md](styling-variants.md)).

`fill` is accepted before or after the values (`slice-[12_fill]`) and changes nothing, because UI Toolkit
paints a sliced background's centre either way (`NineSlicePlaybackTests` pins it). So a slice **without**
`fill` still paints the centre, where `border-image-slice` leaves it empty. No UI Toolkit property turns the
centre off: the slice longhands are `-unity-slice-top`, `-right`, `-bottom`, `-left`, `-scale` and `-type`,
and `SliceType` has two modes, `Sliced` and `Tiled` (`BackgroundRepeatAndSliceTypeUssTests` pins both). An
empty centre would need Velvet to paint the background's eight outer pieces itself.

## `StyleOverrides`

For a value computed at runtime, `StyleOverrides` carries the same properties: `BackgroundRepeat`,
`UnitySliceTop`, `UnitySliceRight`, `UnitySliceBottom`, `UnitySliceLeft`, `UnitySliceScale` and
`UnitySliceType`. They are written inline on mount and on every change, and an override that goes away is
cleared.

`StyleOverrides` is Velvet's `style` prop, and it ranks against the utilities as React's `style` does
against `className`: a member wins over a utility writing the same property whichever was written last,
including a variant one (`hover:bg-[#…]`) — except an important one (`!bg-[#…]`), which wins as an
`!important` rule wins over a `style` attribute. An override that goes away hands the property back to the
utility. That holds for `BackgroundColor`, `Color`, the slice insets, `UnitySliceScale` and
`BackgroundImage` (against `bg-[addr:…]`, which `!bg-[addr:…]` makes important, and the gradient
utilities), a keyword value (`StyleKeyword.Initial`, …) included; `BackgroundRepeat` and `UnitySliceType`
meet only stylesheet rules (`bg-repeat-*`, `slice-tiled`) and are written inline over them. A gradient keeps
baking under an image override, so removing the override shows the gradient's current bake.

An `animate-gradient` or `animate-shimmer` pan holds `background-size`, `background-position` and
`background-repeat` while it runs; when it stops, the element's own inline values return — the gradient's
stretch-to-fill, whatever a `refCallback` wrote, and the current `BackgroundRepeat` override — or none,
which leaves the properties to the classes.

```csharp
V.Div(className: "w-64 h-32", styles: new StyleOverrides
{
    BackgroundImage = new StyleBackground(panelTexture),
    UnitySliceTop = 12, UnitySliceRight = 12, UnitySliceBottom = 12, UnitySliceLeft = 12,
});
```

A pooled element is handed to its next consumer with every slice property and `background-repeat` unset.
