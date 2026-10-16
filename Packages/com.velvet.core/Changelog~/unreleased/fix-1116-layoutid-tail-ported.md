### Fixed

- A `V.Motion` that takes a `layoutId` from another holder mixes its rotate and border radius from that
  holder's to its own over the move, as Framer Motion's shared layout does, and the holders drawn behind it
  take the lead's. A layoutId Motion drawn scaled has a pixel border radius divided by that scale, by the
  geometric mean of the two axes where they differ, and a percent radius left as it is. Its own rotate or radius
  still running on a transition as the move lands runs on to its end, through any move that starts meanwhile.

- While a `layoutId` move runs, the transitions the element's classes declare go on running for every property
  but the ones the move writes, as Framer leaves the CSS transitions of what a layout animation does not animate.
  The move suspended all of them.

- A `layoutId` crossfade that starts while the element's own opacity is running on a transition runs it on as
  UI Toolkit runs it, from where it had got, inside its delay or past it, and shortens a change back towards where
  it started as UI Toolkit shortens a reversal. It ran only what was left of the duration, without the delay.

- A lead alone under its `layoutId` whose own move interrupts the opacity mix from the previous holder holds the
  opacity the mix had reached until that move lands.
