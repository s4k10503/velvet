### Changed

- A `Spring` `StyleTransitionConfig` that sets none of `Stiffness`, `Damping` and `Mass` takes its physics
  from its `DurationSec`, as Framer Motion's spring `duration` describes one, and a variant play on it lands
  on its target once that duration has passed. A `DurationSec` of exactly 0 lands such a play at once. A
  spring's `DurationSec` used to be ignored, so such a spring ran on the default 100 / 10 / 1 physics. This
  includes `V.Motion(transition: spring, duration: …)`, a `layoutId` move on such a spring, which takes the
  same physics, and a sequence `To` step on one, which holds until the first 50 ms sample at or past the
  duration.
