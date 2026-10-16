### Fixed

- A `Spring` or `Bezier` label swap while a running `Spring` or `Bezier` play, or a native CSS transition (the default
  `Tween`), is moving the slot now starts the new play from the value on screen. It used to restart from the
  swapped-out label's own class value, so the element jumped when the label flipped mid-play. A spring that
  interrupts a running `Bezier` play now also starts with that play's velocity rather than at rest. A slot nothing
  is moving still starts from the swapped-out value, except where a `controls.Cancel()` hold supplies the start; a
  spring that interrupts a native transition still starts at rest.
