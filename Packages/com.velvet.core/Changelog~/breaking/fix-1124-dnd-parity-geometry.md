### Changed

- A drag's translate starts at the press, as dnd-kit's does, rather than at the point the activation constraint
  was met. With a `DragActivation` distance or delay the source jumps by the travel made before activation on
  the first move after it, and `DragStartArgs.Origin`, `DragMoveArgs.Delta`, `DragOverArgs.Delta` and
  `DragEndArgs.Delta` count from the press. With no constraint the press is the activation and nothing differs.
- Every droppable is measured without the translate, scale and rotate of its own, as dnd-kit measures its
  droppables without their transforms, so a droppable moved by its own `translate` or `scale` collides where its
  layout box sits. Only a `V.Sortable` item was measured that way.
- A droppable in a `V.WorldSpace` panel is a collision candidate for a drag from another runtime panel of the
  tree, and a drag from a `V.WorldSpace` panel reaches the other panels' droppables, mapped through
  `Camera.main`; it was reachable only from a drag on its own panel.
