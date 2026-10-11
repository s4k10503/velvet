### Added

- `V.FocusScope` takes `orientation:`, a `FocusScopeOrientation` that is also a `FocusScopeSettings` property. On
  a `singleTabStop` group, `Horizontal` ignores an up or down arrow, and `Vertical` a left or right arrow,
  before it moves focus; React Aria's `useToolbar` takes an `orientation` too. Of nested groups, the outermost
  one's value decides. The default, `Both`, is the behaviour before.
