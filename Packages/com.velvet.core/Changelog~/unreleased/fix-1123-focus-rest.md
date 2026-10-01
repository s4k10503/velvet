### Added

- `Hooks.UseFocusManager`, React Aria's `useFocusManager`: `FocusNext`, `FocusPrevious`, `FocusFirst` and
  `FocusLast` move focus among the elements of the focus scope around the calling component, with
  `FocusManagerOptions`' `From`, `Tabbable`, `Wrap` and `Accept`.

### Fixed

- A `contain` scope pulling focus back from a `UIDocument` panel no mounted tree manages now blurs the landing
  first, as it already did for a panel a tree manages, so that panel does not take the landing back for a frame.

- A tree disposed after the element it was mounted on left its panel leaves no focus behind in that panel: a
  pooled `Button` from it no longer takes focus back when focus returns to the panel it left.
