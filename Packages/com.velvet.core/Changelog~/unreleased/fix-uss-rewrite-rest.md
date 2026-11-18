### Fixed

- Velvet's USS importer stores a custom property holding a string beside a value UI Toolkit's importer fails on, such as
  `"a" 1rem`, as text, as it stores one without a string. Such a value used to reach UI Toolkit's importer as written,
  and fail the file; a plain string such as `"Inter"` is stored as it was.
- Velvet's USS importer clamps the channels of an `rgb()` or `rgba()` in a custom property to 0 to 255 and its alpha to
  0 to 1 as CSS does, so `rgb(300, 0, 0)` is stored as `rgb(255, 0, 0)`.
- Velvet's USS importer stores the path of a `resource()` in a custom property as the author wrote it, comments left
  out, where it held a range (`resource(u+1)`) or a comment. The importer stored another path for each.
