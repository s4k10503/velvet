### Changed

- A draggable whose own settings and scope both leave `activation:` unset starts its drag on the press,
  as dnd-kit's `PointerSensor` does with no activation constraint, so a press on a draggable button
  drags it rather than clicking it. The default used to be 4 px of travel; a scope or draggable that
  wants that back passes `activation: new DragActivation(Distance: 4f)`. `new DragActivation()` and
  `DragActivation.Default` now mean no constraint, the same as `DragActivation.None`.

- A press on an interactive child that captures the pointer at its own pointer-down, such as a button
  inside a draggable card, drags the card once the activation constraint is met, and the child's click
  is aborted. Such a child used to block the drag under a distance constraint.

- Every `V.DragOverlay` mounted in the tree when a drag starts shows the drag preview, as every dnd-kit
  `DragOverlay` under one context does. Only one of them used to show it, and mounting a second logged a
  warning.

### Fixed

- A drag pressed on a descendant of the draggable releases that descendant's `whileTap` class and
  `active:` variants when it ends. They stayed applied, since the session swallows the release before
  the descendant sees it and only the draggable and its ancestors were settled.
