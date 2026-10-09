### Fixed

- `Hooks.UseLayoutEffect` reads the committed tree laid out, as a layout read inside React's `useLayoutEffect`
  forces the reflow its commit owes: `layout`, `resolvedStyle` and `worldBound` reflect the commit the effect
  runs in, where on mount they used to read an element not yet laid out and on an update the previous frame's
  box. A commit that runs layout effects, builds a `Hooks.UseImperativeHandle` handle or attaches a
  `refCallback:` now lays out the panel it is mounted on before running them, so a measurement stored in state
  re-renders before the frame is painted. A commit made inside the panel's own layout pass, such as the rows a
  `V.VirtualList` renders as its viewport is laid out or a tree mounted from a `GeometryChangedEvent` callback,
  runs them once that pass has laid out what it changed.
