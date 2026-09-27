### Fixed

- A `V.Motion` carrying a `layoutId` tweens every move rather than every other one. Starting a tween
  dropped the id's registration, so the next move found nothing to tween from and jumped to its new
  place; the move after that tweened again.

- A `layoutId` tween that a later move supersedes mid-flight hands back the transition suspension it
  took. On an element whose own classes transition translate or scale (such as `transition-transform`,
  `transition-all`, a `duration-*` utility), it kept `transition-property: none` on the element after
  the last tween settled, so the element's own transitions stayed off.
