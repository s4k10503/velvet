### Fixed

- A `V.Motion` label change tweens a `translate-*` pose — written as inline style, since translate has no USS
  form — on the transition of the pose it swaps into. It took whatever transition the element carried before
  the change: the previous swap's while that was still on the element, so a 0.3s `Hooks.UseAnimationSequence`
  step after a near-instant one arrived in a single frame; and, once it had been released, the element's own,
  so a step following a finished one jumped unless the element's own classes gave it a transition. A pose's
  inline values now land at the swap itself, alongside its USS classes and in its stagger slot.

- Under a tween transition, a `V.Motion`'s mount enter and `V.AnimatePresence` enter show a `translate-*`
  `initial` pose and tween from it to `animate`, and the exit a removed presence child's anchor plays tweens
  to a `translate-*` `exit` pose before the child is removed. None of the three moved a translate pose: the
  enters rested at `animate` from the first frame and the exit stayed there until removal. An exit interrupted
  by its key coming back leaves the child at the values the re-added child declares; so does one that finished
  just before its key came back, except that a re-add changing the variants map can keep the previous exit
  pose's USS classes, and its inline values too where the new map no longer resolves the exit label.

- A `V.AnimatePresence` child re-added mid-exit with a different `animate` label rests at the new label's
  pose. The exit's cancel put back the previous label's USS classes after the new ones had been applied, so a
  child re-added at `opacity-50` from `opacity-100` carried both and resolved to 1.
