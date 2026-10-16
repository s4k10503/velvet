### Changed

- A component whose handler captures a hook value is now woven by the compiler transform. The compiler hoists a
  variable a lambda captures into a field of a display class, so the hook call stored its result into a field,
  which the weaver read only as a local and answered by leaving the component unwoven. The gate is now keyed on
  the captured value, and a `UseState` setter captured beside it is no dependency. Such a component caches as any
  other does: a render whose props and hook values all compare equal returns the cached tree, so a list,
  dictionary or object held in state and changed in place counts as unchanged. Replace the value (a new list, a
  new object) as in React, or opt the component out with `[Component(Compiler = false)]`.
