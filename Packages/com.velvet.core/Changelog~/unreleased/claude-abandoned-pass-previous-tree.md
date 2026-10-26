### Fixed

- A render that suspends with no `V.Suspense` above it now leaves the components that rendered ahead of the
  suspend as it found them, and no longer only the component that suspended. A memoized component whose own
  element changed in that render could keep showing its old output after the resource resolved, because the
  retry compared its output with the one the abandoned render had produced instead of the one on screen. A
  state update such a component had folded in is now folded again by the render that retries, an effect
  whose deps changed in it runs when the retry commits instead of being skipped, and a render of the
  component alone before the resource resolves uses the props it last committed. A suspend in a later slice
  of a time-sliced render still finds the components of the earlier slices committed.
