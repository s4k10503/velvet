### Changed

- Patching an element reads which class-driven passes its class list can feed once per list, where before each pass
  scanned the list for its own family. The gradient, `animate-*`, `transition-filter`, timing-keyword, skew, shadow,
  border line-style and border-width, ring and outline, `backdrop-*`, `[&>*]:`, gap and space, grid, divide,
  `pointer-events-*` and stacking (`z-*`, `order-*`, `absolute`, `relative`) passes now test one bit of that reading,
  so a list naming none of a family passes over it without scanning the list again.
