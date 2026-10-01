### Added

- `V.VirtualList` takes its `itemHeight` as a function from an item's index to its height, react-window's
  `rowHeight` function, as well as a single height.
- `V.VirtualList`'s `listRef` holds a `VirtualListHandle` while the list is mounted. Its
  `ScrollToItem(index, align)` scrolls the list as react-window's `scrollToRow` does, `align` being
  `VirtualListAlign.Auto`, `Smart`, `Center`, `End` or `Start`, and its `Element` is the list's `ScrollView`.
- `V.List` logs a warning, once per component, when its `keySelector` returns `null`, as React warns about a
  list child with no key.

### Fixed

- Two siblings sharing a key in a container holding a `V.Fragment`, a `V.Provider` or a component each keep
  their own element across a render that leaves them, and every sibling before them, in the same order, as
  React's linear pass keeps them.
  The repeat mounted a fresh element on every render, and the first was patched onto the last one's element.
  The repeat is reported with a warning on every render, mount included.
- A `V.VirtualList` scrolled past its end — its items shrank while it was scrolled down — renders its last
  item. The range update threw.
