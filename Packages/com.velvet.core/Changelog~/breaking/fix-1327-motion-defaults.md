### Changed

- `Animator.Animate` on one target with no transition runs each value on Framer Motion's default for it rather than a
  0.3 s ease-out: `X`, `Y` and `RotateDeg` on a spring of 500 stiffness and 25 damping, `Scale` on one of 550 and 30
  (critically damped toward 0), `Opacity` on a 0.3 s `cubic-bezier(0.25, 0.1, 0.35, 1)` ease, and more than two
  keyframes over 0.8 s. A transition setting only `DelaySec` and the repeat options counts as none. A sequence segment
  with no transition still runs 0.3 s on an ease-out.
- `Animator.Animate` honours `Repeat`, `RepeatType` and `RepeatDelaySec`, which it ignored, and eases more than two
  keyframes pair by pair rather than across the whole run. A `Spring` on a single call runs from the first keyframe to
  the last whatever their number, and a sequence segment's spring of two keyframes is measured over their travel.
- `StyleTransitionConfig.DurationSec` defaults to 0.3 s rather than 0, so a `Tween` or `Bezier` naming no duration
  animates rather than landing at once, and an exit on one animates too.
- A variant play whose transition sets nothing but `DelaySec`, the repeat options and the child-orchestration knobs
  (`StaggerChildrenSec`, `StaggerDirection`, `DelayChildrenSec`, `When`) runs Framer Motion's per-value defaults after
  that delay, as does `V.Motion` given only `delay:`, rather than a zero-length tween or the `Fade` preset.
- A `scale-y-*` channel on the per-value defaults runs on the scale spring rather than on the generic 100 / 10 one.
- `AnimateControls.Finished` never settles once `Stop()` or `Cancel()` ends the animation, as Framer Motion's
  `finished` promise does not; it was cancelled.
