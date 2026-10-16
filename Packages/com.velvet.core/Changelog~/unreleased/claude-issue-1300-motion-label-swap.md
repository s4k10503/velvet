### Fixed

- A `Spring` or `Bezier` label swap while a running play moves the slot now starts the new play from that play's
  current frame. It used to restart from the swapped-out label's own class value, so the element jumped when the
  label flipped mid-play. A slot no play is moving still starts from the swapped-out value.
