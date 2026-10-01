### Changed

- `whileHoverClass`, `whileTapClass` and `whileFocusClass` apply their classes as `hover:`, `active:` and
  `focus:` apply a payload, ranked after every rule the className declares with that variant alone, and the
  drag-and-drop channels `whileDraggingClass`, `whileOverClass` and `whileDragActiveClass` apply theirs the
  same way at `data-[…]:`'s rank. While the state is on, one outranks a utility of the element's own that
  carries no variant or `!`, for the same property, and, for the gesture channels, the className's rule for
  its own variant; Tailwind's order decides the rest, so a tap or focus class outranks a hover one and a
  className `focus:` rule outranks `whileHoverClass`. The element's own value comes back when the state goes
  off. A bracket value such as `bg-[#f00]` applies through them, and on an element so do `shadow-*`,
  `ring-*`, `skew-*`, gradients, `animate-*`, `border-dashed`, `gap-*` and `uppercase`. These channels used
  to rank with the element's own classes, so a base class the stylesheet declares later, a base bracket
  value or a divider's color kept the property, and a bracket value or a paint Velvet draws itself did
  nothing.

### Fixed

- A `V.Motion` hovered through a `hover:` rule of its className while its enter played kept the class of the
  pose it entered from once the pointer left, and showed that pose instead of the one it entered. A variant
  change played while a `V.Motion` was hovered let the new variant's class take the property from its
  `whileHoverClass` class. A class a play or an `AnimatePresence` exit puts on an element now ranks below a
  variant payload while it is there, as the element's own utilities do.
