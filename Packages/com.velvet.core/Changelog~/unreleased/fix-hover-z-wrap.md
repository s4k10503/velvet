### Fixed

- A `hover:z-*` class, or a `z-*` class that reads a CSS custom property, on an element a `wrapElement` wraps moved
  the element inside its wrapper instead of stacking the wrapper among its siblings, so the element stayed behind
  them. The wrapper now stacks, as it already did for an element patched with the same class.
