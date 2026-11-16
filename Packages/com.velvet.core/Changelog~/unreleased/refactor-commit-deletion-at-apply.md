### Changed

- Where a render stops rendering a component, the component's cleanups, and those of the refs inside what it removes,
  now run where the pass applies its recorded writes, as React's do in the commit, rather than where the render
  reaches them. A component rendered after it in the same pass can now render ahead of those cleanups, which an
  effect or state the cleanups write is what can tell. A cleanup that throws reaches its error boundary in a follow-up
  commit, without stopping the render under way. A container whose children are all host elements, with no `null`, fragment, provider, Suspense, memo, presence or
  component among them, removes its rows, and detaches their refs, in the walk, as does a removal that holds a
  component mounting its own wrapper element, or a Portal whose target is the container itself or lies inside what the
  removal removes. The Portals a deferred removal holds move the ranges behind them up where the removal is decided,
  and release their rows, bridge and world-space host where the recorded writes apply.
  Where those cleanups wait for the apply, each runs at the turn of the nearest component, Provider, Suspense
  or Fragment above it that the pass kept: behind the cleanups of what comes before it in the tree and
  ahead of those inside what it still renders. A removal that does not wait is taken down in tree order.
