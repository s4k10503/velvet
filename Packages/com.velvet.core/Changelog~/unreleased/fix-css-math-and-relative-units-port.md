### Fixed

- The bracket grammar for lengths, angles, numbers and times reads every math function of CSS Values 4, not
  only `calc()`, `min()`, `max()` and `clamp()`: `round()`, `mod()`, `rem()`, `abs()`, `sign()`, `hypot()`, the
  trigonometric functions, `pow()`, `sqrt()`, `log()`, `exp()` and the constants `e`, `pi`, `infinity` and
  `NaN`, with CSS's typed arithmetic (`calc(100px/2px*1px)` is 50px). A math function is read in an angle
  (`rotate-[calc(45deg*2)]`, a gradient's angle), a number (`scale-[…]`, `opacity-[…]`) and a time
  (`duration-[…]`) as well as in a length, and inside a `clip-path-[…]` or a radial or conic gradient's line it
  may be written with spaces (`clip-path-[inset(calc(1rem_+_4px))]`). A division by zero and a length past a
  float's range are read as CSS reads them, where they left the class inert: an infinity is the largest float
  of its sign and a NaN is 0.
- `ex`, `ch`, `cap`, `ic`, `lh` and the root-relative `rex`, `rch`, `rcap`, `ric` and `rlh` are measured from
  the font the element's text is drawn in and the line height Velvet gives it, in the sizing, position,
  spacing, `text-[…]` and `tracking-[…]` utilities that measure an `em` on the element, where they left the
  class inert. `outline-[2em]` draws a 2em outline, where it fell back to 3px.
- A literal past a float's range is the largest float of its sign, as Chromium reads it, where it left the class
  inert: `w-[1e39px]`, `blur-[1e39px]`, `rotate-[1e39deg]`, `duration-[1e39s]`, `scale-[1e39]`.
- `scale-[…]`, `opacity-[…]` and the filter amounts take a percentage, written or from a math function, as CSS
  does: `scale-[150%]` and `scale-[calc(150%)]` are 1.5, where `scale-[150%]` and `opacity-[50%]` left the class
  inert.
- A colour function reads a math function in a channel, a hue or the alpha, as CSS does:
  `bg-[rgb(calc(255/2)_0_0)]`, `text-[hsl(calc(60deg*2)_100%_50%)]`, where it left the class inert.
Packages/com.velvet.core/Changelog~/unreleased/fix-css-math-and-relative-units-port.md
- A radial or conic gradient's `at_` position reads CSS's whole `<position>`: lengths (`at_20px_30px`), edge
  offsets (`at_right_10px_bottom_20px`) and a math function adding a percentage and a length, where only keywords
  and percentages were read.
- An outline's width and offset are snapped to the panel's device pixels as the browser snaps a border width
  (`outline-[2.75px]` is 2.5px at two device pixels a point), where they were drawn as written.
