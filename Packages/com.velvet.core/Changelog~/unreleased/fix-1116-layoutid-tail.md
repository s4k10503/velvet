### Fixed

- A `layoutId` Motion's own rotate or border radius still running on a transition as its move lands runs on to
  its end as UI Toolkit runs it, through any move that starts on the Motion meanwhile. It was handed back as the
  move landed instead.
