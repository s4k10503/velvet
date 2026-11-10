### Fixed

- A rounded layoutId `V.Motion` whose background image is set inline keeps painting that image while its move is
  drawn scaled differently on each axis, and has it back once the move lands. It used to paint the image for one
  frame of the move, show none for the rest of it, and lose the inline image for good when the move landed.
