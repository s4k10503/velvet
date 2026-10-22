### Added

- `blur-[…]` and the lengths of a `drop-shadow-[…]` read `em`, the viewport units and a math function over them,
  measured on the element and measured again when its font size or the panel's size moves, as the sizing and
  spacing utilities do. A math function coming below zero gives a blur or a deviation of zero.

### Fixed

- A filter change that lands at once no longer ends a running transform-origin transition on its element. Where
  the element showed no filter, the change now dispatches a `TransitionRunEvent` for `filter`.
