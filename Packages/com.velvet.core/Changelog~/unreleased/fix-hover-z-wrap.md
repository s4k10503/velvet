### Fixed

- A `hover:z-*` class on an element a `wrapElement` wraps moved the element inside its wrapper instead of stacking the
  wrapper among its siblings, so the element stayed behind them. A `z-*` class that reads a CSS custom property did
  nothing there when the property's value arrived or changed after the mount. The wrapper now stacks in both cases, as
  it already did for an element patched with the same class.
