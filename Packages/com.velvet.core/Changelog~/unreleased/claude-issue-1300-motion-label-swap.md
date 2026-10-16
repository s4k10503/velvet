### Fixed

- A `Spring` or `Bezier` label swap while a running `Spring` or `Bezier` play, or a `Tween` running as a native
  transition (realtime clock), is moving the slot now starts the new play from the value on screen. It used to
  restart from the swapped-out label's own class value, so the element jumped when the label flipped mid-play. A
  spring that interrupts a running `Bezier` play or a native transition now also starts with its velocity rather
  than at rest, scaled by a playback's rate. A slot nothing is moving still starts from the swapped-out value,
  except where a `controls.Cancel()` hold supplies the start.
