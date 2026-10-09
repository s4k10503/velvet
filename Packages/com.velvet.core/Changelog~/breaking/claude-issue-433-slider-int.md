### Changed

- A `V.Custom<SliderInt>` gets `V.SliderInt`'s keyboard, which is `V.Slider`'s: Home sets its `lowValue` and End
  its `highValue` whatever the direction and inverted flag, PageUp and PageDown move ten steps of 1, and an
  arrow moves one step, ten with Shift. UI Toolkit's own handler, which such a slider used before, sent Home
  to the high value on a vertical slider and on an inverted horizontal one, left the value where it was on PageUp and PageDown at
  the default `pageSize` of 0, and stepped an arrow by the power of ten closest to a hundredth of the range,
  at least 1. The element now comes from the pool `V.SliderInt` uses rather than being built for each mount,
  so a callback a `refCallback:` registers on it has to be unregistered by the cleanup that `refCallback:`
  returns, as on every pooled element.
