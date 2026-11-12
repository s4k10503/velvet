### Changed

- Callback refs of one commit that each write a colour to their element now share one computation of the panel's
  layout. A ref or a layout effect behind a write that resizes an element still reads the element's new size.
