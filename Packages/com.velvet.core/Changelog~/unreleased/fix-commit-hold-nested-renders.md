### Fixed

- A root that `V.Mount` mounted, or `MountedTree.Dispose` unmounted, while another root rendered, committed or ran its
  `UseEffect`s (from an `onCreated` callback, a layout effect or its cleanup, a `refCallback:`, a `UseEffect` or an
  `OnUncaughtError` handler) rendered or unmounted inside that work. It now does so once that work has ended, before
  control returns to whatever started it, as React's `root.render()` and `root.unmount()` only schedule there.
  `V.Mount` still returns its handle at once.
- A discrete event's handler fired while a root renders or commits (a focus change a render's writes make, a click a
  layout effect, a `refCallback:` or a cleanup makes) rendered its update inside that work, whichever root the handler
  belongs to. It now renders once the work has ended, before control returns, as React's does.
- A root that waited for another's work renders now after the `UseEffect`s still pending from the root whose work ran
  just before it, whatever its commit's lane, as React flushes pending passive effects before it renders sync work.
- A `Hooks.UseStore` or `UseSyncExternalStore` re-render that a store change made outside the reading root's own
  update drain asked for waited for the main thread's next posted flush, and a state update a layout effect or
  `refCallback:` made in the commit of a `V.VirtualList` row a scroll or a resize rendered, or of a fallback a boundary
  showed for a `UseFrame` callback's error, waited for a later frame, as did the fallback of a boundary that caught an
  error one of them threw. Each now renders where the next render, commit or `UseEffect` flush of any root ends, before
  control returns.
- A `refCallback:` cleanup that throws as its element is removed was logged and reached no error boundary. It now goes
  to the nearest boundary above the component that rendered the element, which shows its fallback in a follow-up
  commit, as React's does; with none, the root renders nothing and reports it to `OnUncaughtError`, as it does for a
  ref setup that throws.

### Changed

- A `V.VirtualList` range a scroll or a resize asks for while a root renders, commits or runs its `UseEffect`s (a
  `ScrollToItem` from an `onCreated` callback or a layout effect, the list's own root's included) renders once that work
  has ended, from where the scroller is then. A layout effect that reads the rows right after calling `ScrollToItem`
  reads them as they were. The range a render of the list's host asks for still renders inside that render.
- Work that waited for another root's render or commit and throws has its exception logged, rather than thrown out of
  the call that ended that work; the waiting work after it still runs, and `V.Mount` still returns its handle. Waiting
  work that keeps asking for itself again is dropped once it has run 50 times before control returns, with "Maximum
  update depth exceeded" logged; the rest of the waiting work, its own root's included, still runs.
