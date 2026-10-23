### Fixed

- A container or viewport marker added to or removed from an element's class list in code, rather than through its
  `className`, re-points the descendants already attached at that element's next render.
