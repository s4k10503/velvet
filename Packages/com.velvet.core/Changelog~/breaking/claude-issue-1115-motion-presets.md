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
