### Fixed

- A weight, italic or width class now keeps the `font-family` CSS gives the element, from the nearest declaration: a
  font an author's stylesheet rule or inline style gives the element itself, or an element between it and a
  `font-<name>` ancestor, decides the family instead of that ancestor's class. A font given as a font file, or by
  the legacy `-unity-font`, decides it too, and where no registered family holds it the author's font is left in
  place with the weight or italic emulated on it, rather than the default family's face being written over it. An
  author font gained or lost after the label resolved reaches the label within the next updates of its panel.
