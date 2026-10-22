### Fixed

- While a `layoutId` move runs, the transitions the element's classes declare go on running for every property
  but the ones the move writes, as Framer leaves the CSS transitions of what a layout animation does not animate,
  and a change of those classes mid-move is taken as it comes. The move suspended all of them.
