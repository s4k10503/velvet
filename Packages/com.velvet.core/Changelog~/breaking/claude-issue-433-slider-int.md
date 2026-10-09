### Changed

- A `SliderInt` mounted through `V.Custom<SliderInt>` or `V.Motion(elementType: typeof(SliderInt))` gets
  `V.SliderInt`'s keyboard, which is `V.Slider`'s. Home sets its `lowValue` and End its `highValue` whatever the
  direction and inverted flag, and PageUp and PageDown move ten steps of 1. An arrow moves one step, ten with Shift,
  with no state to enter first, and keeps focus on the slider, as an `<input type="range">` does. UI Toolkit's own handler, which
  such a slider used before, worked differently:
  - Home went to the high value on a vertical slider that is not inverted and on an inverted horizontal one.
  - PageUp and PageDown left the value where it was at the default `pageSize` of 0.
  - An arrow moved the value only while the slider was in its movable state, which focusing it enters and
    Submit toggles. Out of that state the arrow was left to focus navigation and could move focus away. In it, an arrow
    stepped by the power of ten closest to a hundredth of the range, at least 1.
- Such a slider now comes from the pool `V.SliderInt` uses rather than being built for each mount. A callback
  a `refCallback:` registers on it has to be unregistered by the cleanup that `refCallback:` returns, as on
  every pooled element.
