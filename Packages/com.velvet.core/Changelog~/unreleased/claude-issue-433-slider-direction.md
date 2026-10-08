### Added

- `V.Slider` declares `direction:`, `inverted:` and `step:`. The first two are UI Toolkit's `Slider.direction` and `Slider.inverted`,
  so a vertical or inverted slider can change with state instead of being set by hand from `refCallback:`.
  Those two are undeclared when null, on the rule `V.TextField`'s `placeholder:` follows: a member no render has
  declared is left where a `refCallback:` put it, and one a later render drops goes back to what the slider
  carried before any render declared it. A `direction:` naming no member of `SliderDirection` throws an
  `ArgumentOutOfRangeException` before the call rents anything, as does a `step:` that is not above zero and finite. A render that changes only these two leaves
  the range alone, so a range a `refCallback:` set survives it. The same three arrive on
  `Velvet.Experimental.VSlider` as `Direction`, `Inverted` and `Step`.
