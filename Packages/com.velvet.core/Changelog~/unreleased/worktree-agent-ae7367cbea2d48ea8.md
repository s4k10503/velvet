### Fixed

- A node a component keeps across renders inside a composite value — a record in a `Hooks.UseState` or
  `Hooks.UseMemo` slot, a props record, a `Store`'s state — shows the props it was built with when a later render
  puts it back. It used to come back with them cleared once a render that left it out had replaced the tree it was
  in: a `V.Label(text: "carried")` kept that way showed an empty text. The `V.*` factories no longer pool the props
  bags, event arrays and child arrays they build, so each node keeps its own, and a component replacing its output
  no longer walks the trees its ancestors cache to find which of those parts it may hand back.
