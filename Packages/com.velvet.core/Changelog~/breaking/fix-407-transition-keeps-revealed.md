### Changed

- A `V.Suspense` showing its children no longer swaps them for its fallback when a `Hooks.UseTransition`
  update suspends them. The children stay on screen, `isPending` stays true, and the update renders when the
  resource settles. Code that relied on the fallback appearing during a transition now sees the previous
  content instead. Updates that are not a transition's, and boundaries that show no children yet, show the
  fallback as before.
