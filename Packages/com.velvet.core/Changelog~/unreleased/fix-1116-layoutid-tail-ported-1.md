### Fixed

- A `layoutId` crossfade that starts while the element's own opacity is running on a transition runs it on as
  UI Toolkit runs it, from where it had got, inside its delay or past it, and shortens a change back towards where
  it started as UI Toolkit shortens a reversal. It ran only what was left of the duration, without the delay.

- A lead alone under its `layoutId` whose own move interrupts the opacity mix from the previous holder holds the
  opacity the mix had reached until that move lands.
