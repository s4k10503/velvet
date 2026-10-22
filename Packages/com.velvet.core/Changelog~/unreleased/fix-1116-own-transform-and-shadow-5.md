### Fixed

- A `V.Motion` moving under a `layoutId` composes its move with the translate and scale its classes give it now,
  rather than as they were when the move began: a class change during the move is drawn from the frame after the
  styles take it, and a holder that takes the lead in the render that changes its classes moves at its new
  translate and scale rather than its old.
