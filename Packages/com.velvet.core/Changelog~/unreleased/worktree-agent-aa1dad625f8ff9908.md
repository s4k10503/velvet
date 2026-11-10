### Fixed

- A USS file holding a custom property with a token UI Toolkit's importer fails the file on is imported, the value
  stored as the text it was written with, as one holding a `rem` is: a number with an exponent (`1e3px`, `1e3`), one
  a `-`, a `_` or a non-ASCII character directly follows (`1-2`, `1_a`, `1-a`), and most delimiters, such as the `/` of
  Tailwind's `--aspect-video: 16 / 9` or a full stop no digit follows after a fraction (`.5.p`). The importer used to
  leave the whole file empty.
