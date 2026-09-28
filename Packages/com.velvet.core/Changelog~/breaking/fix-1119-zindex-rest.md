### Changed

- `group-*:` and `peer-*:` react to every marked ancestor and every marked preceding sibling, as Tailwind's
  `.group:hover x` and `.peer:checked ~ x` do, and hold the payload while any one of them is in the state.
  The set follows later renders: a source inserted, moved, removed, or gaining or losing its `group` / `peer`
  class is picked up or released. They used to bind only the nearest one, so hovering an outer `group` around
  an inner one lit nothing.
- `z-*` on a `V.Motion` stacks it the way it stacks a `V.Div`. It used to be ignored with a warning.
