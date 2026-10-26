### Fixed

- A parent re-render no longer patches the element tree of a memoized component whose props did not change. It
  used to reconcile every descendant of each such row against itself, so updating one row of a list of many
  memoized rows cost a patch of every other row. The component, and a component below it that has an update
  pending, render as before; a component that is not memoized and sits below an element of the row is no longer
  rendered again by its ancestor's re-render, as with `React.memo`.
- Removing a row looks its element up at the index the removal already holds rather than searching the
  container's children for it, and a fiber appends a child without walking its existing children, so removing
  or creating many rows no longer grows with the square of the row count.
