### Changed

- A `filter-[name]` whose name nothing is registered under leaves the element drawing no filter, its other
  filter utilities included, as CSS ignores a filter chain whose `url()` names no filter. It was an inert
  class, and the element's other filters still drew. The same holds after `VelvetFilters.Unregister`, which
  left an element's resolved filter in place, and for a definition destroyed after registration, which the
  compose skipped.
- `VelvetFilters.Register`, a re-registration and `VelvetFilters.Unregister` resolve every element carrying the
  name again, at once. A class applied before its name was registered resolved only when some later pass
  re-applied the element's classes, and a re-registration left elements on the definition they had.
