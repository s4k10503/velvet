### Fixed

- On an element with a timing class (`transition`, a `transition-*` utility, `ease-in`, `ease-out` or `ease-in-out`),
  the transitions UI Toolkit runs kept UI Toolkit's own curve wherever the element's rules listed more than one timing
  keyword, as a stylesheet's `transition-timing-function: ease-in, ease-out` does. Each `ease-in`, `ease-out` and
  `ease-in-out` in such a list now runs on Tailwind's curve, as it does where the list names one keyword.
- A change to a property a layoutId Motion's move writes (its rotate, a corner radius, its translate or scale, or the
  opacity the move fades) landed at once during a variant swap, the swap's own pose included, where the Motion's
  classes or stylesheet transition that property. It now runs on the swap's timing, as it did where they do not
  transition it, both while the move runs and while what the move carries runs on past its landing.
