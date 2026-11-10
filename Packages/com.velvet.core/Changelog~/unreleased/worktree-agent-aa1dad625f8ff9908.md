### Fixed

- A USS file holding a custom property with a number UI Toolkit's importer fails the file on is imported, the value
  stored as the text it was written with, as one holding a `rem` is: a number with an exponent (`1e3px`, `1e3`), and
  one a `-`, a `_` or a non-ASCII character directly follows (`1-2`, `1_a`, `1-a`). The importer used to leave the
  whole file empty.
