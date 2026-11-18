### Fixed

- On an element with a timing class (`transition`, a `transition-*` utility, `ease-in`, `ease-out` or `ease-in-out`),
  the transitions UI Toolkit runs kept UI Toolkit's own curve wherever the element's rules listed more than one timing
  keyword, as a stylesheet's `transition-timing-function: ease-in, ease-out` does. Each `ease-in`, `ease-out` and
  `ease-in-out` in such a list now runs on Tailwind's curve, as it does where the list names one keyword.
