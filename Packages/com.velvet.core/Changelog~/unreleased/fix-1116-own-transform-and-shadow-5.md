### Fixed

- A `V.Motion` moving under a `layoutId` composes its move with its own translate and scale as they are now,
  rather than as they were when the move began. A change of either during the move, by a class or an inline value,
  runs on the transition the element declares for it, as UI Toolkit would run it. One already running as the move
  starts runs on from where it had got rather than jumping to its end, and one still running as the move lands runs
  on to its end rather than starting again. A translate given as a percent of the element's size follows a change
  of that size during the move at once rather than transitioning. A holder that takes the lead in the render that
  changes its classes moves at its new translate and scale from its second frame on.
