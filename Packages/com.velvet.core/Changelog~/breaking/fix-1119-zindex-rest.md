### Changed

- `group-*:` and `peer-*:` react to every marked ancestor and every marked preceding sibling, as Tailwind's
  `.group:hover x` and `.peer:checked ~ x` do, and hold the payload while any one of them is in the state.
  They used to bind only the nearest one, so hovering an outer `group` around an inner one lit nothing.
- `z-*` on a `V.Motion` stacks it the way it stacks a `V.Div`. It used to be ignored with a warning.
