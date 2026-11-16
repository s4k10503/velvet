### Fixed

- A root mounted, disposed or flushed from inside another root's render or commit (a `V.Mount` from an `onCreated`
  callback, a layout effect or a ref callback, a `Dispose` from a layout cleanup, a discrete event's handler fired by
  an apply or by a layout effect) ran inside it. It now waits for the outermost render or commit to end. The
  committing root's own sync-lane passive effects run first; then, in the order they were asked for, each held root's
  passive effects and flush, the first render of each mounted root and each unmount. `V.Mount` returns its handle at
  once. Work that throws does not stop the work after it, and its exception is rethrown once all of it has run.
- A `refCallback:` cleanup that throws as its element is removed was logged and reached no error boundary. It now goes
  to the nearest boundary above the component that rendered the element, which shows its fallback in a follow-up
  commit, as React's does; with none, the tree unmounts, as it does for any error no boundary catches.
