### Fixed

- Velvet's USS importer stores a custom property holding a `resource()` with only whitespace or comments between its
  parentheses, such as `resource( )`, as text, as it stores one holding `resource()`. Such a value used to reach UI
  Toolkit's importer as written.
