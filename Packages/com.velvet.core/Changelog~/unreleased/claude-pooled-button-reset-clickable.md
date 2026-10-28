### Changed

- Reusing a pooled `Button` keeps its `Clickable` and clears its click handlers and pressed state in place,
  where it used to replace the manipulator and pay a callback unregister and register for each reuse.
