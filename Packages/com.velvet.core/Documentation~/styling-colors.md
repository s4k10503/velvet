# Styling notes: Colours

Every utility that takes a colour in brackets reads it with one grammar: `bg-[…]`, `text-[…]` and
`border-[…]` and their opacity modifier (`bg-[…]/50`), `ring-[…]` and `outline-[…]`, `divide-[…]`,
`shadow-[…]` and `drop-shadow-[…]`, the gradient stops (`from-[…]`, `via-[…]`, `to-[…]` and the colours of
a stop list), a text input's `caret-[…]`, `selection:bg-[…]` and `selection:text-[…]` and their opacity
modifier, and the colour argument of a custom filter (`filter-[name:…]`). An underscore stands for a
space: `bg-[rgb(255_0_0_/_50%)]` is `rgb(255 0 0 / 50%)`.

## Accepted forms

| Form | Example |
|---|---|
| `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa` | `bg-[#0f172a]` |
| a colour name Unity's `ColorUtility` knows (`red`, `blue`, `white`, `black`, …) | `text-[red]` |
| `transparent` | `bg-[transparent]` |
| `rgb()` / `rgba()`, comma syntax | `bg-[rgb(15,23,42)]`, `bg-[rgba(0,_0,_0,_0.5)]` |
| `rgb()` / `rgba()`, space syntax | `bg-[rgb(15_23_42_/_50%)]` |
| `hsl()` / `hsla()`, comma syntax | `text-[hsl(210,40%,50%)]` |
| `hsl()` / `hsla()`, space syntax | `text-[hsl(210_40%_50%_/_0.8)]` |
| `hwb()` | `border-[hwb(150_20%_10%)]` |

`lab()`, `lch()`, `oklab()` and `oklch()` are not read, whatever their components: the class declines.

The functions follow CSS Color 4:

- **The space syntax** separates the three components with spaces and takes an optional alpha after a `/`,
  with or without spaces around it. A component may be a number, a percentage or `none`, mixed freely;
  `none` reads as zero.
- **The comma syntax** exists for `rgb()` / `rgba()` and `hsl()` / `hsla()` only. It takes no `none`, its
  `rgb()` channels are all numbers or all percentages, and its `hsl()` saturation and lightness are
  percentages. `rgb()` and `rgba()` are the same function, as are `hsl()` and `hsla()`: either takes three
  arguments or four.
- **Percentages**: an `rgb()` channel's 100% is 255; saturation, lightness, whiteness and blackness read a
  number as the percentage it names (`hsl(210_40_50)` is `hsl(210_40%_50%)`); an alpha's 100% is 1.
- **A hue** is a number of degrees or an angle in `deg`, `grad`, `rad` or `turn`, and wraps around the circle
  (`-120` is `240`).
- **Out-of-range values are clamped as CSS clamps them when it parses**: `rgb()` channels to 0–255, alpha to
  0–1, and a negative saturation to 0, and in the space syntax a negative lightness too (`hsl(0_150_-50)` is
  black). A whiteness and blackness summing to 100% or more give the gray `whiteness / (whiteness + blackness)`.
- **A saturation above 100%** clamps to 100% in the comma syntax. In the space syntax it clamps only as
  Chromium's fast path does, which reads a *plain* `hsl()` on a colour longhand. Plain means:
  - the function is named exactly `hsl` or `hsla`, in lower case;
  - the hue is a decimal (`210`, `.5`, `-0.25`) with an optional `deg`, `grad`, `rad` or `turn`;
  - the saturation and the lightness are percentages whose decimal has a digit before any point, so `.5%` is not
    plain;
  - an alpha, if any, is a decimal with nothing between it and the closing bracket;
  - no component has an exponent, a `+` sign or `none`, and the saturation is a percentage rather than a number.

  `text-[hsl(0_150%_75%)]` is then `text-[hsl(0_100%_75%)]`. Any other spelling (`hsl(0_150_75)`,
  `hSLa(0_150%_75%)`, `hsl(0_150%_75%_/_50%)`, `hsl(0_150%_75%_/_0.5_)`) keeps the saturation above 100%, and the
  channels clip, so `hsl(0_150_75)` is `rgb(100% 37.5% 37.5%)`.
