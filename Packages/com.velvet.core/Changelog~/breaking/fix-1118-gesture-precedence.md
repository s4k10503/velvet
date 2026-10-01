### Changed

- `whileHoverClass`, `whileTapClass` and `whileFocusClass` apply their classes as `hover:`, `active:` and
  `focus:` apply a payload, after every rule the className declares at that rank, and the drag-and-drop
  channels `whileDraggingClass`, `whileOverClass` and `whileDragActiveClass` apply theirs the same way at
  `data-[…]:`'s rank. While the state is on, one outranks the element's own utility for the same property —
  for the gesture channels a `hover:` rule of the className included — and the element's own value comes back
  when it goes off; a tap or focus class outranks a hover one. A bracket value such as `bg-[#f00]` applies
  through them, and so do `shadow-*`, `ring-*`, `skew-*`, gradients, `animate-*`, `border-dashed`, `gap-*` and
  `uppercase`.
  These channels used to rank with the element's own classes, so a base class the stylesheet declares later,
  a base bracket value or a divider's color kept the property, and a bracket value or a paint Velvet draws
  itself did nothing.
