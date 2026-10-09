### Fixed

- `Hooks.UseLayoutEffect` reads the committed tree laid out, as a layout read inside React's `useLayoutEffect`
  forces the reflow its commit owes: `layout`, `resolvedStyle` and `worldBound` reflect the commit the effect
  runs in, where on mount they used to read an element not yet laid out and on an update the previous frame's
  box. A commit that runs a layout effect, builds a `Hooks.UseImperativeHandle` handle or attaches a
  `refCallback:` lays out the panels its tree renders into before running it, its `V.Portal(layer:)` and
  `V.WorldSpace` panels included, so a measurement stored in state re-renders before the frame is painted. A
  commit made inside the panel's own layout pass, such as the rows a `V.VirtualList` renders as its viewport is
  laid out or a tree mounted from a `GeometryChangedEvent` callback, lays the panel out again from inside that
  pass before running them. A commit's `Hooks.UseInsertionEffect`s run before its `refCallback:`s attach, so a
  callback ref reads the box a layout effect of the same commit reads.
- The `GeometryChangedEvent` callbacks of the elements such a commit lays out run inside that commit, ahead of
  its layout effects and callback refs, rather than later in the frame's layout pass.
