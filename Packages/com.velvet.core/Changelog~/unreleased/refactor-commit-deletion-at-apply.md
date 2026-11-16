### Changed

- Where a render stops rendering a component, the component's cleanups, and those of the refs inside what it removes,
  now run once the render has finished walking, as React's do in the commit, and no later than the next flush of the
  pass's recorded writes. A component rendered after it in the same pass renders ahead of those cleanups, which an
  effect or state the cleanups write is what can tell. A cleanup that throws reaches its error boundary in a follow-up
  commit, without stopping the render under way. A container that holds no component still removes its rows, and
  detaches their refs, in the walk, as does a removal that holds a component mounting its own wrapper element, or a
  Portal whose target is the container itself or lies inside what the removal removes. A removal that holds none of
  those releases its Portals once the render has finished walking, and moves the ranges behind them up where it is
  decided.
