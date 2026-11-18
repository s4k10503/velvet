### Fixed

- Velvet's USS importer stores a custom property holding a `resource()` with only whitespace between its parentheses,
  such as `resource( )`, as text, as it stores one holding `resource()`. Such a value used to reach UI Toolkit's
  importer as written, which leaves the whole file empty on it. One holding a `resource()` with only comments there is
  stored as text the same way, each comment written as a space.
