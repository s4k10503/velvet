### Fixed

- A time-sliced update of a keyed list (a transition, for example) no longer leaves an extra, empty element among
  the list's children when an `absolute` row stacked by `hover:z-*` stops being hovered while the update is paused
  after reaching that row.
