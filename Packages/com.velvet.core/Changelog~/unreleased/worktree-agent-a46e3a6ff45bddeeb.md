### Changed

- The gradient, `animate-*`, `transition-filter`, timing-keyword, skew, shadow, border line-style and border-width,
  ring and outline, `backdrop-*`, `[&>*]:`, gap and space, grid, divide, `pointer-events-*` and stacking (`z-*`,
  `order-*`, `absolute`, `relative`) passes no longer each scan an element's class list on every patch to find whether
  it names their family. The families a class list names are read from it when first needed and kept with the list,
  and each of those passes tests one bit of that reading.
