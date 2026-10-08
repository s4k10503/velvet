### Fixed

- A `V.DragOverlay` that mounts during a drag, whether from the drag start callback's state update or a
  later render, shows the preview and tracks the pointer for the rest of that drag, as a dnd-kit
  `DragOverlay` mounted mid-drag does. It used to stay hidden until the next drag.
