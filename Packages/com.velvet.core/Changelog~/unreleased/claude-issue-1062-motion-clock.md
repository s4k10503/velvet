### Added

- `MountOptions.MotionClock` chooses the clock a mounted tree's motion advances on: every `V.Motion` play
  whatever its `TransitionType`, its delays and stagger slots included, `layoutId` moves, `filter-*`
  transitions, the `animate-*` loops, and the delta `Hooks.UseFrame` hands its callback, which
  `Hooks.UseAnimationSequence` walks its steps on. `MotionClock.GameTime` reads `Time.timeAsDouble`, so that
  motion moves only as far as game time does; a class deriving from `MotionClock` is a clock the application
  steps itself, such as for a frame-step capture. On any clock but `MotionClock.Realtime`, a `Tween` is
  played frame by frame on its own duration, delay, `PropertyOverrides` and easing instead of by UI
  Toolkit's transition, which runs on the panel's time. `MotionClock.Realtime`, the default, keeps the
  timing a tree had before.
