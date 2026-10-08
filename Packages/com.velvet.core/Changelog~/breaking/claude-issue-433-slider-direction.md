### Changed

- Home on a `V.Slider` sets its `lowValue` and End its `highValue` whatever the slider's direction and
  inverted flag, as WAI-ARIA's slider pattern and Radix's `Slider` do. A slider that was vertical or
  inverted, set by hand from `refCallback:` or `onCreated:`, used to take Home to its high value and End to
  its low one.
