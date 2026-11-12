### Changed

- Callback refs that each write a colour to their element now share one computation of the panel's layout, within a
  commit and across the rows a `V.VirtualList` renders together. A ref or a layout effect behind a write that resizes
  an element still reads the element's new size.
