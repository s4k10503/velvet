### Changed

- A draggable whose own settings and scope both leave `activation:` unset starts its drag on the press,
  as dnd-kit's `PointerSensor` does with no activation constraint, so a press on a draggable button
  drags it rather than clicking it. The default used to be 4 px of travel; a scope or draggable that
  wants a press to stay a click below that travel passes `activation: new DragActivation(Distance: 4f)`.
  `new DragActivation()` and `DragActivation.Default` now mean no constraint, the same as
  `DragActivation.None`.

- A press on an interactive child that captures the pointer at its own pointer-down, such as a button
  inside a draggable card, drags the card once the activation constraint is met, and the child's click
  is aborted. Such a child used to block the drag, so a distance constraint alone does not bring the old
  behaviour back: a control inside a draggable that keeps its own pointer gesture (a slider, a scroller,
  a text field's selection) now needs `FiberElementProps.NoDrag` on it or on an element around it.

- When draggables nest, the innermost enabled one under the press takes it, as dnd-kit's innermost
  activator does. Under a distance constraint the inner one already won; with no constraint the outer
  one would otherwise activate first.

- Every `V.DragOverlay` declared under the dragging `V.DndContext` and mounted when the drag starts shows
  the drag preview, as every dnd-kit `DragOverlay` under that context does, and one under another scope
  stays hidden. Only one overlay in the tree used to show it, whichever scope it sat under, and mounting
  a second logged a warning.

### Added

- `FiberElementProps.NoDrag`: a press that starts on the element or inside it never arms a draggable
  enclosing it.

### Fixed

- A drag pressed on a descendant of the draggable releases that descendant's `whileTap` class and
  `active:` variants when it ends. They stayed applied, since the session swallows the release before
  the descendant sees it and only the draggable and its ancestors were settled.
