### Added

- `V.SliderInt` builds a UI Toolkit `SliderInt`, the whole-number counterpart of `V.Slider`: `value:`,
  `lowValue:`, `highValue:` and `step:` are `int`, `onValueChanged:` receives an `int`, and `direction:`,
  `inverted:`, the range rule and the keyboard are `V.Slider`'s. A `direction:` naming no member of
  `SliderDirection`, or a `step:` that is not above zero, throws an `ArgumentOutOfRangeException` before the
  call rents anything. The props bag carries it as `FiberElementProps.SliderInt` (`SliderIntSettings`), and
  `Velvet.Experimental.VSliderInt` is its builder.
