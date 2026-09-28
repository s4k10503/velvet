### Fixed

- `animate-pulse` eases each half of its loop with Tailwind's `cubic-bezier(0.4, 0, 0.6, 1)`. It followed a
  cosine, which passed through full and half opacity at the same moments but between them strayed from
  Tailwind's curve by up to 0.006 of opacity.

- An `animate-*` loop keeps its slot while a `Spring` or `Bezier` `V.Motion` channel, or a `transition-filter`
  tween, drives the same property on the element — `animate-spin` under a Motion `rotate` channel,
  `animate-pulse` under an `opacity` one, `animate-hue` under a filter tween. The loop's frame is written over
  each of their writes, and over the release that ends their play, as a CSS animation outranks an element's
  ordinary declarations. The slot used to show whichever of the two had written last.
