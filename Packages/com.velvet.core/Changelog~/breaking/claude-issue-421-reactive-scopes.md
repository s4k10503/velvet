### Changed

- A component's automatic memoization caches each child of a children list on the values the child reads, as
  React Compiler's reactive scopes do, where it cached the component's tree as a whole and nothing below it. A
  render that changes one input builds again only the children reading it and hands the reconciler the same
  `VNode` instances for the rest, which it skips. A constructor, a property accessor or a `System` member that a
  child's expression calls no longer runs on a render where none of the values the child reads changed.
  A child reading something no dependency stands for (a `MutableRef`, a `static` field that is not `readonly`, a
  method of your own, the clock) is built on every render the component's own cache misses, as it was. The
  children cached and the children not are listed in `Documentation~/memoization.md`;
  `[Component(Compiler = false)]` opts a component out.

### Added

- `Hooks.TryGetMemoizedScope` and `Hooks.StoreMemoizedScope`, the pair the compiler transform weaves around a
  cached child. They are not for calling by hand.
