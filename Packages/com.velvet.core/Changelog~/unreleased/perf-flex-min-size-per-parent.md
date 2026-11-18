### Changed

- A container of text labels and buttons carries one geometry callback rather than one per child, and a resize of
  the container re-derives only the children whose automatic minimum size reads a percentage of its box. A row
  child that can shrink and already holds the width of its whole text no longer carries an inline `min-width`; it is
  written again once the row squeezes the child below that width. A patch that changes neither a label's text nor its
  classes no longer re-derives its minimum.
