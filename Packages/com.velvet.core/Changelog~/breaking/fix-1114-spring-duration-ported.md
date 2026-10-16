### Changed

- A `Spring` `StyleTransitionConfig` that sets none of `Stiffness`, `Damping` and `Mass` takes its physics from
  its `DurationSec` (and `Bounce`), as Framer Motion's spring `duration` describes one, and each pass of a play
  on it ends on its target once that duration has passed, a `layoutId` move included. A channel released with
  a velocity solves its physics for that velocity, as Framer's `findSpring` does per value. A spring's
  `DurationSec` used to be ignored, so such a spring ran on the default 100 / 10 / 1 physics until it rested.
  This includes `V.Motion(transition: spring, duration: …)`, the `When = BeforeChildren` wait on such a spring,
  and a sequence `To` step on one, which holds until the first 50 ms sample at or past the duration.
- Any `Spring`, physics knobs included, with a `DurationSec` of exactly 0 and no `RepeatDelaySec` is instant,
  as Framer makes a zero-duration transition: it lands on its pose once its `DelaySec` and the stagger its play
  is given have run, at once where both are none, and repeats nothing. A `V.AnimatePresence` with no
  `staggerSec` or `delayChildrenSec` removes a child whose undelayed exit is such a spring at once instead of
  keeping it as an exiting ghost. Such a spring used to play on its physics. A sequence `To` step on one still
  plays, as in Framer's sequence: on its physics knobs where it sets any, and otherwise on the spring a 0.01 s
  duration describes.
- An enter or label change on a `Spring` or a `Bezier` that interrupts a running `Spring` or `Bezier` enter or
  label change starts each channel the two share from the value it is drawn at, as Framer Motion animates every
  value from the one it has, whichever transition either plays on; a `Tween` on a mount clock other than the
  panel's counts as a `Bezier` here. A spring also takes the velocity the interrupted channel moves at, a
  bezier's being its curve's slope. A color starts at the color drawn, and a length the two hold in different
  units keeps its own start. A label change used to start from the pose the swapped-out label names, with the
  interrupted channel's velocity where a spring interrupted a spring and at rest otherwise.
