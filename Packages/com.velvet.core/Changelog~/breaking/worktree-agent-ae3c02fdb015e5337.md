### Changed

- A `[Component]` that calls through an interface, or calls a virtual method an override can replace, is now
  auto-memoized where that call follows every hook call the body makes, and one taking props with no hook at
  all is memoized on its props. Such a component was left unwoven before, so its body ran on every render
  that reached it. A render whose props and hook values compare equal now returns the tree the last miss
  built, so whatever the call reads that is neither a prop nor a hook value no longer reaches the tree on
  that render — the same as for a call to a non-virtual method. A call of this kind made ahead of a hook
  call, directly or inside a custom hook, still leaves the component unwoven, and so does a call whose
  target the build cannot resolve.
- Once a hook runs between the woven gate and the point the body returns — one reached through such a call
  — the component's body runs on every render from then on rather than reusing a tree, where a hit would
  have skipped that hook. `[Component(Compiler = false)]` remains the opt-out.
- A component compiled with optimization, as a player build compiles it, is auto-memoized where its Debug
  build is, except where the body stores a hook value only to discard it (`var r = Hooks.UseRef<T>(); _ = r;`),
  which the optimizing compiler turns into the bare discard the weaver refuses. The optimizing compiler keeps
  a hook value it reads once on the stack rather than in a local,
  which the weaver did not recognize, so `var x = Hooks.UseContext(...)` followed by a single read of `x`,
  among other shapes, left the component unwoven in a player. A hook value passed straight as an argument —
  `V.Label(text: Hooks.UseStore(store, s => s.Name))` — is recognized in either build, where it left the
  component unwoven before.

### Fixed

- Two custom hooks calling each other are each read as reaching every hook the pair reaches. One of them
  could be read as calling no hook, depending on which component the build met first, so a component
  calling it behind a condition, or calling it for a `UseMutation` the pair composes, was memoized where it
  should have been left unwoven.
