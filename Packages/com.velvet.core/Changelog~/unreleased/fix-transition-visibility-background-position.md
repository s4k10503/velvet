### Fixed

- On an element Velvet renders, a `visibility` transition with a `visible` end shows `visible` while its eased progress
  lies between its ends, as CSS shows it, so under `transition-all` alone a fade-out stays painted until it ends and a
  fade-in paints from the panel's next animation update; UI Toolkit switched it halfway through. A
  `background-position-x` or `-y` transition slides from one position to the other, its percentage and its length each
  taken across as CSS takes them, where UI Toolkit switched it halfway through too. A reversal of either is shortened
  as CSS shortens one.
