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
- A `StylePropertyTransition` replaces the enclosing config for its property, as Framer Motion's per-value
  transition does: a null `DurationSec` runs 0.3 s and a null `Easing` eases out rather than taking the
  enclosing config's, and on a `Spring` or `Bezier` play an entry's `DelaySec` replaces the play's rather than
  adding to it. `Animator.Animate` reads `PropertyOverrides` as per-value transitions.
- A `Spring` on a single `Animator.Animate` call ends where it comes to rest rather than at 20 s.
- A `layoutId` move on a `V.Motion` given only `delay:` runs 0.3 s on an ease-out after it rather than the
  `Fade` preset's timing.
- A `UseAnimationSequence` `To` step naming no transition plays 0.3 s on an ease-out, as a segment of Framer
  Motion's sequence does, rather than reusing an earlier step's transition or the `Fade` preset.
- `AnimateControls.Finished` never settles once `Stop()` or `Cancel()` ends the animation, as Framer Motion's
  `finished` promise does not; it was cancelled.
