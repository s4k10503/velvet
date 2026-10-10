### Added

- `AnimationSequenceControls`, the handle `Hooks.UseAnimationSequence` returns, carries a read-only
  `TimeSec` beside `Play`, `Pause` and `Restart`: the Web Animations API's `currentTime` over the
  sequence's own timeline, counting each hold at its authored length. Seek and reverse are not offered; the
  motion guide's Timelines section says why.

### Fixed

- A `Call` step at index 0 of a `Hooks.UseAnimationSequence` that pauses the sequence from its callback
  keeps it paused, and one that plays it under `autoplay: false` keeps it playing. A mount or a `deps`
  restart applied `autoplay` after step 0 committed, which overrode what the callback had just set.
