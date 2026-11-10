### Fixed

- A component that is not memoized no longer renders again with its parent when the parent hands it back the
  node it last rendered from, as React bails an element it is given again. This is what a `[Component]` body's
  automatic memoization returns when its inputs did not change, so a child built there now keeps its output on
  its parent's render. Its own state update, a change to a context it reads, a read it is still waiting on and a
  render of its own that suspended still render it.
- Automatic memoization now caches a call that builds a component from a static method, such as
  `V.Component(Row.Render)`. It used to build every such call again on each render, and the method's
  `MemoizationReportAttribute` listed it as writing a static: the field the C# compiler keeps that method's
  delegate in.
