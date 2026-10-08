### Fixed

- A `gap-*` container reads an inline `flex-direction` ahead of its direction classes, as CSS ranks an inline
  declaration over a rule, so `refCallback: el => el.style.flexDirection = FlexDirection.RowReverse` on a
  `flex flex-row gap-x-4` container puts the gap on the trailing edge. The classes used to win.
