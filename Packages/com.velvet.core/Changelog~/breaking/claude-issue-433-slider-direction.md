### Changed

- A `V.Slider`'s input is Radix's `Slider`. Home sets its `lowValue` and End its `highValue` whatever the
  direction and inverted flag, where a vertical or inverted slider set by hand from `refCallback:` or
  `onCreated:` used to take Home to its high value. PageUp and PageDown move ten steps and an arrow moves
  one, ten with Shift, with the arrows Radix's for each direction and flag, and an arrow moves the slider
  without Enter being pressed first. The step is the new `step:` parameter, 1 when it is null, and a value a key or a
  drag gives the slider lands on the grid of steps counted from `lowValue:`: a
  slider over 0 to 1 that used to drag continuously declares `step: 0.01` to keep doing so.
