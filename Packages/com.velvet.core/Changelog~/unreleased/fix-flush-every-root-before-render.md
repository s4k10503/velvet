### Fixed

- A render of one root ran the `UseEffect`s that root left pending, or those of the root whose work ran just before it,
  but not every root's, and `MountedTree.Dispose`, the render a resolved `Hooks.Use` read retries and an error
  boundary's fallback for a `UseFrame` callback's error ran none. Each now first runs every root's pending `UseEffect`s
  and the `AnimatePresence` exit completions pending beside them, as React flushes its pending passive effects before
  it renders or commits anything. A tree disposed before its mount's `UseEffect`s ran now runs them and then their
  cleanups, as React's `root.unmount()` does.
