### Added

- A bracketed colour reads CSS Color 4's space syntax for `rgb()` / `rgba()` (`bg-[rgb(15_23_42_/_50%)]`),
  `hsl()` / `hsla()` in both syntaxes (`text-[hsl(210_40%_50%)]`), and `hwb()` (`border-[hwb(150_20%_10%)]`),
  with the `/` alpha, percentages, `none` and the `deg` / `grad` / `rad` / `turn` hue units.
  Each previously left the class inert. Every utility taking a colour in brackets reads them:
  `bg-` / `text-` / `border-` and their opacity modifier, `ring-` / `outline-`, `divide-`, `shadow-` /
  `drop-shadow-`, the gradient stops and a custom filter's colour argument. A new guide, styling-colors.md,
  lists the accepted forms.

### Fixed

- The comma syntax of `rgb()` / `rgba()` follows CSS Color 4: either name takes three arguments or four, a
  channel may be a fraction or a percentage, and a channel or alpha outside its range is clamped where the
  class was previously inert.
- A colour function written with spaces inside `shadow-[…]` (`shadow-[0_4px_8px_rgb(0,_0,_0)]`) is read as
  one colour; the spaces previously split it and left the class inert.
