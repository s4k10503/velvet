### Fixed

- Several `V.Motion`s may hold one `layoutId` at once, as Framer Motion's shared layout lets them. The one
  that took the id last leads and the others are hidden; a render that moves one of them no longer takes the
  id over and tweens it from the lead's box. When the lead leaves, the holder of those left that took the id
  last tweens from the box the lead left, whether or not its own layout changed; a holder inside a
  `V.AnimatePresence` child hands the lead to the latest holder before it that is not exiting as its exit
  starts, and is removed once that holder has landed. A Motion whose `layoutId` becomes null stops holding the
  id. Before, whichever of them a render patched last took the id, and a lead that left handed it to nobody
  still mounted.

- A `layoutId` Motion mounted inside one that is growing or shrinking is corrected for that change of size on
  the frame it is first laid out. It was drawn stretched with the outer one for that frame.
