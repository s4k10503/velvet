### Changed

- A component that is not memoized no longer renders again with its parent when the parent hands it back the
  element it last rendered from, as React with React Compiler bails such an element. A `[Component]` body's
  automatic memoization hands back the element it built for a part whose inputs did not change, so a child built
  there keeps its output on its parent's render: one that reads something no hook tracks, such as a static field
  or `Time.time`, shows the value it last read until an update of its own. Its own state update, a change to a
  context it reads, a read it is still waiting on and a render of its own that suspended still render it; a state
  update or a suspended render of its own does so inside an element the parent handed back too. An `Outlet`
  handed back by its parent does not render again at the same location either, so a route scope whose creation
  failed is asked for again only when the Outlet next renders.
- Automatic memoization now caches a call that builds a component from a static method, such as
  `V.Component(Row.Render)`. It used to build every such call again on each render, and the method's
  `MemoizationReportAttribute` listed it as writing a static: the field the C# compiler keeps that method's
  delegate in.
