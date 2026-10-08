### Added

- `Hooks.UseAnimationSequence` has an overload taking `iterations` in place of `loop`, which plays the steps a
  fixed number of passes: the Web Animations API's `iterations` and CSS's `animation-iteration-count`, so the
  count includes the first pass. The sequence completes once the last pass's last hold elapses, a restart
  plays every pass again, and `iterations: 0` plays none, committing no step. The overload taking `loop` is
  unchanged. An alternate direction is not offered; the motion guide's Timelines section says why.
