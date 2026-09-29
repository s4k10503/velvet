### Added

- `StyleTransitionConfig.Layout`: the transition a `layoutId` move takes in place of the Motion's own,
  Framer's `transition.layout`.

### Fixed

- A `layoutId` tween composes with the element's own `scale-*` and `translate-*` instead of replacing
  them. A resizing tween wrote its scale over the element's own for the whole tween, so a `scale-[1.5]`
  element started at the inverse alone and snapped back to its own scale at the end, and a move wrote its
  translate over the element's own `translate-*` the same way.

- A `layoutId` Motion inside another that grows or shrinks keeps its own size and its place inside the
  outer one while the outer one tweens. It was drawn scaled with the outer one — at half its size inside a
  Motion that doubled — and, when both were recreated, started from a box that ignored the outer one's scale.

- A `layoutId` Motion that moves again while its tween is running starts the new tween from where it is
  drawn. It started from its last layout box, so a fast second move visibly jumped before tweening.

- A `layoutId` handed to a new element no longer keeps the parent the old element stood in alive, which it
  did until the Motion patched again or left the tree even when that parent had been removed.

- A `layoutId` tween whose width and height change by different factors scales each axis by its own
  factor. It averaged the two into one scale, so it started off the old box on both axes.
