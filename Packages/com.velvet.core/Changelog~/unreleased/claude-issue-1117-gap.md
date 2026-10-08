### Fixed

- `has-[.foo]:` no longer matches a descendant whose `.foo` was put on it only by an active variant
  payload: with `hover:foo` on a hovered child, the parent's `has-[.foo]:` payload stays off, as CSS
  `:has(.foo)` tests the classes the author wrote and not the ones a variant switched on. A `.foo` the
  descendant's `className` declares still matches, including while a variant keeps it off the live class
  list.
