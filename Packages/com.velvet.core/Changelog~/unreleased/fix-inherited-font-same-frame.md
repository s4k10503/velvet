### Fixed

- An element with a weight, italic or width class directly under another such element in the visual tree, neither of
  them under a `font-<name>` class, now takes the family of the face the outer one takes from a change to the font it
  inherits in the same frame, where that face shows at once. It could take it a frame later for each level of such
  nesting before.
