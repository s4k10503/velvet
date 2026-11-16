### Changed

- A deleted subtree's cleanups and ref detaches now run in the order the tree had before the first cleanup ran, as
  React's deletion walk takes it from the fiber tree. A layout cleanup that unlinks a host element from its parent no
  longer keeps that element's ref from detaching.
