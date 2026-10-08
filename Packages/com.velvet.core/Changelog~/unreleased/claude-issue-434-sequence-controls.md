### Added

- `AnimationSequenceControls`, the handle `Hooks.UseAnimationSequence` returns, carries `Cancel` and
  `TimeSec` beside `Play`, `Pause` and `Restart`: the Web Animations API's `cancel()` and a read-only
  `currentTime` over the sequence's own timeline. `Cancel` stops the sequence and returns its state to how
  it reads before step 0 commits; `Play` starts it again from step 0. `TimeSec` reads the seconds into the
  timeline, counting each hold at its authored length. A playback rate, seek and reverse are not offered; the
  motion guide's Timelines section says why.

### Fixed

- A `Call` step at index 0 of a `Hooks.UseAnimationSequence` that pauses the sequence from its callback
  keeps it paused, and one that plays it under `autoplay: false` keeps it playing. A mount or a `deps`
  restart applied `autoplay` after step 0 committed, which overrode what the callback had just set.
