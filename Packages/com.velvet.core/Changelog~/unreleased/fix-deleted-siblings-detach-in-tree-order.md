### Fixed

- Sibling elements an update removed from one parent left the panel from the last to the first, so their
  `DetachFromPanelEvent` callbacks ran in reverse tree order, and the rows of a `V.Portal` inside a removed element
  could leave the Portal's target from its last row to its first. Both now leave first to last, as React commits a
  parent's deletions in the order its child reconciler recorded them. Where every child of that parent is an
  element, the ref and effect cleanups of what the removed elements hold now run in that order as well.
