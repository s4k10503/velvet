### Changed

- A `layoutId` move takes the curve its transition's `Type` names, as a variant swap does: a tween by
  `DurationSec` / `Easing`, a bezier by its control points, a spring by its knobs, each after
  `DelaySec`, and a zero duration such as `StyleTransitionConfig.None` lands it at once. It always
  sprang on the transition's `Stiffness` / `Damping` / `Mass`, so a Motion whose transition is a tween
  for its variants — `StyleTransitionConfig`'s default `Type` — sprang on the default knobs. Set a
  spring as the transition, or as its `Layout`, to keep the spring.

- A `layoutId` Motion with no `transition` moves on Framer's default layout transition, a 0.45 s tween
  eased by `cubic-bezier(0.4, 0, 0.1, 1)`, instead of a spring with stiffness 100, damping 10 and mass 1.
