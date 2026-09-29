### Fixed

- `animate-pulse` eases each half of its loop with Tailwind's `cubic-bezier(0.4, 0, 0.6, 1)`. It followed a
  cosine, which passed through full and half opacity at the same moments but between them strayed from
  Tailwind's curve by up to 0.006 of opacity.

- An `animate-*` loop and a per-frame writer on the same slot of one element resolve the way they do beside
  Framer Motion. The loop's frame is written over a `transition-filter` tween and over a `Spring` or `Bezier`
  `V.Motion` `rotate` channel, as a CSS animation outranks an element's ordinary declarations; a `Spring` or
  `Bezier` `opacity` channel shows over `animate-pulse` while its play drives it and hands the slot back when
  the play lets go, as Framer runs opacity on the browser's own animation engine. The slot used to show
  whichever of the two had written last.

- A `Spring` `V.Motion` channel, and a `layoutId` move, settle for any positive, finite `Stiffness`, `Damping`
  and `Mass`. Each tick now takes the spring's exact motion over that tick, the solution Framer Motion's
  spring evaluates; the step it replaced ran away once `Damping` times the tick over `Mass` reached 2 —
  `Damping` 125 at `Mass` 1 at 60 frames a second — and the play never completed.
