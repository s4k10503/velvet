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
  0–1, and a negative saturation to 0. A whiteness and blackness summing to 100% or more give the gray
  `whiteness / (whiteness + blackness)`.
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
