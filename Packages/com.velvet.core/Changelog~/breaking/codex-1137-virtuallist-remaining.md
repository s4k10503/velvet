### Changed

- A `V.VirtualList` no longer renders the item starting exactly at the viewport's far edge, which
  react-window's `getStartStopIndices` leaves out of the visible range: a list whose viewport is a whole
  number of items long renders one row fewer, overscan aside. It used to render that item as well.
