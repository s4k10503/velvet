### Fixed

- A `V.Motion` that takes a `layoutId` from another holder mixes its border radius from that holder's to its own
  over the move, as Framer Motion's shared layout does, without taking it below zero or mixing a pixel radius
  with a percent one, and the holders drawn behind it take the lead's. A layoutId Motion drawn scaled has a
  pixel border radius divided by that scale, by the geometric mean of the two axes where they differ, and a
  percent radius left as it is. Its own radius still running on a transition as the move lands runs on to its
  end, through any move that starts meanwhile.
