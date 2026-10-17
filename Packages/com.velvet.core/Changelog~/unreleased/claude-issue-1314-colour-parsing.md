### Fixed

- An `hsl()` saturation above 100% clamps to 100% as Chromium clamps it: in the comma syntax, and in the space
  syntax for a plain `hsl()` or `hsla()` read by a colour longhand (`text-[…]`, `bg-[…]`, the per-side
  `border-t-[…]` family, `outline-[…]`, `caret-[…]`, `selection:…`), so `text-[hsl(0_150%_75%)]` is
  `rgb(255 128 128)`, not `rgb(255 96 96)`. A shorthand (`border-[…]`), a colour with an opacity modifier, a
  ring, a shadow, a gradient stop, an upper-case function name, a `none`, an exponent, a `+` sign, a percentage
  alpha or a number in place of the saturation leaves it above 100%, as in Chromium. The guide spells out what
  plain means.
- The space syntax floors an `hsl()` lightness at 0 when it parses, as it already did the saturation, so
  `hsl(0_150_-50)` is black.
- A number past single precision's range in a colour function reads as the largest single-precision value of its
  sign, as Chromium's tokenizer reads one, rather than the largest double. A hue between `1e39` and `1e308` is now
  the hue of that value (`hsl(1e305_100%_50%)` is red), where the double-precision remainder gave an arbitrary one.
