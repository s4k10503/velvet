# Styling notes: Colours

Every utility that takes a colour in brackets reads it with one grammar: `bg-[…]`, `text-[…]` and
`border-[…]` and their opacity modifier (`bg-[…]/50`), `ring-[…]` and `outline-[…]`, `divide-[…]`,
`shadow-[…]` and `drop-shadow-[…]`, the gradient stops (`from-[…]`, `via-[…]`, `to-[…]` and the colours of
a stop list), and the colour argument of a custom filter (`filter-[name:…]`). An underscore stands for a
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
  `HSL(0_150%_75%)`, `hsl(0_150%_75%_/_50%)`, `hsl(0_150%_75%_/_0.5_)`) keeps the saturation above 100%, and the
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
- **Function names, `none` and angle units** are read in any case.
- **The opacity modifier** (`bg-[…]/50`, `bg-red-500/50`, `text-…/[0.3]`) multiplies the colour's alpha, as
  Tailwind v4's `color-mix(in oklab, <colour> 50%, transparent)` does: `bg-[#ff000080]/50` has an alpha of a
  quarter.

## Where Velvet differs from CSS

- **Named colours** are the ones `ColorUtility.TryParseHtmlString` reads, not the full CSS list.
- **The palette is Tailwind v3's**, in hex: `bg-red-500` and a palette name in a gradient stop are v3's
  `#ef4444`, not v4's `oklch(63.7% 0.237 25.331)`. Write the v4 value in brackets to get it.
- **A colour outside sRGB**, which an `hsl()` or `hwb()` whose components lie outside their ranges can
  describe, has each channel clipped to 0–1 where CSS Color 4 maps it into the gamut.
- **`calc()`, `var()`, `currentColor` and relative colour syntax** are not read.
