### Changed

- A component's automatic memoization caches each child of a children list on the values the child reads, as
  React Compiler's reactive scopes do, where it cached the component's tree as a whole and nothing below it. A
  render that changes one input builds again only the children reading it and hands the reconciler the same
  `VNode` instances for the rest, which it skips. A constructor, a property accessor or a `System` member that a
  child's expression calls no longer runs on a render where none of the values the child reads changed.
  A child compares the references it reads, so a list, dictionary or object held in state, context or props and
  changed in place counts as unchanged and the child keeps the element it built before: replace the value (a new
  list, a new object) as in React, or opt the component out with `[Component(Compiler = false)]`.
  A child reading something no dependency stands for (a `MutableRef`, a `static` field that is not `readonly`, a
  method of your own, a clock or random source named in `Documentation~/memoization.md`, an instance property of
  a `System.Diagnostics` or `System.Threading` type such as `Stopwatch.Elapsed`) is built on every render the
  component's own cache misses, as it was. The children cached and the children not are listed there.

### Added

- `Hooks.TryGetMemoizedScope` and `Hooks.StoreMemoizedScope`, the pair the compiler transform weaves around a
  cached child. They are not for calling by hand.
