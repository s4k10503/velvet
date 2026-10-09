### Added

- `StyleTransitionConfig.Repeat`, `RepeatType` and `RepeatDelaySec` repeat a `V.Motion` play: Framer Motion's
  `repeat`, `repeatType` and `repeatDelay`, played by `TransitionType.Bezier` and `TransitionType.Spring` on
  enters, label changes and exits. `Repeat` counts the passes after the first and takes
  `float.PositiveInfinity` for a play that repeats until something replaces it. `TransitionRepeatType.Loop`
  starts each pass at the from-pose, `Reverse` plays every second pass backwards in time (CSS's
  `animation-direction: alternate`), and `Mirror` plays it from the to-pose back. A spring's channels each
  repeat on the time their own spring takes to rest, as Framer's values do. An odd count under `Reverse` or
  `Mirror` ends on the from-pose and holds it, as Framer's does. `When = BeforeChildren` waits for every pass
  and every wait between them, and never releases its children under an endless repeat. A
  `Hooks.UseAnimationSequence` `To` step deriving its hold waits for a repeat below 20; one of 20 or more is
  dropped from the transition the step hands out, with a warning, as Framer's sequence drops it. A `Tween`
  transition with a `Repeat` plays once, and the first such play in a mounted tree logs a warning.

### Fixed

- `When = BeforeChildren` on a `TransitionType.Spring` transition held its children for the transition's
  `DelaySec + DurationSec`, though a spring does not read `DurationSec`. They now wait as long as the spring's
  slowest channel takes to rest from the pose classes, measured as Framer Motion measures a spring's duration.
