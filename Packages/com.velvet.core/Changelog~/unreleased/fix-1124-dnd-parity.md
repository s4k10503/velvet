### Fixed

- An element on a drag's press path that leaves the tree mid-drag, a pooled one included, drops the drag's
  callbacks and pointer hold at once and is left out of the release settle; it kept them until the drag
  ended, and the settle then settled whatever the element had become.
- A raw `DragOverlay` prop that a component mounts on its own re-render inside a portal pairs with the
  `V.DndContext` around the portal's call site and shows the drag; it stayed hidden.
- A draggable inside an engine `ListView` with `reorderable: true`, or inside a touch `ScrollView`, keeps its
  press: every move a drag observes carries UI Toolkit's draggable claim, so neither control starts a
  gesture of its own under it.
