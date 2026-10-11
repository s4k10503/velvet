### Changed

- An element that gets a transition from its styles now transitions a style a `refCallback:` or a
  `Hooks.UseLayoutEffect` writes to it on mount, as a browser does once a layout read has computed the
  element's styles. The commit applies the element's styles before the callback runs, and UI Toolkit starts a
  transition only for an element whose styles it has computed before; the write used to land in the element's
  first computed styles, which start none. A mount that should show the written value at once puts it in the
  element's `className` or `styles:` instead.
