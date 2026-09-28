### Changed

- A `Hooks.UseAnimationSequence` `To` step with a `Spring` transition and no `holdSec` holds for the
  transition's `DelaySec` plus the time its spring takes to settle, simulated from `Stiffness` / `Damping` /
  `Mass` across 100px (or opacity's whole 0→1) and capped at 20 seconds, as Framer Motion's sequence times a
  spring whose distance it cannot read. It used to log a warning and hold for a fixed 0.5 seconds whatever
  the spring, so a sequence whose spring steps relied on that now advances when the spring settles instead.
