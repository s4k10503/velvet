### Changed

- A `V.Div`, `V.Custom<T>`, `V.ScrollView` or `V.Button` shorthand call passing a literal `null` as its
  second argument with children after it, as in `V.Div("p-4", null, child)`, no longer compiles: it matches
  the new `(className, events, children)` form as well as `(className, children)`. Cast the `null` to
  `VNode` to keep the old meaning.
