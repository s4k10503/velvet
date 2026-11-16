### Fixed

- A Portal that unmounts or moves to another element now takes its rows out of the element it rendered into first to
  last, so on a panel each row's `DetachFromPanelEvent` arrives in row order, as React removes a deleted portal's
  children. The rows left last to first before.
