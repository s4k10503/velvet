### Changed

- A `Hooks.UseAnimationSequence` `To` step with a `Spring` transition and no `holdSec` holds for the
  transition's `DelaySec` plus the duration Framer Motion's sequence gives the same spring: a travel of 100,
  sampled every 50ms until it is within 0.5 of its target and moving at no more than 2 per second, and at most
  20 seconds — 1.05 seconds for the default `Stiffness` 100 / `Damping` 10 / `Mass` 1. It used to log a
  warning and hold for a fixed 0.5 seconds whatever the spring, so a sequence whose spring steps relied on
  that now advances when Framer's would.
