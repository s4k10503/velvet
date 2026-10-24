### Changed

- `StyleTransitionConfig.Repeat`, `RepeatType` and `RepeatDelaySec` apply to a `TransitionType.Tween` play, which
  used to play once and log a warning. A repeating `Tween` is now played by the per-frame driver a `Bezier` plays
  on, eased by the curve UI Toolkit's transition would ease it by, so code that already sets them on a `Tween`
  renders every pass, and a `Hooks.UseAnimationSequence` step or a `TransitionWhen.BeforeChildren` parent holds
  for all of them. The repeat warning is no longer logged.
- A `Tween` play started on a sequence's playback follows `AnimationSequenceControls.Pause`, `Speed` and `Cancel`
  on the default `MotionClock.Realtime` as well, where it was UI Toolkit's own transition and ran on to its end.
  It is driven, as a `Tween` on any other clock already was, so it lands every property its swap changes with the
  swap rather than leaving the element's other transitions to run it.
