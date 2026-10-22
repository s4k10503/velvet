### Fixed

- A `V.Motion` that takes a `layoutId` from another holder mixes its rotate from that holder's to its own over
  the move, as Framer Motion's shared layout does, and the holders drawn behind it take the lead's. Its own
  rotate still running on a transition as the move lands runs on to its end, through any move that starts
  meanwhile, and a running `animate-spin` on it turns on top of the rotate the move mixes.
