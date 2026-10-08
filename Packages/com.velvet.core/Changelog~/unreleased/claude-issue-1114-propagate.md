### Added

- `V.AnimatePresence` takes `propagate: true`, after Framer's `propagate`. Set on an inner presence, it exits all of
  the inner presence's children through its own exit path while the enclosing presence's keyed child that holds it
  is leaving, and the enclosing child stays mounted until those exits have completed. The inner presence's
  `onExitComplete` runs once, when its own exits finish. A child the inner presence was already exiting is waited
  for rather than exited again, a returning enclosing key brings the inner presence's present children back, and
  a presence between the two that does not propagate stops it. A key added to the inner presence while the
  enclosing child is leaving is not mounted.
