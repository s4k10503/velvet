### Fixed

- A filter bracket whose value opens on a parenthesis, such as `brightness-[(2)]`, is now rejected as the
  browser rejects the declaration, so the element draws none of its filter utilities, where it used to take
  the value as a function that might come to anything.
