### Added

- `V.VirtualList` takes `horizontal: true` to lay its items out in a row it scrolls sideways, FlashList's
  `horizontal`; `itemHeight` is then each item's width.
- `VirtualListHandle.ScrollToItem` takes a `VirtualListScrollBehavior`, react-window's `scrollToRow`
  `behavior`: `Smooth` animates the list to the item over 300 ms, and any other scroll along the list's axis
  ends the animation where it stands. `Auto`, the default, and `Instant` jump there as before.
