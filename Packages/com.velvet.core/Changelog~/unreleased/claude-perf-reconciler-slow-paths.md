### Fixed

- Removing many components at once, such as clearing a list of them, takes time proportional to their number. Each
  removal used to walk the parent's chain of child components from the first to find the one before it.
- Tearing down elements no longer reads the Suspense and AnimatePresence records of every other element for each one,
  a cost that grew with the number of such records.
- A keyed list re-render that leaves every row where it was no longer scans the container again to confirm it.
