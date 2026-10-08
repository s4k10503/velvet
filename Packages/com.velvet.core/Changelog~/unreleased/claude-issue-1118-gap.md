### Fixed

- A `gap-*` container reads an inline `flex-direction` and an inline `flex-wrap` ahead of its direction and
  wrap classes, as CSS ranks an inline declaration over a rule, so an inline reverse row on a
  `flex flex-row gap-x-4` container puts the gap on the trailing edge and an inline `nowrap` on a
  `flex-wrap` container keeps the half-margin path off. The classes used to win. The value is read when
  the container applies its spacing, among them its reconcile or patch, attach and a geometry change.
