### Changed

- The gradient, `animate-*`, skew, shadow, border line-style, ring and outline, `backdrop-*`, `[&>*]:`, gap and space,
  grid, divide, `pointer-events-*` and stacking (`z-*`, `order-*`, `absolute`, `relative`) passes no longer each scan
  an element's class list on every patch to find whether it names their family. Which of those families a class list
  names is read from its text alone and kept in a table by the list's identity, read again where another list has
  taken its place, and each of those passes tests one bit of that reading.
