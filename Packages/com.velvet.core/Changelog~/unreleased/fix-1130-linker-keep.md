### Changed

- A player build hands managed code stripping a link.xml that keeps the engine members Velvet reaches by
  name: the focus state it clears from a panel an element leaves, the focus pseudo-state, the composite-root
  flag, UI Toolkit's internal property-change event, and a stylesheet's `@import`s. The file is generated during
  the build; a `link.xml` in the package would not be read.
