### Fixed

- Sibling elements an update removed from one parent left the panel from the last to the first, so their
  `DetachFromPanelEvent` callbacks ran in reverse tree order, and the rows of a `V.Portal` inside a removed element
  could leave the Portal's target from its last row to its first. Both now leave first to last, as React commits a
  parent's deletions in the order its child reconciler recorded them. Where the update kept a component, or a
  construct such as a fragment, among those siblings and removed elements inside it too, those can still leave apart
  from the parent's own: after them, or ahead of them where the first element removed is one of its own. Where every
  child of the parent is an element, the ref and effect cleanups of what the removed elements hold now run in tree
  order as well.
