### Fixed

- `Hooks.UseLayoutEffect` reads the committed tree laid out, as a layout read inside React's `useLayoutEffect`
  forces the reflow its commit owes: `layout`, `resolvedStyle` and `worldBound` reflect the commit the effect
  runs in, where on mount they used to read an element not yet laid out and on an update the previous frame's
  box. A commit that
  runs layout effects, or builds a `Hooks.UseImperativeHandle` handle, now lays out the panel it is mounted on
  before running them, so a measurement stored in state re-renders before the frame is painted. The rows a
  `V.VirtualList` renders from inside the panel's layout pass are the exception the React migration guide
  names.
