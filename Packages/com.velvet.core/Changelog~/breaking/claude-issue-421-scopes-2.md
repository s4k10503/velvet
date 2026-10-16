### Changed

- A component's automatic memoization now also caches, below the component's own gate, each child of a children
  list on the values it reads, as React Compiler's reactive scopes do, where it cached the component's tree as a
  whole and nothing below it. A child is a call of a `V` factory or a constructor whose arguments are parameters
  and hook values. A render that changes one input builds again only the children reading it and hands the
  reconciler the same `VNode` instances for the rest, which it skips. A constructor a child's expression calls no
  longer runs on a render where none of the values the child reads changed.
  A scope compares the references it reads, so a list, dictionary or object held in state, context or props and
  changed in place counts as unchanged and the child keeps what it built before: replace the value (a new list, a
  new object) as in React, or opt the component out with `[Component(Compiler = false)]`.
  A child reading anything else (a `MutableRef`, a `static` field, a call to a method of your own) is built on
  every render the component's own cache misses, as it was.

### Added

- `Hooks.TryGetMemoizedScope` and `Hooks.StoreMemoizedScope`, the pair the compiler transform weaves around a
  cached `VNode`. They are not for calling by hand.
