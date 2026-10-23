### Changed

- A variant play on a `V.Motion` whose caller named no `transition`, `duration`, `easing` or `delay` runs each value
  on the transition Framer Motion picks for its type: a spring for translate, rotate and scale (500 stiffness and 25
  damping, scale on 550 and 30), and a 0.3 s ease for opacity, colours and lengths. It used to play the `Fade`
  preset's 0.2 s tween over every value. A value neither channel set covers lands with the swap, as it does on any
  spring. A Motion naming a transition, or a pose naming its own, is unchanged, as is the preset enter and exit of a
  Motion without variants.
