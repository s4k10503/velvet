### Fixed

- A `V.Motion` moving under a `layoutId` composes its move with its own translate and scale as they are now,
  rather than as they were when the move began. A change of either during the move, by a class or an inline value,
  runs on the transition the element declares for it, as UI Toolkit would run it. One already running as the move
  starts runs on from where it had got rather than jumping to its end, and one still running as the move lands runs
  on to its end rather than starting again. A translate given as a percent of the element's size is drawn at that
  percent of the size it has on each frame rather than transitioning towards a new pixel value, and a change of
  either axis of the translate or the scale starts both axes over, as UI Toolkit runs each as one value. A follower
  that takes the lead back as its leader leaves starts from its own translate, scale and rotate rather than from where
  it was drawn over the leader. A holder that takes the lead in the render that changes its classes moves at its new
  translate and scale from its second frame on. A corner radius of its own already running from pixels into a
  percent, or back, as the move starts runs on from where it had got, and a rotate of 0deg after one of none starts
  over on its transition rather than running back, as UI Toolkit runs both.
