### Fixed

- An `hsl()` saturation above 100% clamps to 100% as Chromium clamps it: always in the comma syntax, and in the
  space syntax where the hue, saturation, lightness and alpha are plain decimals (`hsl(0_150%_75%)` is
  `rgb(255 128 128)`, not `rgb(255 96 96)`). A `none`, an exponent, a `+` sign, a percentage alpha or a number in
  place of the saturation leaves it above 100%, as in Chromium.
- A number past single precision's range in a colour function reads as the largest single-precision value of its
  sign, as Chromium's tokenizer reads one, rather than the largest double. A hue between `1e39` and `1e308` is now
  the hue of that value (`hsl(1e305_100%_50%)` is red), where the double-precision remainder gave an arbitrary one.
