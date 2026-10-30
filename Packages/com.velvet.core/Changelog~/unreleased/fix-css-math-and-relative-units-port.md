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
