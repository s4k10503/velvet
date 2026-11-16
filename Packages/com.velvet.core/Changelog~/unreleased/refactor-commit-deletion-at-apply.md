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
  The cleanups of what a component stops rendering run at that component's turn among the writes, ahead of those of
  the components and elements after it, as React runs a fiber's deletions ahead of its children's, and what the
  container itself stops rendering runs ahead of what the components in it do.
