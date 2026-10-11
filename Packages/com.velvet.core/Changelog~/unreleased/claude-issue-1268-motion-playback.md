### Added

- `AnimationSequenceControls.Speed`, Framer Motion's `speed`: the rate a `Hooks.UseAnimationSequence` timeline
  and the `Spring`, `Bezier` and (on a non-panel `MotionClock`) `Tween` plays its steps started advance at, delays included. 0 holds both; setting it
  re-times running plays from the next frame. A negative, NaN or infinite speed throws.
- `AnimationSequenceControls.Cancel`, Framer Motion's `cancel()`: the cursor stops, and each `Spring`,
  `Bezier` or driven `Tween` play the steps started returns to the values it started from and stops there, without its
  completion running. `Play` then starts the sequence again from step 0. The motion guide's Timelines section
  owns both.
