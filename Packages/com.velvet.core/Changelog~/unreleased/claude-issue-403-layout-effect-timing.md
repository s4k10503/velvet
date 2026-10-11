### Fixed

- `Hooks.UseLayoutEffect` reads the committed tree laid out, as a layout read inside React's `useLayoutEffect`
  forces the reflow its commit owes: `layout`, `resolvedStyle` and `worldBound` reflect the commit the effect runs
  in, where on mount they used to read an element not yet laid out and on an update the previous frame's box. A
  commit that runs a layout effect, builds a `Hooks.UseImperativeHandle` handle or attaches a `refCallback:`
  computes the layout of the panels its tree renders into before running it, its `V.Portal(layer:)` and
  `V.WorldSpace` panels included, so a measurement stored in state re-renders before the frame is painted. That
  holds for a commit made inside the panel's own layout pass, such as a tree mounted from a
  `GeometryChangedEvent` callback, whose `GeometryChangedEvent`s the pass still sends afterwards; what a
  `ScrollView` settles from those events, its scrollers among them, settles then too. A commit's
  `Hooks.UseInsertionEffect`s run before its `refCallback:`s attach, so a callback ref reads the box a layout
  effect of the same commit reads.
- A `V.VirtualList` row, and the components inside it, run their layout effects once the list holds the row,
  where they used to run on a row not yet in the panel.
