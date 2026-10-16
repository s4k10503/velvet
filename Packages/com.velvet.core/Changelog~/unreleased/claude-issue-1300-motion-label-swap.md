### Fixed

- A `Spring` or `Bezier` label swap while a play is running now starts the new play from the value the element is
  showing, as Framer Motion starts a new animation from a value's current state. It used to restart from the
  swapped-out label's own class value, so the element jumped back to that value when the label flipped mid-play.
