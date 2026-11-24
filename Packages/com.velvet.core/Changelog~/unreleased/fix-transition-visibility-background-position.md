### Fixed

- On an element Velvet renders, a `visibility` transition with a `visible` end shows `visible` while its eased
  progress lies strictly between 0 and 1, as CSS shows it, so under `transition-all` alone a fade-out stays painted
  until it ends and a fade-in paints from the first animation update later than its start; UI Toolkit switched it
  halfway through. A `background-position-x` or `-y` transition slides from one position to the other, its percentage
  and its length each taken across as CSS takes them, where UI Toolkit switched it halfway through too. A reversal of
  either is shortened as CSS shortens one.
