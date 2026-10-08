### Fixed

- A `V.DragOverlay` that mounts during a drag, from the drag start callback's state update or a later
  render, shows the preview at the pointer on the commit that mounts it and tracks the pointer for the rest
  of that drag, as a dnd-kit `DragOverlay` mounted mid-drag does, instead of staying hidden until the next
  drag.