- **Which utilities clamp**: the colour longhands only: `text-[…]`, `bg-[…]`, `border-t-[…]` / `-r-` / `-b-` / `-l-`
  / `-s-` / `-e-` / `-bs-` / `-be-`, `outline-[…]`, `caret-[…]` and `selection:bg-[…]` / `selection:text-[…]`. A
  shorthand (`border-[…]`, `border-x-[…]`, `border-y-[…]`), a colour with an opacity modifier (`bg-[…]/50`),
  `ring-[…]`, `shadow-[…]`, `drop-shadow-[…]`, `divide-[…]`, a gradient stop and a custom filter's colour
  argument read through Chromium's general parser, which leaves the saturation above 100%.
- **A number** is a CSS `<number>`: `.5`, `+1e2` and `-0` read, `1.` and `1e` do not, and neither do `infinity` or
  `nan`. One past single precision's range (`1e39`, `1e999`) reads as the largest value of its sign, about
  `3.4e38`, as Chromium reads it, so `rgb(1e999_0_0)` is red and `rgb(-1e999_0_0)` black. A hue is then the hue of
  plus or minus that value: 0deg in degrees or turns, 216deg in gradians and 240deg in radians, and 144deg and
  120deg for the negative gradians and radians.
- **A math function** ([styling-arbitrary-lengths.md](styling-arbitrary-lengths.md#math-functions)) stands for any
  component that comes to a number or a percentage, and for a hue that comes to an angle or a number:
  `rgb(calc(255/2)_0_0)`, `hsl(calc(60deg*2)_100%_50%)`, `rgb(255_0_0_/_calc(50%))`. It is clamped as a written
  component is, and a NaN reads as 0. One coming to a length is no component (`rgb(calc(1px)_0_0)` is inert).
- **Function names, `none` and angle units** are read in any case.
- **The opacity modifier** (`bg-[…]/50`, `bg-red-500/50`, `text-…/[0.3]`, `text-…/[30%]`) multiplies the colour's alpha, as
  Tailwind v4's `color-mix(in oklab, <colour> 50%, transparent)` does: `bg-[#ff000080]/50` has an alpha of a
  quarter. The modifier is written the forms Tailwind generates: a number in steps of 0.25 as a number prints it (`/50`,
  `/2.25`, not `/05`, `/.5` or `/50.0`), a bracketed number, which is a fraction (`/[0.33]`), or a bracketed percentage
  (`/[33.3%]`). One outside 0–100% on a colour Tailwind writes through a variable (a palette name, a bracketed `var()`) leaves the
  `color-mix()` invalid once computed, so the property takes its unset value: `bg-red-500/150` is transparent,
  `text-red-500/150` the inherited text colour and `border-red-500/150` `currentcolor` (`outline-red-500/150` is the text
  colour, and a ring, `shadow-*` or inset colour discards the element's box-shadow). On any other colour
  (`bg-[#f00]/150`, `bg-transparent/150`) Tailwind writes no variable and the browser drops the declaration, so an
  earlier class stands; on `current` (`bg-current/150`) the property is plain `currentcolor`.
- **`current` and `inherit`** (`text-current`, `bg-inherit`, `border-current`, `ring-current`, `ring-inherit`,
  `outline-inherit`, …) read their value off a live source: `current` is the element's own text colour (the inherited one
  on `text-current` itself), `current/50` scales its alpha, and `inherit` is the parent's value of the same property, which
  an `inherit` there passes on. Both follow their source as it changes, through a transition included: an element reading one through `text-`, `bg-` or `border-`, and its parent, carry the `velvet-color-reference` class while it does, whose rule in the bundled stylesheet is what makes UI Toolkit report their restyles. `inherit` takes no modifier, which `color-mix()`
  cannot hold.

## Where Velvet differs from CSS

- **Named colours** are the ones `ColorUtility.TryParseHtmlString` reads, not the full CSS list.
- **The palette** (`bg-red-500`, a palette name in a gradient stop) is Tailwind v4's in sRGB: each OKLCH colour
  is converted as CSS Color 4 converts it and a channel outside 0–1 is clipped, so `bg-blue-500` is `#2b7fff`.
- **A colour outside sRGB**, which an `hsl()` or `hwb()` whose components lie outside their ranges can
  describe, has each channel clipped to 0–1 where CSS Color 4 maps it into the gamut.
- **`currentColor` and relative colour syntax** are not read. A `var()` is replaced by the custom
  property it reads before the colour is read ([styling-custom-properties.md](styling-custom-properties.md)).
