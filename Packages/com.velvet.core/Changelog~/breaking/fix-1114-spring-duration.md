### Changed

- A `Spring` `StyleTransitionConfig` that sets none of `Stiffness`, `Damping` and `Mass` takes its physics
  from its `DurationSec`, as Framer Motion's spring `duration` describes one, and a variant play or a
  `layoutId` move on it lands on its target once that duration has passed. A spring's `DurationSec` used to
  be ignored, so such a spring ran on the default 100 / 10 / 1 physics. This includes
  `V.Motion(transition: spring, duration: …)` and a sequence `To` step on such a spring, which holds until the
  first 50 ms sample at or past the duration.

- Any `Spring`, physics knobs included, with a `DurationSec` of exactly 0 lands at once, as Framer makes a
  zero-duration transition instant: its enters and label changes land on their pose, a `V.AnimatePresence`
  removes a child whose exit is such a spring at once instead of keeping it as an exiting ghost, and a
  `layoutId` move on one lands at once. This includes `V.Motion(transition: spring, duration: 0f)`. Such a
  spring used to play on its physics. A sequence `To` step on one still plays, as in Framer's sequence: on its
  physics knobs where it sets any, and otherwise on the spring a 0.01 s duration describes.
