### Added

- `StyleTransitionConfig.Repeat`, `RepeatType` and `RepeatDelaySec` repeat a `V.Motion` play: Framer Motion's
  `repeat`, `repeatType` and `repeatDelay`, played by `TransitionType.Bezier` on enters, label changes and
  exits. `Repeat` counts the passes after the first and takes `float.PositiveInfinity` for a play that repeats
  until something replaces it. `TransitionRepeatType.Loop` starts each pass at the from-pose, `Reverse` plays
  every second pass backwards with its easing reversed (CSS's `animation-direction: alternate`), and `Mirror`
  plays it from the to-pose back on the same easing. An odd count under `Reverse` or `Mirror` ends on the
  from-pose and holds it, as Framer's does. `When = BeforeChildren` and a `Hooks.UseAnimationSequence` `To` step
  deriving its hold wait for the passes of a finite repeat and the waits between them. A `Tween` or `Spring` transition with a `Repeat` plays once and
  logs a warning.
