### Fixed

- A label with a weight, italic or width class now follows a change to the font above it in the update that makes it,
  where it reached the label one update later when an element with no font of its own sat in between, as a plain
  container or a scroll view's content container does, or when the ancestor's font came to be one no registered family
  holds, whoever wrote the change. An element in between that transitions its font is followed as it shows it.
