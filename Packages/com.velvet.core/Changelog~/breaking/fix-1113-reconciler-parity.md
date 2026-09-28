### Changed

- An unkeyed element is matched by the index it is written at among its siblings, as in React, rather than
  by how many elements were emitted before it. A sibling turning to `null` no longer shifts the elements
  after it onto their predecessors' elements: in `cond ? V.Div(V.Component(Row)) : null` beside a second
  such `V.Div`, the surviving wrapper keeps its own element and the `Row` under it its own state, where it
  was patched onto the departing wrapper's element and took over the first `Row`'s state. A `V.Fragment` or
  a component written earlier among the siblings rendering more or fewer elements no longer remounts the
  elements after it. An unkeyed element moved into or out of an unkeyed `V.Fragment` or `V.Provider`
  written among its siblings is remounted, as React remounts a child whose parent fiber changes, where it
  used to be patched in place when its flat position matched. An unkeyed `V.Fragment` that is an element's,
  a `V.Motion`'s, a `V.Portal`'s or a `V.WorldSpace`'s only child is reconciled as its children, as React
  unwraps an unkeyed top-level Fragment: a `V.TextField` written alone, then first in such a Fragment beside
  a sibling, keeps its element and its text.
