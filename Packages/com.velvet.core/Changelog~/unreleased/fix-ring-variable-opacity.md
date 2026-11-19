### Fixed

- An untyped bracket that is no colour and no length behind an opacity modifier (`ring-[10%]/50`, `ring-[foo]/50`,
  and the same on `ring-offset-` and `inset-ring-`) now discards the box-shadow, as the browser does. It was drawn.
  A length (`ring-[10px]/50`) is still the width's, which takes no modifier, and is left as it was.
