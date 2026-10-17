### Changed

- On a field control a surface utility a play's class shows lands on the input box, as the className's does,
  and on a clipped element a `clip-path-*` class a play shows re-shapes the clip.

### Fixed

- A direct child of a skewed element showed either the skew's seat or its own translate — `translate-x-*`, a
  `V.Motion` translation, a drag offset — whichever was written last. The two compose, as CSS composes a
  parent's transform with the child's, and a translate play on such a child starts from its own translate
  rather than offset by the seat.
