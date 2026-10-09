### Added

- `AnimationSequenceControls.Speed`, Framer Motion's `speed`: the rate a `Hooks.UseAnimationSequence` timeline
  and the `Spring` and `Bezier` plays its steps started advance at, delays included. 0 holds both; setting it
  re-times running plays from the next frame. A negative, NaN or infinite speed throws.
- `AnimationSequenceControls.Cancel`, the Web Animations API's `cancel()`: the cursor stops, and each `Spring`
  or `Bezier` play the steps started comes off its element without its completion running. `Play` then starts
  the sequence again from step 0. The motion guide's Timelines section owns both.
