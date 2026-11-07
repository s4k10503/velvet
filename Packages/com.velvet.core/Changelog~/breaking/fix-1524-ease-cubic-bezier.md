### Added

- `ease-[cubic-bezier(x1,y1,x2,y2)]`, `ease-<name>` for a `--ease-<name>` token a project declares, and the CSS easing
  keywords as an `ease-[…]` value run a transition along the curve, as Tailwind v4's `ease-*` utilities do. UI Toolkit's
  `transition-timing-function` names only an `EasingMode` keyword, so the element is left easing by the keyword nearest
  the curve and Velvet has the transition the engine runs ease along the curve once it is running. Duration, delay and
  the reversal of an interrupted transition stay the engine's, per property, and `transition-filter`'s tween follows the
  same curve.

### Changed

- The bundled `ease-in`, `ease-out` and `ease-in-out` classes run their transition along Tailwind v4's curves
  (`cubic-bezier(0.4, 0, 1, 1)`, `(0, 0, 0.2, 1)` and `(0.4, 0, 0.2, 1)`) in place of the UI Toolkit keywords of the
  same names that approximated them. A project's `--ease-in`, `--ease-out` or `--ease-in-out` replaces the curve.
