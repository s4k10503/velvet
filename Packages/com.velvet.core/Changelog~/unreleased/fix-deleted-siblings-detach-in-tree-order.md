### Fixed

- When an update deleted several of the children an element, a component or a fragment declares, they left the panel
  from the last to the first, so their `DetachFromPanelEvent` callbacks ran in reverse tree order, and the rows of a
  `V.Portal` inside a deleted element could leave the Portal's target the same way. Those deletions, and the rows of
  one Portal, now leave first to last, in the order React commits them. Where every child of that parent is an
  element, the ref and effect cleanups of what the deleted elements hold now run in that order as well.
