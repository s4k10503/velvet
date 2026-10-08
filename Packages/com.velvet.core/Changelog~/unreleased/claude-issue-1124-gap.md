### Fixed

- A `V.DragOverlay` that mounts during a drag shows the preview and tracks the pointer for the rest of that
  drag, as a dnd-kit `DragOverlay` mounted mid-drag does, instead of staying hidden until the next drag.
  One mounted by the drag start callback's state update shows at once; one a later render mounts shows from
  the drag's next pointer move.
