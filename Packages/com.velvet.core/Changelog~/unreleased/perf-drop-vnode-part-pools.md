### Fixed

- A node a component keeps across renders inside a composite value — a record in a `Hooks.UseState` or
  `Hooks.UseMemo` slot, a props record, a `Store`'s state — shows the props and handlers it was built with when a
  later render puts it back. Once a render that left it out had replaced the tree it was in, its props bag and its
  event array went back to a shared pool: it came back with them cleared, so a `V.Label(text: "carried")` kept that
  way showed an empty text, and where another element had been built in between, with that element's values — a
  label showed another label's text, a button ran another button's click handler. The `V.*` factories no longer pool
  the props bags, event arrays and child arrays they build, so each node keeps its own, and a component replacing its
  output no longer walks the trees its ancestors cache to find which of those parts it may hand back.

### Changed

- A `[Component]` body's computed value that can hold a node in a field or in a collection other than an array or a
  `List<T>` of nodes is now cached as any other value is. The memoization report used to decline it with "it can
  hold nodes that nothing keeps out of the pool".
