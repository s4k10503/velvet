### Fixed

- A component that is not memoized, in the output of a memoized component that bailed because its props did not
  change, was rendered again on every re-render of the parent. That covers the component directly in the output
  (`[Component(Memoize = true)] Row(int id) => V.Component(Inner)` rendered `Inner` on every parent tick) and,
  when another component below the row had an update pending, the ones below its elements. Only a component
  with an update pending of its own renders now, as with `React.memo`.
