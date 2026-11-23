### Changed

- A `[Component(Memoize = true)]` props type now gets a comparer written by the source generator, so the props-bail on
  an IL2CPP player reads each member by typed access instead of by `FieldInfo` and `PropertyInfo`, and a Mono runtime
  no longer compiles an expression for it. Which members are compared, and how, is unchanged. A props type the
  generator cannot compare member by member the same way — one holding a struct declared in another assembly, say —
  is compared as before. The memoization guide says what the generator compares.
