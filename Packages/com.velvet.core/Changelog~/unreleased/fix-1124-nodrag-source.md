### Fixed

- Honor `FiberElementProps.NoDrag` on the draggable itself for presses on it or its children. Clearing
  the marker on a later render restores dragging, while a nested draggable keeps its own activation.
