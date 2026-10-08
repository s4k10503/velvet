### Added

- `MountOptions.MotionClock` chooses the clock a mounted tree's `Spring` and `Bezier` Motion plays, their
  delays and its `animate-*` loops advance on. `MotionClock.GameTime` reads `Time.timeAsDouble`, so that motion
  moves only as far as game time does; a class deriving from `MotionClock` is a clock the application steps
  itself, such as for a frame-step capture. `MotionClock.Realtime`, the default, keeps the timing a tree had
  before. A `Tween` transition runs on UI Toolkit's own transitions, which no clock reaches.
