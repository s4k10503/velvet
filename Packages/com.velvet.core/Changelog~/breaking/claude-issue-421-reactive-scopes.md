### Changed

- A component's automatic memoization caches, below the component's own gate, each child `VNode` expression, each
  lambda that captures variables and each value the body derives and stores once, on the values it reads, as React
  Compiler's reactive scopes do, where it cached the component's tree as a whole and nothing below it. A render
  that changes one input builds again only what reads it and hands the reconciler the same `VNode` instances for
  the rest, which it skips. A constructor, a property accessor or a `System` member that a scope's expression
  calls no longer runs on a render where none of the values the scope reads changed.
  A scope compares the references it reads, so a list, dictionary or object held in state, context or props and
  changed in place counts as unchanged and the scope keeps what it built before: replace the value (a new
  list, a new object) as in React, or opt the component out with `[Component(Compiler = false)]`.
  A scope reading something no dependency stands for (a `MutableRef`, a `static` field that is not `readonly`, a
  method of your own, a clock or random source named in `Documentation~/memoization.md`, an instance property of
  a `System.Diagnostics` or `System.Threading` type such as `Stopwatch.Elapsed`) is built on every render the
  component's own cache misses, as it was. The scopes cached and the ones not are listed there.

### Added

- `Hooks.TryGetMemoizedScope` and `Hooks.StoreMemoizedScope` for a cached `VNode`, and `Hooks.TryGetMemoizedValue`
  and `Hooks.StoreMemoizedValue` for any other cached value, the pairs the compiler transform weaves. They are not
  for calling by hand.
