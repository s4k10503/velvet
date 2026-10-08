### Changed

- A weight or italic class with no `font-<name>` beside it keeps the family it inherits, as CSS's
  `font-weight` keeps an inherited `font-family`: `font-bold` inside a `font-serif` container renders
  `serif`'s `Bold` entry. It used to resolve against `VelvetFonts.DefaultFamily`, so the bold text
  switched to the default family whenever that named another one. The family is the one named by the
  nearest ancestor with a `font-<name>` class that Velvet rendered; `DefaultFamily` applies when there is
  none. A root carrying the class outside Velvet (a portal target, layer root, world-space panel root or
  another `V.Mount`) is not seen, so register its family as the default as well.
