### Added

- `V.FocusScope` takes `orientation:`, a `FocusScopeOrientation` that is also a `FocusScopeSettings` property. On
  a `singleTabStop` group, `Horizontal` keeps an up or down arrow, and `Vertical` a left or right arrow, on the
  member it started from, as a toolbar's orientation does in React Aria's `useToolbar`. Of nested groups, the
  outermost one's value decides. The default, `Both`, is the behaviour before.
