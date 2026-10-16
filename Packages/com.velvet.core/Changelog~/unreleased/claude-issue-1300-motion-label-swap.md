### Fixed

- A `Spring` or `Bezier` label swap while the slot is moving now starts the new play from the value on screen, whether
  a running play or a native CSS transition is moving it. It used to restart from the swapped-out label's own class
  value, so the element jumped when the label flipped mid-play. A slot nothing is moving still starts from the
  swapped-out value.
