### Added

- `V.Slider` declares `direction:` and `inverted:`, UI Toolkit's `Slider.direction` and `Slider.inverted`,
  so a vertical or inverted slider can change with state instead of being set by hand from `refCallback:`.
  Each is undeclared when null, on the rule `V.TextField`'s `placeholder:` follows: a member no render has
  declared is left where a `refCallback:` put it, and one a later render drops goes back to what the slider
  carried before any render declared it. A `direction:` naming no member of `SliderDirection` throws an
  `ArgumentOutOfRangeException` before the call rents anything. A render that changes only these two leaves
  the range alone, so a range a `refCallback:` set survives it. The same two arrive on
  `Velvet.Experimental.VSlider` as `Direction` and `Inverted`.
