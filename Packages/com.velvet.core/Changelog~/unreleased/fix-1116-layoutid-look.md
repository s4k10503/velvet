### Fixed

- A `V.Motion` that takes a `layoutId` from another holder mixes its rotate and border radius from that
  holder's to its own over the move, as Framer Motion's shared layout does, and the holders drawn behind it
  take the lead's. A layoutId Motion drawn scaled has its border radius divided by that scale, so its corners
  keep their radius on screen.
