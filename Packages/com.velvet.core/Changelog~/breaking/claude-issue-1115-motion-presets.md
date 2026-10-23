### Changed

- A Motion `Spring` or `Bezier` variant swap now animates the font-size presets (`text-sm` → `text-2xl`), the
  tracking presets (`tracking-tight` → `tracking-wide`), `rounded-full` and the `-full` lengths (`w-full`,
  `size-full`, `left-full` and the rest of the sizing, position and `basis-` families, as 100%), which landed
  instantly before. A swap that names one of them now runs a play where it used to apply the classes at once,
  so its completion callbacks and exit waits last as long as the play does.
- A percentage translate (`translate-x-1/2`, `translate-x-full`, `translate-1/2`, `translate-x-[50%]`) is a
  channel of a Spring or Bezier play, in percent of the element's own box, where it used to apply with the
  swap. An element that rests at `-translate-x-1/2` and plays a swap on its other axis keeps that percentage
  for the play; it was written back in pixels before.
- `scale-x-*` and `scale-y-*` are scale channels of a Spring or Bezier play, each axis read as the element
  draws it (`scale-50 scale-x-75` is 0.75 by 0.5), where a swap naming one used to apply with the swap.
- Semantic colours (`bg-primary`, `text-subtle`, `border-default`), `aspect-[…]`, `aspect-square` and
  `aspect-video`, and pixel-to-percentage pairs of offsets, padding and margin are driven where they used to land
  instantly.
- A Spring or Bezier play takes only the longhands it writes out of the element's own USS transitions, where it
  suspended every transition on the element until it settled; the element's other transitions keep running, and
  a variant turning on a transition class mid-play is picked up. A Tween on a clock other than the panel's still
  lands everything its swap changes.
- A Bezier play reads `PropertyOverrides` (each entry's delay, duration and easing for the channels it names) and
  an exit can run its own curve through the new `StyleTransitionConfig.ExitBezier`; a Bezier config carrying
  overrides ignored them before. A sequence step's hold counts a Bezier override that outlasts the play.
- An important utility on an element with an `animate-*` loop (`!opacity-50 animate-pulse`) holds its slot against
  the loop, where the loop overwrote it on every tick.
- A slot only the leaving pose names, or one the entering pose leaves `-auto`, is driven toward what the element
  shows there when it already carries the pose it plays into.
