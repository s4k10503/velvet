### Fixed

- A render of one root left the `UseEffect`s another root had pending, and `MountedTree.Dispose`, the render a resolved
  `Hooks.Use` read retries and an error boundary's fallback for a `UseFrame` callback's error ran none. Each now first
  runs the `UseEffect`s the roots left pending, whichever root they belong to, and the `AnimatePresence` exit
  completions pending beside them, as React flushes its pending passive effects before it renders or commits anything.
  A tree disposed before its mount's `UseEffect`s ran now runs them and then their cleanups, as React's
  `root.unmount()` does. An exception one root's flush throws there is logged, and the rest of that work still runs.
- A component mounted and rendered again before its `UseEffect`s ran, as when a render completes a parked transition
  pass that mounted it, lost StrictMode's second run of its mount's `UseEffect`s. It now keeps it, as React runs it for
  a component in the commit that mounts it.
